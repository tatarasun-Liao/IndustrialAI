using IndustrialAI.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Driver
{
    /// <summary>
    /// 设置标签调用时构造函数参数与属性参数
    /// </summary>
    [Driver("CAN",Station = "EOL-01")]
    public class CanDriver : IDeviceDriver
    {
        //表示只读属性，编译后生成get方法
        public string DriverName => "CAN Bus Driver v2.0";
        public void Connect() => Console.WriteLine($"[{DriverName}] CAN 总线连接成功。");
        public string ReadData() => "CAN 数据帧: 0x123, 8 bytes";
    }
}
