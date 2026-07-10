using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Driver
{
    /// <summary>
    /// 定义接口 IDeviceDriver，用于表示设备驱动程序的基本功能。
    /// </summary>
    public interface IDeviceDriver
    {
        string DriverName { get; }
        void Connect();
        string ReadData();

    }

    /// <summary>
    /// 继承自IDeviceDriver接口，使用协变类型参数T，T只能作为返回值不可作为输入参数
    /// 实现了调用时，左边是父类，右边是子类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IDriverProvider<out T> where T : IDeviceDriver
    {
        T CreateDriver();
    }
}
