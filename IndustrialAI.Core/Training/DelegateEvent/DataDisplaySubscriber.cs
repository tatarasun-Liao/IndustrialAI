using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Training.DelegateEvent
{
    /// <summary>
    /// 事件订阅者类
    /// 显式订阅事件的时候需要继承idispose，并在dispose中取消事件订阅，防止内存溢出
    /// </summary>
    public class DataDisplaySubscriber : IDisposable
    {
        private readonly PlcDataPublisher _publisher;

        private bool _disposed = false;

        public DataDisplaySubscriber(PlcDataPublisher publisher)
        {
            _publisher = publisher;

            // 这里传入的是“具名方法” OnDataReceived，而不是 Lambda。

            // 因为取消订阅（-=）时，必须使用同一个委托实例。
            _publisher.DataChanged += OnDataReceived;

            Console.WriteLine("[显示订阅者] 已订阅 PLC 数据变化。");
        }

        private void OnDataReceived(object sender, float newValue)
        {
            // 模拟 UI 显示更新
            Console.WriteLine($"[显示] 当前电压: {newValue:F2} V");
        }

        // ----- IDisposable 实现：用于显式取消订阅 -----
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 【核心】通过 -= 取消订阅，防止发布者持有该对象的引用。
                    // 如果不取消订阅，该订阅者对象将永远无法被 GC 回收（内存泄漏！）。
                    if (_publisher != null)
                    {
                        _publisher.DataChanged -= OnDataReceived;
                        Console.WriteLine("[显示订阅者] 已取消订阅（Dispose）。");
                    }
                }
                _disposed = true;
            }
        }
    }
}
