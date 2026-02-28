using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration; // 新增

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class MediaAndDataPlugin
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly string _connectionString;

        // 注入 Configuration，从 appsettings.json 读取数据库连接
        public MediaAndDataPlugin(IHubContext<ChatHub> hubContext, IConfiguration config)
        {
            _hubContext = hubContext;
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        [KernelFunction, Description("根据用户要求，调取并显示指定位置的监控视频画面。")]
        public async Task<string> ShowSurveillanceCameraAsync([Description("监控位置，例如：车间A、大门")] string location)
        {
            string rtspUrl = string.Empty;

            try
            {
                // 去 SQL 数据库中检索真实的 RTSP 地址
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                string sql = "SELECT RtspUrl FROM SurveillanceCameras WHERE LocationName = @Location AND Status = 'Online'";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Location", location);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null) rtspUrl = result.ToString();
            }
            catch (Exception ex)
            {
                return $"查询监控配置数据库失败：{ex.Message}";
            }

            if (string.IsNullOrEmpty(rtspUrl))
            {
                return $"未能找到【{location}】的在线监控配置，请检查区域名称是否正确。";
            }

            await _hubContext.Clients.All.SendAsync("ReceiveMediaMessage", new ChatMessageItem
            {
                Role = "AI",
                MessageType = "Video",
                Content = $"正在为您接入 {location} 的监控画面...",
                MediaPath = rtspUrl
            });

            return $"系统已成功在屏幕上为用户展示了 {location} 的监控画面 (流地址: {rtspUrl})。";
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

                return $"已成功向用户展示了图片：{Path.GetFileName(matchedFile)}。请在回复中顺便提及找到了这张图。";
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