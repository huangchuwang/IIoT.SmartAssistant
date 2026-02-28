using Prism.Mvvm;

namespace IIoT.SmartAssistant.Models
{
    // 继承 BindableBase，使属性具备通知 UI 刷新的能力
    public class ChatMessageItem : BindableBase
    {
        private string _role;
        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        private string _content;
        public string Content
        {
            get => _content;
            // 当 Content 内容增加时（比如流式接收字），自动通知 WPF 界面刷新
            set => SetProperty(ref _content, value);
        }

        private string _messageType;
        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        private string _mediaPath;
        public string MediaPath
        {
            get => _mediaPath;
            set => SetProperty(ref _mediaPath, value);
        }
    }
}