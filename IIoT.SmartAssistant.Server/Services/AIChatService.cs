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

            // 替换 AIChatService.cs 里面的 dbSchemaPrompt 变量定义为：
            string dbSchemaPrompt = @"
                                    你是一个工业物联网与MES系统的数据分析专家。你可以编写 SQL Server (T-SQL) 语句来查询数据库，并分析数据。

                                    【绝对规则 - 必须遵守】：
                                    1. 只能使用下面【数据库真实 Schema】中列出的表名和字段名，绝对严禁捏造、猜测或使用任何未列出的字段！(特别注意：字段名是 DeviceId，包含大写字母 I，绝不能错拼成 Deviceld)。
                                    2. 务必只生成 SELECT 语句，不要使用 UPDATE/DELETE。
                                    3. 编写 SQL 语句时，关键字之间必须严格保留空格（例如 FROM 表名 后面必须加空格再写 GROUP BY，绝不能粘连写成 FROM ProductionDataGROUP BY）。

                                    【回复规则】：
                                    当你获取到底层 SQL 数据后，请用清晰、专业的语言直接向用户汇报分析结果，不要包含任何画图指令。

                                    【数据库真实 Schema】：
                                    （如果你要查询自己的真实数据，请将以下结构替换为你数据库中真实存在的表和字段！）

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