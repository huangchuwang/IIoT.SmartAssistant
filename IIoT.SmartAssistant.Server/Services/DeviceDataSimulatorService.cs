using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IIoT.SmartAssistant.Server.Services
{
    public class DeviceDataSimulatorService : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<DeviceDataSimulatorService> _logger;

        private const int SimulationIntervalMilliseconds = 1000;

        private static readonly string[] Devices = { "Motor-01", "Motor-02", "Pump-01" };
        private const string DeviceKeyPrefix = "Device:";
        private const string TempKeySuffix = ":Temp";
        private const string StatusKeySuffix = ":Status";
        private const string VibrationKeySuffix = ":Vibration";

        private const double BaseTemperature = 40.0;
        private const double TempFluctuation = 20.0;
        private const int WarningProbability = 95;
        private const double BaseVibration = 0.5;
        private const double VibrationFluctuation = 2.5;

        private readonly Random _rand = new();

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
                foreach (var device in Devices)
                {
                    // 模拟温度在 40.0 到 60.0 之间波动
                    double temp = BaseTemperature + _rand.NextDouble() * TempFluctuation;
                    // 模拟极小概率出现 Warning 报警状态
                    string status = _rand.Next(100) > WarningProbability ? "Warning" : "Running";
                    double vibration = BaseVibration + _rand.NextDouble() * VibrationFluctuation;

                    // 写入 Redis 缓存 (Key 的格式如: Device:Motor-01:Temp)
                    await db.StringSetAsync($"{DeviceKeyPrefix}{device}{TempKeySuffix}", temp.ToString("F1"));
                    await db.StringSetAsync($"{DeviceKeyPrefix}{device}{StatusKeySuffix}", status);
                    await db.StringSetAsync($"{DeviceKeyPrefix}{device}{VibrationKeySuffix}", vibration.ToString("F2"));
                }

                // 模拟每 1 秒钟采集一次底层设备的轮询周期
                await Task.Delay(SimulationIntervalMilliseconds, stoppingToken);
            }
        }
    }
}