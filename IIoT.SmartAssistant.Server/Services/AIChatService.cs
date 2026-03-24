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
        private const string MemoryCollectionName = "DeviceManual";
        private const string EmbeddingModelId = "text-embedding-v3";
        private const string ChatModelId = "deepseek-v3";
        private const string VlmModelId = "qwen-image-max";
        private const string AliCloudApiUrl = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        private const int MaxChunkLength = 500;
        private const int SearchResultsLimit = 3;
        private const double MinRelevanceScore = 0.15;

        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        private readonly HttpClient _aliHttpClient;

        private readonly string _filePath;
        private readonly string _promptFilePath;

        private readonly ISemanticTextMemory _memory;

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
                .WithOpenAITextEmbeddingGeneration(EmbeddingModelId, apiKey: _apiKey, httpClient: _aliHttpClient)
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
            var files = Directory.GetFiles(_filePath, "*.*", SearchOption.AllDirectories);
            int chunkId = 1;

            var fileExtractors = new Dictionary<string, Func<string, Task<string>>>
            {
                { ".txt", async (path) => await File.ReadAllTextAsync(path) },
                { ".md", async (path) => await File.ReadAllTextAsync(path) },
                { ".csv", async (path) => await File.ReadAllTextAsync(path) },
                { ".json", async (path) => await File.ReadAllTextAsync(path) },
                { ".pdf", (path) => Task.FromResult(ExtractTextFromPdf(path)) },
                { ".docx", (path) => Task.FromResult(ExtractTextFromWord(path)) },
                { ".xlsx", (path) => Task.FromResult(ExtractTextFromExcel(path)) },
                { ".jpg", async (path) => await AnalyzeImageWithVlmAsyncWrapper(path) },
                { ".png", async (path) => await AnalyzeImageWithVlmAsyncWrapper(path) },
                { ".bmp", async (path) => await AnalyzeImageWithVlmAsyncWrapper(path) }
            };

            foreach (var file in files)
            {
                string extension = Path.GetExtension(file).ToLower();
                if (!fileExtractors.TryGetValue(extension, out var extractor))
                {
                    continue;
                }

                try
                {
                    string extractedText = await extractor(file);
                    if (string.IsNullOrWhiteSpace(extractedText)) continue;

                    var chunks = SplitTextIntoChunks(extractedText, maxChunkLength: MaxChunkLength);

                    foreach (var chunk in chunks)
                    {
                        await _memory.SaveInformationAsync(
                            collection: MemoryCollectionName,
                            text: chunk,
                            id: $"doc_chunk_{chunkId++}",
                            description: Path.GetFileName(file)
                        );
                    }

                    Console.WriteLine($"[知识库] 成功加载并向量化文件: {Path.GetFileName(file)}，切片数量: {chunks.Count}");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[知识库] 文件读取异常: {file}, 错误: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[知识库] 解析文件时发生未知异常: {file}, 错误: {ex.Message}");
                }
            }
        }

        private async Task<string> AnalyzeImageWithVlmAsyncWrapper(string path)
        {
            Console.WriteLine($"[视觉大模型] 正在识别图片内容: {Path.GetFileName(path)}");
            string extractedText = await AnalyzeImageWithVlmAsync(path);
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                return $"[图片文件: {Path.GetFileName(path)}] 画面内容描述：\n" + extractedText;
            }
            return extractedText;
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
                    model = VlmModelId, // 阿里云视觉大模型
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

                using var request = new HttpRequestMessage(HttpMethod.Post, AliCloudApiUrl);
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
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[视觉大模型HTTP请求异常]: {ex.Message}");
                return "图片内容解析失败，网络请求出错。";
            }
            catch (System.Text.Json.JsonException ex)
            {
                Console.WriteLine($"[视觉大模型JSON解析异常]: {ex.Message}");
                return "图片内容解析失败，解析响应时出错。";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[视觉大模型未知异常]: {ex.Message}");
                return "图片内容解析失败，发生未知错误。";
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

        
public enum SearchMode
{
    Docs,
    DB,
    IoT,
    Auto
}
public async IAsyncEnumerable<string> SendMessageStreamAsync(string userMessage, string searchMode)
{
    var builder = Kernel.CreateBuilder();
    builder.AddOpenAIChatCompletion(modelId: ChatModelId, apiKey: _apiKey, httpClient: _aliHttpClient);

    var modeMapping = new Dictionary<string, (SearchMode Mode, string PromptFile)>
    {
        { "知识问答 (Docs)", (SearchMode.Docs, "DocsMode.txt") },
        { "数据报表 (DB)", (SearchMode.DB, "DbMode.txt") },
        { "设备控制 (IoT)", (SearchMode.IoT, "IoTMode.txt") },
        { "全局智能 (Auto)", (SearchMode.Auto, "AutoMode.txt") }
    };

    if (!modeMapping.TryGetValue(searchMode, out var modeInfo))
    {
        // Default to Auto mode if the searchMode string is not recognized
        modeInfo = (SearchMode.Auto, "AutoMode.txt");
    }

    string systemPrompt = LoadPromptFromFile(modeInfo.PromptFile);

    // Configure plugins based on the search mode
    switch (modeInfo.Mode)
    {
        case SearchMode.Docs:
            builder.Plugins.AddFromObject(new FileSearchPlugin(_filePath, "http://localhost:5109"), "FileSearch");
            break;
        case SearchMode.DB:
            builder.Plugins.AddFromObject(new DynamicDatabasePlugin(_hubContext, _configuration), "DBOps");
            break;
        case SearchMode.IoT:
            builder.Plugins.AddFromObject(new DeviceOpsPlugin(_redis), "DeviceOps");
            builder.Plugins.AddFromObject(new MediaAndDataPlugin(_hubContext, _configuration), "MediaOps");
            break;
        case SearchMode.Auto:
            builder.Plugins.AddFromObject(new DeviceOpsPlugin(_redis), "DeviceOps");
            builder.Plugins.AddFromObject(new MediaAndDataPlugin(_hubContext, _configuration), "MediaOps");
            builder.Plugins.AddFromObject(new DynamicDatabasePlugin(_hubContext, _configuration), "DBOps");
            builder.Plugins.AddFromObject(new FileSearchPlugin(_filePath, "http://localhost:5109"), "FileSearch");
            break;
    }

    var kernel = builder.Build();
    var chatService = kernel.GetRequiredService<IChatCompletionService>();
    var chatHistory = new ChatHistory(systemPrompt);

    string referenceContext = "";
    if (modeInfo.Mode == SearchMode.Auto || modeInfo.Mode == SearchMode.Docs)
    {
        var searchResults = _memory.SearchAsync(MemoryCollectionName, userMessage, limit: SearchResultsLimit, minRelevanceScore: MinRelevanceScore);
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