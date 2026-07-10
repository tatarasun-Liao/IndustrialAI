using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Events
{
    /// <summary>
    /// 定义逆变接口，TEvent类型只能作为输入参数，不能作为返回值
    /// 实现调用时左侧为子类，右边为父类或子类
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    public interface IEventHandler<in TEvent>
    {
        void Handle(TEvent @event);
    }

    public class DeviceOnlineEvent : IEvent
    {
        public string DeviceId { get; set; } = string.Empty;

        public DateTime OnlineTime { get; set; } = DateTime.Now;
    }

    public class ConsoleLoggerHandler : IEventHandler<DeviceOnlineEvent>
    {
        public void Handle(DeviceOnlineEvent @event)
        {
            Console.WriteLine($"[事件] 设备 {@event.DeviceId} 已上线。");
        }
    }

    public sealed class UniversalLoggerHandler : IEventHandler<object>
    {
        public void Handle(object @event)
        {
            // 【底层原理】is 模式匹配（C# 7.0+）：如果 @event 是 DeviceOnlineEvent 类型，则解构赋值给 deviceEvent。
            if (@event is DeviceOnlineEvent deviceEvent)
            {
                Console.WriteLine($"[万能日志] 设备上线: {deviceEvent.DeviceId}，时间: {deviceEvent.OnlineTime:HH:mm:ss}");
            }
            else
            {
                Console.WriteLine($"[万能日志] 收到未知事件: {@event.GetType().Name}");
            }
        }
    }
}
