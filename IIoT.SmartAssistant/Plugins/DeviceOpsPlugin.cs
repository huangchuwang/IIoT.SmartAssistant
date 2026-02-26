using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace IIoT.SmartAssistant.Plugins
{
    public class DeviceOpsPlugin
    {
        [KernelFunction, Description("读取指定工业设备（如电机、PLC）的当前运行状态和温度")]
        public async Task<string> GetDeviceStatusAsync(
            [Description("设备编号，例如：Motor-01, PLC-A")] string deviceId)
        {
            // 这里未来可以替换为真实的 SerialPort 或 NModbus 读取逻辑
            await Task.Delay(500); // 模拟串口通信或 I/O 延迟

            if (deviceId.ToUpper() == "MOTOR-01")
            {
                return $"设备 {deviceId} 通信正常，当前线圈温度 45.5°C，无报警代码。";
            }
            return $"设备 {deviceId} 串口请求超时，未获取到数据。";
        }
    }
}