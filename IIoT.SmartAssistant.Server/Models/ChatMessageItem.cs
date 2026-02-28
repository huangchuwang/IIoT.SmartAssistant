namespace IIoT.SmartAssistant.Server.Models
{
    public class ChatMessageItem
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public string MessageType { get; set; }
        public string MediaPath { get; set; }
    }
}