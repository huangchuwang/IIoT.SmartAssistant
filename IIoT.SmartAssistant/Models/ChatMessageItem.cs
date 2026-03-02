using Prism.Mvvm;

namespace IIoT.SmartAssistant.Models
{
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

        // ▼▼ 新增的两个文件下载专用属性 ▼▼
        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        private string _fileUrl;
        public string FileUrl
        {
            get => _fileUrl;
            set => SetProperty(ref _fileUrl, value);
        }
    }
}