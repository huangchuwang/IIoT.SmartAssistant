using System.Net.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using IIoT.SmartAssistant.Plugins;

namespace IIoT.SmartAssistant.Services
{
    public class AIChatService2
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;
        private readonly ChatHistory _chatHistory;

        public AIChatService2()
        {
            // 1. 初始化 Kernel
            var builder = Kernel.CreateBuilder();

            // 2. 配置大模型接入（以硅基流动/DeepSeek为例，需替换为你申请的 API Key）
            builder.AddOpenAIChatCompletion(
                modelId: "deepseek-v3", // 替换为实际模型名称
                apiKey: "sk-",
                httpClient: new HttpClient { BaseAddress = new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1/") }
            );

            // 3. 将刚刚写的插件注入 Kernel
            builder.Plugins.AddFromType<DeviceOpsPlugin>("DeviceOps");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // 4. 设定 System Prompt
            _chatHistory = new ChatHistory("你是一个专业的工业上位机 AI 助手。必须使用提供的工具查询设备真实数据后才能回答用户。");
        }

        // 使用异步流实现“打字机”效果
        public async IAsyncEnumerable<string> SendMessageStreamAsync(string userMessage)
        {
            _chatHistory.AddUserMessage(userMessage);

            // 开启自动函数调用（Function Calling 核心配置）
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            string fullResponse = "";
            var responseStream = _chatService.GetStreamingChatMessageContentsAsync(_chatHistory, executionSettings, _kernel);

            await foreach (var chunk in responseStream)
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