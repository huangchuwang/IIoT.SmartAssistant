using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IIoT.SmartAssistant.Server.Services
{
    public class DeviceDataSimulatorService : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<DeviceDataSimulatorService> _logger;
        // 模拟车间里的三台设备
        private readonly string[] _devices = { "Motor-01", "Motor-02", "Pump-01" };
        private readonly Random _rand = new Random();

        public DeviceDataSimulatorService(IConnectionMultiplexer redis, ILogger<DeviceDataSimulatorService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var db = _redis.GetDatabase();
            _logger.LogInformation("IoT 虚拟网关数据采集服务已启动...");

            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var device in _devices)
                {
                    // 模拟温度在 40.0 到 60.0 之间波动
                    double temp = 40.0 + _rand.NextDouble() * 20.0;
                    // 模拟极小概率出现 Warning 报警状态
                    string status = _rand.Next(100) > 95 ? "Warning" : "Running";
                    double vibration = 0.5 + _rand.NextDouble() * 2.5;

                    // 写入 Redis 缓存 (Key 的格式如: Device:Motor-01:Temp)
                    await db.StringSetAsync($"Device:{device}:Temp", temp.ToString("F1"));
                    await db.StringSetAsync($"Device:{device}:Status", status);
                    await db.StringSetAsync($"Device:{device}:Vibration", vibration.ToString("F2"));
                }

                // 模拟每 1 秒钟采集一次底层设备的轮询周期
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}