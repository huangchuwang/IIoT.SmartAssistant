using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IIoT.SmartAssistant.Models
{
    public class ChatMessageItem : BindableBase
    {
        private string _role;
        private string _content;
        private string _messageType;
        private string _mediaPath;

        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        public string MediaPath
        {
            get => _mediaPath;
            set => SetProperty(ref _mediaPath, value);
        }
    }
}
