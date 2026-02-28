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

            //使用 Options 模式绑定配置 ▼
            builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AIConfig"));

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<AIChatService>();

            builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AIConfig"));

            // 注册 Redis 连接复用器 (单例)
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("RedisConnection") ?? "localhost:6379"));

            // 注册 IoT 数据采集后台服务
            builder.Services.AddHostedService<DeviceDataSimulatorService>();

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