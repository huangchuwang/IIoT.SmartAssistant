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
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
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

        public AIChatService(
            IHubContext<ChatHub> hubContext,
            IOptions<AppConfig> configOptions,
            IConnectionMultiplexer redis,
            IConfiguration configuration)
        {
            AppConfig config = configOptions.Value;
            string apiKey = config.ApiKey;
            var aliHttpClient = new HttpClient { BaseAddress = new Uri(config.ApiUrl) };

            var builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(
                modelId: "deepseek-v3",
                apiKey: apiKey,
                httpClient: aliHttpClient);

            // 注册插件
            builder.Plugins.AddFromObject(new DeviceOpsPlugin(redis), "DeviceOps");
            builder.Plugins.AddFromObject(new MediaAndDataPlugin(hubContext, configuration), "MediaOps");
            builder.Plugins.AddFromObject(new DynamicDatabasePlugin(hubContext, configuration), "DBOps");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            _memory = new MemoryBuilder()
                .WithOpenAITextEmbeddingGeneration(
                    modelId: "text-embedding-v3",
                    apiKey: apiKey,
                    httpClient: aliHttpClient)
                .WithMemoryStore(new VolatileMemoryStore())
                .Build();

            // ==========================================
            // 【核心修改】为 AI 注入严格的图表 JSON 生成规则
            // ==========================================
            string dbSchemaPrompt = @"
你是一个工业物联网与MES系统的数据分析专家。你可以编写 SQL Server (T-SQL) 语句来查询数据库，并分析数据。

【绝对规则 - 必须遵守】：
1. 只能使用下面列出的表名和字段名，绝对严禁捏造、猜测或使用任何未列出的字段！
2. 务必只生成 SELECT 语句，不要使用 UPDATE/DELETE。
3. 与提问话题不相关的资料不需要回答或者不能多余的回答。

【图表生成规则 - 核心要求】：
如果用户明确要求生成图表（如折线图、柱状图、对比图等），你在调用 SQL 获取到数据后，必须且只能回复一段 JSON 格式的数据，绝对不要包含任何其他多余的解释文字、不要包含 markdown 标记 (如 ```json)！
JSON 的格式必须严格如下（注意键名大小写）：
{
    ""action"": ""render_chart"",
    ""chartType"": ""Bar"",   // 柱状图填 Bar，折线图填 Line
    ""title"": ""图表的主标题"",
    ""xAxis"": [""A线"", ""B线"", ""C线""], // X轴的标签数组 (字符串)
    ""series"": [25.0, 40.0, 15.0]        // 对应的 Y 轴数值数组 (浮点数)
}

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
                await _memory.SaveInformationAsync(MemoryCollectionName, para, $"chunk_{id++}");
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
    }
}