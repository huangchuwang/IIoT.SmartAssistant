using Prism.Events;

namespace IIoT.SmartAssistant.Models
{
    // 定义一个通知 UI 更新媒体消息的事件
    public class MediaMessageEvent : PubSubEvent<ChatMessageItem> { }
}
