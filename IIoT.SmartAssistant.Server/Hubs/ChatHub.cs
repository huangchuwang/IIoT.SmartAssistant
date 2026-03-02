using Microsoft.AspNetCore.SignalR;
using IIoT.SmartAssistant.Server.Services;

namespace IIoT.SmartAssistant.Server.Hubs
{
    public class ChatHub : Hub
    {
        private readonly AIChatService _chatService;

        public ChatHub(AIChatService chatService)
        {
            _chatService = chatService;
        }

        public IAsyncEnumerable<string> SendMessageStream(string message, string searchMode)
        {
            return _chatService.SendMessageStreamAsync(message, searchMode);
        }
    }
}