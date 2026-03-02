namespace IIoT.SmartAssistant.Server.Models
{
    public class AppConfig
    {
        public string ApiKey { get; set; }
        public string ApiUrl { get; set; }

        /// <summary>
        /// 统一使用此目录作为知识库文档和供前端下载的文件库目录
        /// </summary>
        public string FilePath { get; set; }

        public string PromptFilePath { get; set; }
    }
}