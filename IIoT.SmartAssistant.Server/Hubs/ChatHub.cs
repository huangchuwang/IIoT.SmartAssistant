using IIoT.SmartAssistant.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace IIoT.SmartAssistant.Server.Hubs
{
    // Hub 是 SignalR 用于和客户端实时双向通信的核心类
    public class ChatHub : Hub
    {
        private readonly AIChatService _aiService;

        public ChatHub(AIChatService aiService)
        {
            _aiService = aiService;
        }

        // 接收客户端问题，并通过 IAsyncEnumerable 实时流式返回 AI 的回复
        public async IAsyncEnumerable<string> SendMessageStream(string message, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var chunk in _aiService.SendMessageStreamAsync(message))
            {
                if (cancellationToken.IsCancellationRequested) break;
                yield return chunk;
            }
        }
    }
}