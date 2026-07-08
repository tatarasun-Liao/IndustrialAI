using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core
{
    //根据设备ID读取电压值
    public class DeviceService
    {
        public Result<double> ReadVoltage(string deviceId)
        {

            if(string.IsNullOrEmpty(deviceId))

                    return Result<double>.Failure("Device ID cannot be null or empty.");

            if(!deviceId.Equals("PLC_V01"))

                    return Result<double>.Failure($"Device with ID '{deviceId}' not found.");

            // 模拟读取电压值
            double voltage = 220.0; // 假设电压值为220V

            return Result<double>.Success(voltage);
        }
    }
}
