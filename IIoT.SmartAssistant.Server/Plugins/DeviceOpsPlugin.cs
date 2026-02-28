using System.ComponentModel;
using Microsoft.SemanticKernel;
using StackExchange.Redis;

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class DeviceOpsPlugin
    {
        private readonly IConnectionMultiplexer _redis;

        public DeviceOpsPlugin(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        [KernelFunction, Description("获取指定工业设备的当前实时状态、温度和振动频率。")]
        public async Task<string> GetDeviceStatusAsync([Description("设备的编号，如 Motor-01, Pump-01")] string deviceId)
        {
            var db = _redis.GetDatabase();

            // 直接从 Redis 内存中极速读取
            var temp = await db.StringGetAsync($"Device:{deviceId}:Temp");
            var status = await db.StringGetAsync($"Device:{deviceId}:Status");
            var vibration = await db.StringGetAsync($"Device:{deviceId}:Vibration");

            if (!temp.HasValue || !status.HasValue)
            {
                return $"未能在 Redis 缓存中找到设备 {deviceId} 的实时数据，请确认设备是否在线或编号是否正确。";
            }

            return $"设备 {deviceId} 的实时数据如下：状态 [{status}]，温度 [{temp} °C]，振动频率 [{vibration} mm/s]。";
        }

        [KernelFunction, Description("向指定设备下发控制指令，如重启、停机等。")]
        public async Task<string> ControlDeviceAsync(
            [Description("设备的编号，如 Motor-01")] string deviceId,
            [Description("控制指令，如 Restart, Stop, Start")] string command)
        {
            var db = _redis.GetDatabase();
            // 模拟下发指令存入 Redis（真实的网关可以监听这个 Key 执行硬体操作）
            await db.StringSetAsync($"Device:{deviceId}:Command", command);

            return $"已成功向设备 {deviceId} 队列下发 {command} 控制指令。";
        }
    }
}