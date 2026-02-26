using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IIoT.SmartAssistant.Models
{
    public class ChatMessageItem
    {
        public string Role { get; set; } // "User" 或 "AI"
        public string Content { get; set; } // 文本内容
        public string MessageType { get; set; } // "Text", "Image", "Video", "Chart"
        public string MediaPath { get; set; } // 媒体文件路径或流地址
    }
}
