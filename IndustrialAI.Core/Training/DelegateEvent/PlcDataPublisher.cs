using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Training.DelegateEvent
{
    /// <summary>
    /// 【核心训练 1】标准事件发布者。
    /// 使用自定义委托类型，模拟工业 PLC 数据变化通知。
    /// </summary>
    public class PlcDataPublisher
    {
        //委托本质上是个类，该委托定义了发送者和发送数据
        public delegate void DataChangeEvendHandler(object sender, float newValue);

        //event关键字用于修饰委托类型变量，封装为事件，防止外部随意修改调用委托列表
        //event限制了外部只能通过+=  -=操作，而不能直接赋值或者调用,只有事件内部才能调用、赋值委托变量
        //event关键字把委托进行了封装，封装成了私有字段+add+remove，使其在外部无法直接调用委托而只能订阅或者取消订阅事件的方法来访问委托
        public event DataChangeEvendHandler? DataChanged;

        /// <summary>
        /// 事件触发方法
        /// ?.Invoke(...) 是 C# 6.0 引入的空条件操作符，等效于：
        // if (DataChanged != null) DataChanged(this, value);
        // 它保证了在无订阅者时不会抛出 NullReferenceException。
        /// </summary>
        /// <param name="newValue"></param>
        public void OnSimulateValueUpdate(float newValue)
        {
            Console.WriteLine($"[发布者] 模拟 PLC 数值变化: {newValue} V");

            //当调用该方法的时候，所有订阅该事件的方法都会按照订阅顺序执行
            //?.Invoke 会检查调用列表是否为空。内部遍历委托链，按顺序同步执行所有订阅者方法
            DataChanged?.Invoke(this, newValue);
        }

        public void ClearAllSubscribers()
        {
            // 注意：只有定义事件的类内部才能直接操作委托变量（将 DataChanged 置 null）。
            // 外部只能用 -=，不能置 null。
            DataChanged = null;

            Console.WriteLine("[发布者] 已清空所有订阅者。");
        }
    }
}
