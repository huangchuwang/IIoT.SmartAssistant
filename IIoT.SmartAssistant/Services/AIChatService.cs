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

            // 注册结构化数据库插件
            builder.Plugins.AddFromObject(new DynamicDatabasePlugin(eventAggregator), "DBOps");

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

            // 3. 初始化System Prompt提示词，定义数据库 Schema 提示词，让 AI 知道怎么写 SQL
            string dbSchemaPrompt = @"
                                        你是一个工业物联网与MES系统的数据分析专家。你可以编写 SQL Server (T-SQL) 语句来查询数据库，并分析数据。

                                        【绝对规则 - 必须遵守】：
                                        1. 只能使用下面列出的表名和字段名，**绝对严禁捏造、猜测或使用任何未列出的字段！
                                        2. 务必只生成 SELECT 语句，不要使用 UPDATE/DELETE。
                                        3. 与提问话题不相关的资料不需要回答或者不能多余的回答。

                                        【数据库真实 Schema】：
                                        1. ProductionData (生产数据表)
                                        - DeviceId (VARCHAR): 设备编号
                                        - OutputQuantity (INT): 产出数量
                                        - DefectQuantity (INT): 次品数量
                                        - RecordTime (DATETIME): 记录时间

                                        2. DeviceAlarms (设备报警表)
                                        - DeviceId (VARCHAR): 设备编号
                                        - AlarmCode (VARCHAR): 报警代码
                                        - DurationMinutes (INT): 停机分钟数
                                        - AlarmTime (DATETIME): 报警发生时间

                                        3. MesOrders (MES工单表)
                                        - OrderNo (VARCHAR): 工单号
                                        - ProductCode (VARCHAR): 产品编号
                                        - TargetQuantity (INT): 目标产量
                                        - CompletedQuantity (INT): 已完成数量
                                        - OrderStatus (VARCHAR): 状态(Pending, InProgress, Completed)
                                        - PlanStartTime (DATETIME): 计划开始时间（查询某日工单时用此字段过滤）
                                        - ActualEndTime (DATETIME): 实际结束时间

                                        4. MaterialInventory (物料库存主表)
                                        - MaterialCode (VARCHAR): 物料编号
                                        - MaterialName (NVARCHAR): 物料名称
                                        - CurrentStock (DECIMAL): 当前库存

                                        5. InventoryTransactions (出入库流水表)
                                        - MaterialCode (VARCHAR)
                                        - TransType (VARCHAR): 'IN' 入库，'OUT' 出库
                                        - Quantity (DECIMAL): 数量
                                        - TransTime (DATETIME): 交易时间
                                        - RelatedOrderNo (VARCHAR): 关联单号

                                        6. ProductionInputs (生产投入消耗表)
                                        - OrderNo (VARCHAR): 关联工单
                                        - DeviceId (VARCHAR): 生产设备
                                        - MaterialCode (VARCHAR): 消耗物料
                                        - ConsumedQuantity (DECIMAL): 消耗量
                                        - RecordTime (DATETIME): 投料记录时间
                                        ";

            _chatHistory = new ChatHistory(dbSchemaPrompt);

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
            //limit 增加到 3 甚至 5，稍微放宽匹配分数。
            var searchResults = _memory.SearchAsync(MemoryCollectionName, userMessage, limit: 3, minRelevanceScore: 0.15);

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

                        // 文档切片 Chunking
                        // 为了防止单次文本过长超出大模型的上下文限制，通常需要切片。
                        // 这里为了快速验证，采用最简单的“按页切片”（一页作为一个独立知识块）。
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