#pragma warning disable SKEXP0001, SKEXP0011 
using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models;
using IIoT.SmartAssistant.Server.Plugins;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.XWPF.UserModel;
using StackExchange.Redis;
using UglyToad.PdfPig;
using ICell = NPOI.SS.UserModel.ICell;

namespace IIoT.SmartAssistant.Server.Services
{
    public class AIChatService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        private readonly HttpClient _aliHttpClient;

        private readonly string _filePath;
        private readonly string _promptFilePath;

        private readonly ISemanticTextMemory _memory;
        private const string MemoryCollectionName = "DeviceManual";

        public AIChatService(
            IHubContext<ChatHub> hubContext,
            IOptions<AppConfig> configOptions,
            IConnectionMultiplexer redis,
            IConfiguration configuration)
        {
            _hubContext = hubContext;
            _redis = redis;
            _configuration = configuration;

            AppConfig config = configOptions.Value;
            _apiKey = config.ApiKey;
            _aliHttpClient = new HttpClient { BaseAddress = new Uri(config.ApiUrl) };

            _filePath = config.FilePath;
            _promptFilePath = config.PromptFilePath;

            _memory = new MemoryBuilder()
                .WithOpenAITextEmbeddingGeneration("text-embedding-v3", apiKey: _apiKey, httpClient: _aliHttpClient)
                .WithMemoryStore(new VolatileMemoryStore())
                .Build();

            _ = LoadKnowledgeBaseAsync();
        }

        private async Task LoadKnowledgeBaseAsync()
        {
            if (string.IsNullOrWhiteSpace(_filePath) || !Directory.Exists(_filePath))
            {
                Console.WriteLine($"[知识库] 目录不存在: {_filePath}");
                return;
            }

            Console.WriteLine($"[知识库] 开始扫描目录: {_filePath}");
            // 递归获取目录下所有的文件
            var files = Directory.GetFiles(_filePath, "*.*", SearchOption.AllDirectories);
            int chunkId = 1;

            foreach (var file in files)
            {
                string extension = Path.GetExtension(file).ToLower();
                string extractedText = string.Empty;

                try
                {
                    //根据后缀名提取纯文本
                    switch (extension)
                    {
                        case ".txt":
                        case ".md":
                        case ".csv":
                        case ".json":
                            extractedText = await File.ReadAllTextAsync(file);
                            break;

                        case ".pdf":
                            // 使用 PdfPig 解析 PDF 文本
                            extractedText = ExtractTextFromPdf(file);
                            break;

                        case ".docx":
                            extractedText = ExtractTextFromWord(file);
                            break;

                        case ".xlsx":
                            extractedText = ExtractTextFromExcel(file);
                            break;

                        case ".jpg":
                        case ".png":
                        case ".bmp":
                            // 调用视觉大模型生成图片描述
                            Console.WriteLine($"[视觉大模型] 正在识别图片内容: {Path.GetFileName(file)}");
                            extractedText = await AnalyzeImageWithVlmAsync(file);
                            // 为了让大模型知道这是一个可以下载的图片，我们强行给描述加一个前缀
                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                extractedText = $"[图片文件: {Path.GetFileName(file)}] 画面内容描述：\n" + extractedText;
                            }
                            break;

                        default:
                            continue;
                    }

                    if (string.IsNullOrWhiteSpace(extractedText)) continue;

                    // 文本切块器 Chunking
                    // 将动辄几万字的长文档，切分为 500 字左右的片段，否则向量模型会内存溢出
                    var chunks = SplitTextIntoChunks(extractedText, maxChunkLength: 500);

                    //向量化与入库
                    foreach (var chunk in chunks)
                    {
                        // 将文件名作为 description 元数据存入，方便后续知道答案来自哪个文件
                        await _memory.SaveInformationAsync(
                            collection: MemoryCollectionName,
                            text: chunk,
                            id: $"doc_chunk_{chunkId++}",
                            description: Path.GetFileName(file)
                        );
                    }

                    Console.WriteLine($"[知识库] 成功加载并向量化文件: {Path.GetFileName(file)}，切片数量: {chunks.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[知识库] 解析文件异常: {file}, 错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// PDF 解析器实现
        /// </summary>
        private string ExtractTextFromPdf(string filePath)
        {
            using PdfDocument document = PdfDocument.Open(filePath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Word (.docx) 解析器
        /// </summary>
        private string ExtractTextFromWord(string filePath)
        {
            var sb = new System.Text.StringBuilder();
            using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                XWPFDocument document = new XWPFDocument(file);

                // 提取普通段落文本
                foreach (var para in document.Paragraphs)
                {
                    if (!string.IsNullOrWhiteSpace(para.ParagraphText))
                    {
                        sb.AppendLine(para.ParagraphText);
                    }
                }

                // 提取 Word 中的表格数据 (极其重要，工业文档里全都是参数表)
                foreach (var table in document.Tables)
                {
                    sb.AppendLine("\n[Word表格数据]:");
                    foreach (var row in table.Rows)
                    {
                        var cellValues = new List<string>();
                        foreach (var cell in row.GetTableCells())
                        {
                            // 替换掉单元格内多余的换行符，防止破坏表格结构
                            cellValues.Add(cell.GetText().Replace("\n", " ").Replace("\r", ""));
                        }
                        // 用竖线分割，让大模型能精准识别这是一行数据
                        sb.AppendLine(string.Join(" | ", cellValues));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Excel (.xlsx) 解析器
        /// </summary>
        private string ExtractTextFromExcel(string filePath)
        {
            var sb = new System.Text.StringBuilder();
            using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(file);

                // 遍历所有的 Sheet 表
                for (int i = 0; i < workbook.NumberOfSheets; i++)
                {
                    ISheet sheet = workbook.GetSheetAt(i);
                    if (sheet == null) continue;

                    sb.AppendLine($"\n[Excel工作表: {sheet.SheetName}]:");

                    // 遍历所有行
                    for (int rowIdx = 0; rowIdx <= sheet.LastRowNum; rowIdx++)
                    {
                        IRow row = sheet.GetRow(rowIdx);
                        if (row == null) continue;

                        var cellValues = new List<string>();
                        // 遍历所有列
                        for (int cellIdx = 0; cellIdx < row.LastCellNum; cellIdx++)
                        {
                            ICell cell = row.GetCell(cellIdx);
                            cellValues.Add(cell?.ToString()?.Replace("\n", " ") ?? "");
                        }

                        // 同样使用竖线分割，转化为 Markdown 风格的大模型友好格式
                        string rowText = string.Join(" | ", cellValues);
                        if (!string.IsNullOrWhiteSpace(rowText.Replace("|", "").Trim()))
                        {
                            sb.AppendLine(rowText);
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private string LoadPromptFromFile(string promptFileName)
        {
            string filePath = Path.Combine(_promptFilePath, promptFileName);
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            // 兜底策略：如果文件丢失，给一个基础设定
            return "你是一个专业的工业物联网智能助手。";
        }

        /// <summary>
        /// 调用阿里云 Qwen-VL 视觉大模型识别图片内容
        /// </summary>
        private async Task<string> AnalyzeImageWithVlmAsync(string filePath)
        {
            try
            {
                // 将本地图片转换为 Base64 格式
                byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";

                string dataUri = $"data:{mimeType};base64,{base64Image}";

                // 构建阿里云 DashScope 多模态请求的 JSON 结构
                var requestPayload = new
                {
                    model = "qwen-image-max", // 阿里云视觉大模型
                    input = new
                    {
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { image = dataUri },
                                  new { text = "请极其详细地描述这张图片里的所有内容，包括人物、物体、颜色、背景、是否有异常或特殊标志。直接输出描述，不要废话。" }
                                }
                            }
                        }
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");

                string jsonBody = System.Text.Json.JsonSerializer.Serialize(requestPayload);
                request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                using var response = await _aliHttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    // 解析阿里云返回的 JSON
                    using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
                    var outputText = doc.RootElement
                        .GetProperty("output")
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")[0]
                        .GetProperty("text").GetString();

                    return outputText ?? "";
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[视觉大模型报错]: {error}");
                    return "图片内容解析失败。";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[视觉大模型异常]: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 文本切块算法 (按换行符和最大长度切分)
        /// </summary>
        private List<string> SplitTextIntoChunks(string text, int maxChunkLength)
        {
            var chunks = new List<string>();
            // 按段落切分
            var paragraphs = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string currentChunk = "";
            foreach (var para in paragraphs)
            {
                if ((currentChunk.Length + para.Length) > maxChunkLength && !string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                    currentChunk = "";
                }
                currentChunk += para + "\n";
            }
            if (!string.IsNullOrEmpty(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }
            return chunks;
        }

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string userMessage, string searchMode)
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "deepseek-v3", apiKey: _apiKey, httpClient: _aliHttpClient);

            string systemPrompt = "";

            if (searchMode == "知识问答 (Docs)")
            {
                builder.Plugins.AddFromObject(new FileSearchPlugin(_filePath, "http://localhost:5109"), "FileSearch");
                systemPrompt = LoadPromptFromFile("DocsMode.txt"); 
            }
            else if (searchMode == "数据报表 (DB)")
            {
                builder.Plugins.AddFromObject(new DynamicDatabasePlugin(_hubContext, _configuration), "DBOps");
                systemPrompt = LoadPromptFromFile("DbMode.txt"); 
            }
            else if (searchMode == "设备控制 (IoT)")
            {
                builder.Plugins.AddFromObject(new DeviceOpsPlugin(_redis), "DeviceOps");
                builder.Plugins.AddFromObject(new MediaAndDataPlugin(_hubContext, _configuration), "MediaOps");
                systemPrompt = LoadPromptFromFile("IoTMode.txt"); 
            }
            else // "全局智能 (Auto)"
            {
                builder.Plugins.AddFromObject(new DeviceOpsPlugin(_redis), "DeviceOps");
                builder.Plugins.AddFromObject(new MediaAndDataPlugin(_hubContext, _configuration), "MediaOps");
                builder.Plugins.AddFromObject(new DynamicDatabasePlugin(_hubContext, _configuration), "DBOps");
                builder.Plugins.AddFromObject(new FileSearchPlugin(_filePath, "http://localhost:5109"), "FileSearch");

                systemPrompt = LoadPromptFromFile("AutoMode.txt"); 
            }

            var kernel = builder.Build();
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory(systemPrompt);

            string referenceContext = "";
            if (searchMode == "全局智能 (Auto)" || searchMode == "知识问答 (Docs)")
            {
                var searchResults = _memory.SearchAsync(MemoryCollectionName, userMessage, limit: 3, minRelevanceScore: 0.15);
                await foreach (var result in searchResults)
                {
                    referenceContext += result.Metadata.Text + "\n";
                }
            }

            string finalPrompt = userMessage;
            if (!string.IsNullOrEmpty(referenceContext))
            {
                finalPrompt = $"请根据以下参考资料回答用户问题：\n【参考资料】\n{referenceContext}\n【用户问题】\n{userMessage}";
            }

            chatHistory.AddUserMessage(finalPrompt);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel))
            {
                if (chunk.Content != null)
                {
                    yield return chunk.Content;
                }
            }
        }
    }
}