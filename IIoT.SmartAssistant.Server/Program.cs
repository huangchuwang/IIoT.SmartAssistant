using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models; 
using IIoT.SmartAssistant.Server.Services;

namespace IIoT.SmartAssistant.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddAuthorization();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            //使用 Options 模式绑定配置 ▼
            builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AIConfig"));

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<AIChatService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();
            app.MapHub<ChatHub>("/chathub");

            app.Run();
        }
    }
}