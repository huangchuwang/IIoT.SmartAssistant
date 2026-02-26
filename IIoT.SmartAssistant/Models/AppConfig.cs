using System.IO;
using System.Text.Json;

namespace IIoT.SmartAssistant.Models
{
    public class AppConfig
    {
        public string ApiKey { get; set; }
        public string ApiUrl { get; set; }


        public static AppConfig Load()
        {
            string configPath = "application.json";

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("配置文件未找到application");
            }
            string jsonString = File.ReadAllText(configPath);

            return JsonSerializer.Deserialize<AppConfig>(jsonString);
        }
    }
}
