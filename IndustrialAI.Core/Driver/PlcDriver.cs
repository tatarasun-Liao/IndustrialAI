using IndustrialAI.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Driver
{
    [Driver("PLC", Station = "Main-Line-02")]
    public class PlcDriver : IDeviceDriver
    {
        public string DriverName => "Siemens S7 Driver";
        public void Connect() => Console.WriteLine($"[{DriverName}] PLC 连接成功。");
        public string ReadData() => "PLC 寄存器值: 100.5 Hz";
    }
}
