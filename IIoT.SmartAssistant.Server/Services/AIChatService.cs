#pragma warning disable SKEXP0001, SKEXP0011 // 同时忽略内存 API 和 OpenAI 连接器的实验性警告
using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models;
using IIoT.SmartAssistant.Server.Plugins;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace IIoT.SmartAssistant.Server.Services
{
    public class AIChatService
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;
        private readonly ChatHistory _chatHistory;
        private readonly ISemanticTextMemory _memory;
        private const string MemoryCollectionName = "DeviceManual";

        public AIChatService(IHubContext<ChatHub> hubContext, IOptions<AppConfig> configOptions)
        {
            AppConfig config = configOptions.Value;
            string apiKey = config.ApiKey;
            var aliHttpClient = new HttpClient { BaseAddress = new Uri(config.ApiUrl) };

            var builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(
                modelId: "deepseek-v3",
                apiKey: apiKey,
                httpClient: aliHttpClient);

            builder.Plugins.AddFromType<DeviceOpsPlugin>("DeviceOps");

            // 将 hubContext 传递给需要主动推送消息到前台的插件
            builder.Plugins.AddFromObject(new MediaAndDataPlugin(hubContext), "MediaOps");
            builder.Plugins.AddFromObject(new DynamicDatabasePlugin(hubContext), "DBOps");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            _memory = new MemoryBuilder()
                .WithOpenAITextEmbeddingGeneration(
                    modelId: "text-embedding-v3",
                    apiKey: apiKey,
                    httpClient: aliHttpClient)
                .WithMemoryStore(new VolatileMemoryStore())
                .Build();

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
                                        - PlanStartTime (DATETIME): 计划开始时间
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

            _ = LoadKnowledgeBaseAsync();
        }

        private async Task LoadKnowledgeBaseAsync()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "DeviceManual.txt");
            if (!File.Exists(filePath)) return;

            string[] paragraphs = await File.ReadAllLinesAsync(filePath);
            int id = 1;

            foreach (var para in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(para)) continue;

                await _memory.SaveInformationAsync(
                    collection: MemoryCollectionName,
                    text: para,
                    id: $"chunk_{id++}");
            }
        }

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string userMessage)
        {
            var searchResults = _memory.SearchAsync(MemoryCollectionName, userMessage, limit: 3, minRelevanceScore: 0.15);

            string referenceContext = "";
            await foreach (var result in searchResults)
            {
                referenceContext += result.Metadata.Text + "\n";
            }

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

        private async Task LoadKnowledgeBasePDFAsync()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "AC-4100S检重控制软件使用说明书.pdf");
            if (!File.Exists(filePath))
            {
                _chatHistory.AddSystemMessage("警告：未找到本地知识库文件。");
                return;
            }

            try
            {
                using (PdfDocument document = PdfDocument.Open(filePath))
                {
                    foreach (Page page in document.GetPages())
                    {
                        string text = page.Text;
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        await _memory.SaveInformationAsync(
                            collection: MemoryCollectionName,
                            text: text,
                            id: $"page_{page.Number}",
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