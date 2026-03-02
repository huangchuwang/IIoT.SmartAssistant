using IIoT.SmartAssistant.Server.Hubs;
using IIoT.SmartAssistant.Server.Models;
using IIoT.SmartAssistant.Server.Services;
using StackExchange.Redis;

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

            builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AIConfig"));

            // 注册 Redis 和 后台服务
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("RedisConnection") ?? "localhost:6379"));
            builder.Services.AddHostedService<DeviceDataSimulatorService>();

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<AIChatService>();

            var app = builder.Build();

            var aiConfig = app.Configuration.GetSection("AIConfig").Get<IIoT.SmartAssistant.Server.Models.AppConfig>();
            if (aiConfig != null && !string.IsNullOrWhiteSpace(aiConfig.FilePath) && Directory.Exists(aiConfig.FilePath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(aiConfig.FilePath),
                    RequestPath = "/files" // 映射的虚拟访问路径
                });
            }

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