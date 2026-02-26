#pragma warning disable SKEXP0001, SKEXP0011 // 同时忽略内存 API 和 OpenAI 连接器的实验性警告
using IIoT.SmartAssistant.Models;
using IIoT.SmartAssistant.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Prism.Events;
using System.IO;
using System.Net.Http;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace IIoT.SmartAssistant.Services
{
    public class AIChatService
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;
        private readonly ChatHistory _chatHistory;

        // 新增：内存/向量检索核心接口
        private readonly ISemanticTextMemory _memory;
        private const string MemoryCollectionName = "DeviceManual";

        public AIChatService(IEventAggregator eventAggregator)
        {
            AppConfig config = AppConfig.Load();
            string apiKey = config.ApiKey;
            var aliHttpClient = new HttpClient { BaseAddress = new Uri(config.ApiUrl) };

            // 1. 初始化 Kernel 构建器
            var builder = Kernel.CreateBuilder();

            // 对话模型
            builder.AddOpenAIChatCompletion(
                modelId: "deepseek-v3",
                apiKey: apiKey,
                httpClient: aliHttpClient);

            // 注册基础设备插件
            builder.Plugins.AddFromType<DeviceOpsPlugin>("DeviceOps");

            // 注册多媒体与数据插件，并将事件聚合器传给它
            builder.Plugins.AddFromObject(new MediaAndDataPlugin(eventAggregator), "MediaOps");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // 2. 初始化知识库内存服务 (这段代码丢失会导致 _memory 为 null，从而引发你遇到的报错)
            _memory = new MemoryBuilder()
                .WithOpenAITextEmbeddingGeneration(
                    modelId: "text-embedding-v3", // 阿里云百炼的向量模型
                    apiKey: apiKey,
                    httpClient: aliHttpClient)
                .WithMemoryStore(new VolatileMemoryStore()) // 内存级向量库，适合边缘端轻量级验证
                .Build();

            // 3. 初始化提示词
            _chatHistory = new ChatHistory("你是一个工业物联网智能助手。你可以回答问题，也可以根据要求调用工具来显示监控、图片或分析数据。如果用户要看监控，请务必调用对应的视频显示函数。");

            // 4. 异步加载本地知识库 (保留你原本正在使用的那个方法)
            _ = LoadKnowledgeBaseAsync();
        }

        /// <summary>
        /// 加载并切片 TXT 文档
        /// </summary>
        /// <returns></returns>
        private async Task LoadKnowledgeBaseAsync()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "DeviceManual.txt");
            if (!File.Exists(filePath)) return;

            string[] paragraphs = await File.ReadAllLinesAsync(filePath);
            int id = 1;

            foreach (var para in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(para)) continue;

                // 将段落向量化并存入内存
                await _memory.SaveInformationAsync(
                    collection: MemoryCollectionName,
                    text: para,
                    id: $"chunk_{id++}");
            }
        }



        public async IAsyncEnumerable<string> SendMessageStreamAsync(string userMessage)
        {
            // 3. RAG 核心逻辑：在提问前，先去内存中进行向量检索
            var searchResults = _memory.SearchAsync(MemoryCollectionName, userMessage, limit: 1, minRelevanceScore: 0.3);

            string referenceContext = "";
            await foreach (var result in searchResults)
            {
                referenceContext += result.Metadata.Text + "\n";
            }

            // 4. 将检索到的知识库内容拼接到提示词中
            string prompt = userMessage;
            if (!string.IsNullOrEmpty(referenceContext))
            {
                prompt = $"请根据以下参考资料回答用户问题：\n【参考资料】\n{referenceContext}\n【用户问题】\n{userMessage}";
            }

            _chatHistory.AddUserMessage(prompt);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            string fullResponse = "";
            await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(_chatHistory, executionSettings, _kernel))
            {
                if (chunk.Content != null)
                {
                    fullResponse += chunk.Content;
                    yield return chunk.Content;
                }
            }

            _chatHistory.AddAssistantMessage(fullResponse);
        }        
        
        
        /// <summary>
        /// 加载并解析本地 PDF 说明书
        /// </summary>
        /// <returns></returns>
        private async Task LoadKnowledgeBasePDFAsync()
        {
            // 替换为你真实的 PDF 文件名
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "AC-4100S检重控制软件使用说明书.pdf");
            if (!File.Exists(filePath))
            {
                // 如果找不到文件，就在历史记录里报个错方便排查
                _chatHistory.AddSystemMessage("警告：未找到本地知识库文件。");
                return;
            }

            try
            {
                // 使用 PdfPig 打开 PDF 文件
                using (PdfDocument document = PdfDocument.Open(filePath))
                {
                    // 遍历 PDF 的每一页
                    foreach (Page page in document.GetPages())
                    {
                        // 提取当前页的纯文本
                        string text = page.Text;

                        if (string.IsNullOrWhiteSpace(text)) continue;

                        // 【核心概念：文档切片 Chunking】
                        // 为了防止单次文本过长超出大模型的上下文限制，通常需要切片。
                        // 这里为了快速验证，我们采用最简单的“按页切片”（一页作为一个独立知识块）。
                        // 将每一页的文本向量化并存入内存
                        await _memory.SaveInformationAsync(
                            collection: MemoryCollectionName,
                            text: text,
                            id: $"page_{page.Number}", // 用页码作为这条知识的唯一 ID
                            description: $"说明书第{page.Number}页内容"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _chatHistory.AddSystemMessage($"解析PDF时发生错误: {ex.Message}");
            }
        }
    }
}