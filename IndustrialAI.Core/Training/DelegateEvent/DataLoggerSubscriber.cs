using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Training.DelegateEvent
{
    /// <summary>
    /// 为什么订阅者必须实现 IDisposable 并在其中取消订阅？**************************
    /// A：发布者作为类似全局的变量，生命周期很长，但是订阅者很可能只有在调用的时候才会声明，生命周期可能非常短，如果订阅者销毁的时候没有从发布者中移除订阅的事件，那么发布者的委托链中仍然会一直保存订阅者的强引用，导致GC无法回收而内存泄漏
    /// </summary>
    public class DataLoggerSubscriber:IDisposable
    {
        private readonly PlcDataPublisher _publisher;

        private int _logCount = 0;

        public DataLoggerSubscriber(PlcDataPublisher publisher)
        {
            _publisher = publisher;
            // 同样使用具名方法订阅
            _publisher.DataChanged += OnDataLogged;
            Console.WriteLine("[日志订阅者] 已订阅 PLC 数据变化。");
        }

        private void OnDataLogged(object sender, float newValue)
        {
            _logCount++;
            Console.WriteLine($"[日志] 第 {_logCount} 条记录: 数值 {newValue:F2} V (时间: {DateTime.Now:HH:mm:ss})");
        }

        public void Unsubscribe()
        {
            if (_publisher != null)
            {
                _publisher.DataChanged -= OnDataLogged;
                Console.WriteLine("[日志订阅者] 已手动取消订阅。");
            }
        }

        public void Dispose()
        {
            Unsubscribe();

            GC.SuppressFinalize(this);
        }
    }
}
