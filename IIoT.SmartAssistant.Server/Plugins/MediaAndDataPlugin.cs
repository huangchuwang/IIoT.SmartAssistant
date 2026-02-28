using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class MediaAndDataPlugin
    {
        private readonly IHubContext<ChatHub> _hubContext;

        public MediaAndDataPlugin(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        [KernelFunction, Description("根据用户要求，调取并显示指定位置的监控视频画面。")]
        public async Task<string> ShowSurveillanceCameraAsync([Description("监控位置，例如：车间A、大门")] string location)
        {
            string rtspUrl = $"rtsp://admin:12345@192.168.1.100/{location}_stream";

            await _hubContext.Clients.All.SendAsync("ReceiveMediaMessage", new ChatMessageItem
            {
                Role = "AI",
                MessageType = "Video",
                Content = $"正在为您接入 {location} 的监控画面...",
                MediaPath = rtspUrl
            });

            return $"系统已成功在屏幕上为用户展示了 {location} 的监控画面。";
        }

        [KernelFunction, Description("从本地资料库中检索并显示相关的架构图、照片或图纸。")]
        public async Task<string> ShowDeviceImage([Description("想要查找的图片关键词或设备名称")] string keyword)
        {
            string dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(dataFolder))
            {
                return "本地 Data 文件夹不存在，无法查询图片。";
            }

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var allFiles = Directory.GetFiles(dataFolder)
                                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                    .ToList();

            if (!allFiles.Any())
            {
                return "本地资料库中没有任何图片文件。";
            }

            string matchedFile = allFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (matchedFile != null)
            {
                // 使用 SignalR 发送图片消息
                await _hubContext.Clients.All.SendAsync("ReceiveMediaMessage", new ChatMessageItem
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
                var availableFileNames = allFiles.Select(Path.GetFileName).ToList();
                string filesListStr = string.Join(", ", availableFileNames);

                return $"未找到包含关键词 '{keyword}' 的图片。当前本地图库中仅包含以下文件：[{filesListStr}]。请委婉地告诉用户没找到，并向用户列出这些可用的图片供其选择。";
            }
        }
    }
}