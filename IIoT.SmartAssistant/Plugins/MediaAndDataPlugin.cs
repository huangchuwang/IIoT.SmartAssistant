using System.ComponentModel;
using Microsoft.SemanticKernel;
using Prism.Events;
using IIoT.SmartAssistant.Models;
using System.IO;

namespace IIoT.SmartAssistant.Plugins
{
    public class MediaAndDataPlugin
    {
        private readonly IEventAggregator _eventAggregator;

        public MediaAndDataPlugin(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        [KernelFunction, Description("根据用户要求，调取并显示指定位置的监控视频画面。")]
        public string ShowSurveillanceCamera([Description("监控位置，例如：车间A、大门、流水线")] string location)
        {
            // 1. 模拟查找对应位置的摄像头 RTSP 流地址
            string rtspUrl = $"rtsp://admin:12345@192.168.1.100/{location}_stream";

            // 2. 通过 Prism 事件总线通知前台 UI 显示视频
            _eventAggregator.GetEvent<MediaMessageEvent>().Publish(new ChatMessageItem
            {
                Role = "AI",
                MessageType = "Video",
                Content = $"正在为您接入 {location} 的监控画面...",
                MediaPath = rtspUrl
            });

            // 3. 告诉大模型操作已完成，让大模型继续生成文本回复
            return $"系统已成功在屏幕上为用户展示了 {location} 的监控画面。";
        }

        [KernelFunction, Description("从本地资料库中检索并显示指定设备的架构图、照片或图纸。")]
        public string ShowDeviceImage([Description("设备名称或图纸类型")] string targetImage)
        {
            // 模拟本地查找图片路径 (实际应用中可以是检索本地库或 URL)
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", targetImage + ".jpg");
            _eventAggregator.GetEvent<MediaMessageEvent>().Publish(new ChatMessageItem
            {
                Role = "AI",
                MessageType = "Image",
                Content = $"这是您查询的 {targetImage} 图片：",
                MediaPath = imagePath
            });

            return "图片已成功展示给用户。";
        }
    }
}