using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class FileSearchPlugin
    {
        private readonly string _directory;
        private readonly string _serverBaseUrl;

        public FileSearchPlugin(string directory, string serverBaseUrl)
        {
            _directory = directory;
            _serverBaseUrl = serverBaseUrl;
        }

        [KernelFunction, Description("根据用户提供的文件名或关键词，在服务器的文件库中检索匹配的文件，并生成下载卡片。")]
        public string SearchFiles([Description("要搜索的文件名或关键词")] string keyword)
        {
            if (string.IsNullOrWhiteSpace(_directory) || !Directory.Exists(_directory))
                return "服务器未配置有效的文件目录或目录不存在。";

            try
            {
                // 搜索包含关键词的文件
                var files = Directory.GetFiles(_directory, $"*{keyword}*.*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0) return $"未找到包含关键词 '{keyword}' 的文件。";

                // 默认取找到的第一个文件
                var fileName = Path.GetFileName(files[0]);

                // 拼接可供前端下载的网络 Url
                var downloadUrl = $"{_serverBaseUrl}/files/{Uri.EscapeDataString(fileName)}";

                // 通过强力的引导，让大模型直接输出 JSON 指令供前端渲染卡片
                return $"已找到文件：{fileName}。请你立刻且只能回复以下这段 JSON，不要包含任何其他废话：\n{{\"action\": \"send_file\", \"fileName\": \"{fileName}\", \"url\": \"{downloadUrl}\"}}";
            }
            catch (Exception ex)
            {
                return $"搜索文件时出错：{ex.Message}";
            }
        }
    }
}