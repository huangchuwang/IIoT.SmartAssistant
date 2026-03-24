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

            // ע�� Redis �� ��̨����
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var redisConnection = builder.Configuration.GetConnectionString("RedisConnection");
                if (string.IsNullOrEmpty(redisConnection))
                {
                    throw new InvalidOperationException("Redis connection string 'RedisConnection' not found.");
                }
                return ConnectionMultiplexer.Connect(redisConnection);
            });
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
                    RequestPath = "/files" // ӳ����������·��
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