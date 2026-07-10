using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Events
{
    /// <summary>
    /// 
    /// </summary>
    public class EventBus
    {
        //强引用和弱引用
        //强引用：如果你有一个变量 var obj = new MyClass();，只要这个变量还在作用域内活着，GC 就坚决不能回收这个 MyClass 对象。
        //弱引用：如果你用 new WeakReference(obj) 包装了它。GC 回收垃圾时，完全无视弱引用。如果除了弱引用之外，没有其他强引用指向这个对象，GC 就会把它立刻回收掉。

        //可以认为当强引用资源被释放时，如果事件总线中之前有注册过强引用类型的实例，则会导致GC判定资源仍然在占用，无法杀死，这会导致内存占用
        //而使用弱引用类型包裹后，当释放资源的时候，GC会释放资源，而且没有任何强引用资源指向该资源，所以GC直接回收内存，当下次总线触发了事件的时候会遍历事件列表发现弱引用资源指向的引用为null，此时直接将记录从字典内删除，使用弱引用，总线可以“认识”所有订阅者，但不“绑架”它们。订阅者随时可以安息（被回收），而不会造成内存泄漏。

        //所以这个声明方法就是：声明了一个线程安全的可以存储事件类型和防止内存溢出而创造的弱引用类型对象字典缓存，用于存储事件与对应的订阅
        private readonly ConcurrentDictionary<Type, List<WeakReference>> _handlers = new();

        //订阅事件（同步委托）
        public void Subscribe<T>(Action<T> handler) where T : class, IEvent//约束 T 必须是引用类型且实现 IEvent 接口。
        {
            if (handler == null)

                throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(T);

            //*************************************8
            //将 Action<T> 包装为弱引用存储。Action<T> 是委托，委托内部持有目标对象（Target）。
            // 若直接存储委托，会强引用订阅者，导致内存泄漏。使用 WeakReference 允许 GC 回收订阅者。
            var weakRef = new WeakReference(handler);

            //如果 key 存在，则执行 update 委托；否则执行 add 委托
            _handlers.AddOrUpdate(eventType, //添加Type
                new List<WeakReference> { weakRef },//添加的包装Action弱引用
                (key, list) =>
                {
                    list.Add(weakRef);

                    return list;
                });
        }

        public void Subscribe<T>(IEventHandler<T> handler) where T : class, IEvent
        {
            if (handler == null)

                throw new ArgumentNullException(nameof(handler));

            Subscribe<T>(handler.Handle);//将 IEventHandler<T> 的 Handle 方法转换为 Action<T> 委托存储
        }

        /// <summary>
        /// 异步发布事件，并发执行所有订阅者
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="event"></param>
        /// <returns></returns>
        public async Task PublishAsync<T>(T @event) where T : class, IEvent
        {
            if (@event == null)

                throw new ArgumentNullException(nameof(@event));

            var eventType = typeof(T);

            if (!_handlers.TryGetValue(eventType, out var weakReferences))

                return; // 无订阅者，静默返回

            //创建快照，虽然使用了线程安全类型字典，但是字典内value类型仍然是List，而list是非线程安全的
            var handlersToInvoke = new List<Action<T>>();

            //用于收集已经GC的弱引用对象，清理内存
            var deadRefs = new List<WeakReference>();

            lock (weakReferences)//通过锁住当前列表资源保证遍历时安全性，防止篡改
            {
                foreach (var wr in weakReferences)
                {
                    if (wr.Target is Action<T> action)

                        handlersToInvoke.Add(action);

                    else if (!wr.IsAlive)

                        deadRefs.Add(wr);//收集死亡的弱引用
                }

                //清理死亡的弱引用，防止列表无限膨胀
                if (deadRefs.Count > 0)
                {
                    foreach (var dead in deadRefs)
                    {
                        weakReferences.Remove(dead);
                    }
                }
            }

            //遍历执行所有action，并且异步处理异常
            var tasks = handlersToInvoke.Select(action => Task.Run(() =>
            {
                try
                {
                    action(@event);
                }
                catch (Exception ex)
                {
                    // 【重要】生产环境中，应将异常记录到 ILogger，而不是直接 Console。
                    // 此处为了演示，暂时输出到控制台。
                    Console.WriteLine($"[EventBus] 事件处理器执行异常: {ex.Message}");
                    // 不重新抛出，保证其他处理器继续执行。
                }
            }));

            //whenall是异步等待所有任务结束，不会阻塞主线程
            //waitall是同步等待所有任务结束，阻塞主线程
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 同步发布事件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="event"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Publish<T>(T @event) where T : class, IEvent
        {
            if (@event == null)

                throw new ArgumentNullException(nameof(@event));

            var eventType = typeof(T);

            if (!_handlers.TryGetValue(eventType, out var weakReferences))

                return;

            var handlersToInvoke = new List<Action<T>>();

            var deadRefs = new List<WeakReference>();

            lock (weakReferences)
            {
                foreach (var wr in weakReferences)
                {
                    if (wr.Target is Action<T> action)
                    {
                        handlersToInvoke.Add(action);
                    }
                    else if (!wr.IsAlive)
                    {
                        deadRefs.Add(wr);
                    }
                }

                if (deadRefs.Count > 0)
                {
                    foreach (var dead in deadRefs)

                        weakReferences.Remove(dead);
                }
            }

            //按照顺序依次执行，依次执行并处理异常
            foreach (var action in handlersToInvoke)
            {
                try
                {
                    action(@event);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EventBus] 同步处理器异常: {ex.Message}");
                }
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : class, IEvent
        {
            if (handler == null)

                return;

            var eventType = typeof(T);

            //没有找到对应事件类型的委托资源，不需要释放
            if (!_handlers.TryGetValue(eventType, out var weakReferences))

                return;

            lock (weakReferences)
            {
                var toMove = new List<WeakReference>();

                foreach (var wr in weakReferences)
                {
                    //当遍历到和参数相同需要移除的资源时
                    if(wr.Target is Action<T> exsiting && exsiting == handler)

                            toMove.Add(wr);
                }

                foreach(var dead in toMove)

                    weakReferences.Remove(dead);
            }
        }

        public void Clear<T>() where T : class, IEvent
        {
            var eventType = typeof(T);

            _handlers.TryRemove(eventType, out _);//_表示放弃输出参数
        }
    }
}
