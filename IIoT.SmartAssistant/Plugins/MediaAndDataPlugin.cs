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


        [KernelFunction, Description("从本地资料库中检索并显示相关的架构图、照片或图纸。")]
        public string ShowDeviceImage([Description("想要查找的图片关键词或设备名称")] string keyword)
        {
            // 1. 获取 Data 目录
            string dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(dataFolder))
            {
                return "本地 Data 文件夹不存在，无法查询图片。";
            }

            // 2. 获取所有支持的图片文件（不限格式）
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var allFiles = Directory.GetFiles(dataFolder)
                                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                    .ToList();

            if (!allFiles.Any())
            {
                return "本地资料库中没有任何图片文件。";
            }

            // 3. 模糊匹配：只要文件名中包含 AI 提取的关键词即可
            string matchedFile = allFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));

            // 4. 如果找到了图片
            if (matchedFile != null)
            {
                _eventAggregator.GetEvent<MediaMessageEvent>().Publish(new ChatMessageItem
                {
                    Role = "AI",
                    MessageType = "Image",
                    Content = $"为您找到相关图片：{Path.GetFileName(matchedFile)}",
                    MediaPath = matchedFile
                });

                return $"已成功向用户展示了图片：{Path.GetFileName(matchedFile)}。请在回复中顺便提及你找到了这张图。";
            }
            else
            {
                // 5. 【核心智能体现】：如果没找到，把目录里实际有的图片名字告诉大模型！
                // 这样大模型就不会说“我不知道”，而是会说：“没找到您说的图片，但本地有 xxx.png 和 yyy.jpg，您要看哪一个？”
                var availableFileNames = allFiles.Select(Path.GetFileName).ToList();
                string filesListStr = string.Join(", ", availableFileNames);

                return $"未找到包含关键词 '{keyword}' 的图片。当前本地图库中仅包含以下文件：[{filesListStr}]。请委婉地告诉用户没找到，并向用户列出这些可用的图片供其选择。";
            }
        }
    }
}