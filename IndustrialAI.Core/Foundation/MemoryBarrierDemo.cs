using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Foundation
{
    public static class MemoryBarrierDemo
    {
        private static volatile bool _StopFlag = false;//volatile保证每次都是从主内存中拿到的最新值

        private static int _counter = 0;

        public static void Run()
        {
            Console.WriteLine("========== 内功早课：volatile 与 原子操作 ==========\n");

            // ----- 场景 1：volatile 保证可见性 -----
            Console.WriteLine("--- 场景 1：使用 volatile 保证停止标志可见 ---");
            _StopFlag = false;

            var t1 = Task.Run(() =>
            {
                Console.WriteLine("工作线程开始循环...");

                int iterations = 0;

                //如果不加volatile，release模式下JIT可能会优化为
                // if (!_stopFlag) { while(true) ... } 导致死循环。
                while (!_StopFlag)
                {
                    iterations++;

                    if (iterations % 1_000_000 == 0)
                        Thread.Sleep(1);
                }
                Console.WriteLine($"工作线程退出，共迭代 {iterations} 次。");
            });

            Thread.Sleep(500); // 让工作线程跑一会儿
            Console.WriteLine("主线程设置停止标志...");
            _StopFlag = true; // volatile 写入，立即刷新到主内存。

            t1.Wait();
            Console.WriteLine("场景 1 完成，线程正常退出。\n");//volatile一般用于并不频繁变化的bool值，开销极低


            // ----- 场景 2：Interlocked 原子操作（无锁线程安全）-----

            //运行结果:
            //线程 A 成功获取锁 (state=1)
            //线程 B 获取锁失败，当前 state = 1
            //线程 A 释放锁。
            //最终 state = 0
            //此处模拟的就是A和B多线程同时修改State状态，A获取到了State原始状态0并进行了修改，此时B再进行修改时发现State与原始值0不匹配，此时可知道State被修改过了
            Console.WriteLine("--- 场景 2：Interlocked.CompareExchange 实现原子状态机 ---");
            int state = 0; // 0=空闲, 1=运行中, 2=完成

            var t2 = Task.Run(() =>
            {

                // 尝试将 0 改为 1，如果当前值是 0，则修改成功返回旧值 0；否则返回当前值
                //Interlocked.CompareExchange = “比较后交换”。
                //它的逻辑是：“只有当目标变量还等于预期的旧值时，我才把它更新成新值；无论更新成不成功，我都把目标变量这一刻的原始值告诉你。”
                //public static T CompareExchange<T>(ref T location1, T value, T comparand)
                //它的执行流程翻译成中文就是：
                //看看 location1 现在的值是多少（记作 A）。
                //把 A 和参数里的 comparand（预期旧值）进行比对。
                //如果 A == comparand 相等，说明没别人改过它，立刻把 location1 更新为 value（新值）。
                //如果 A != comparand 不相等，说明这期间“被别人抢先改过了”，什么都不做（不写入新值）。
                //把一开始读取到的原始值 A，作为整个方法的返回值返回给你！

                //其实就是做了一个判断赋值：判断state 与预期值0是否一致？如果一致的话表示state还没有被其他线程修改过，代表这个变量此时可以修改，那么此时state被修改为1，方法返回state原始值0；而如果state被外部别的线程修改了变为1或者2，此时比较state与0不一致，则放弃写入1，而是直接返回state的原始值1或者2
                //这个方法就相当于比较后赋值，并把最新的值返回
                // 尝试将 0 改为 1，如果当前值是 0，则修改成功返回旧值 0并将state变为1；否则返回当前值。
                int original = Interlocked.CompareExchange(ref state, 1, 0);

                if (original == 0)//判断state的原始值是否是0，若为0表示上一步修改成功，此时state=1
                {
                    Console.WriteLine($"线程 A 成功获取锁 (state={state})");
                    Thread.Sleep(200); // 模拟持有锁
                                       // 释放锁：将 1 改回 0
                    Interlocked.Exchange(ref state, 0);
                    Console.WriteLine("线程 A 释放锁。");
                }
                else
                {
                    Console.WriteLine($"线程 A 获取锁失败，当前 state={original}");
                }
            });

            var t3 = Task.Run(() =>
            {
                int original = Interlocked.CompareExchange(ref state, 1, 0);
                if (original == 0)
                {
                    Console.WriteLine($"线程 B 成功获取锁 (state={state})");
                    Interlocked.Exchange(ref state, 0);
                    Console.WriteLine("线程 B 释放锁。");
                }
                else
                {
                    Console.WriteLine($"线程 B 获取锁失败，当前 state={original}");
                }
            });

            Task.WaitAll(t2, t3);
            Console.WriteLine($"最终 state = {state}\n");

            // ----- 场景 3：CPU 缓存伪共享（False Sharing）概念演示 -----
            // 【底层原理】当两个线程频繁修改相邻内存地址（如数组相邻元素），
            // 它们会不断使对方缓存行失效，导致性能雪崩。
            // 解决方案：Padding（填充）使变量独占一个缓存行（64字节）。
            Console.WriteLine("--- 场景 3（概念）：伪共享（False Sharing）---");
            Console.WriteLine("当两个高频变量放在同一缓存行（64字节）内，多核性能会暴跌。");
            Console.WriteLine("工业级高性能组件（如 Disruptor）会使用 Padding 隔离。");
            Console.WriteLine();
        }
    }

    public class LockFreeQueue<T>
    {
        private T[] _items;

        private volatile int _head = 0; // volatile 保证头部索引永远可见

        private int _tail = 0;

        public LockFreeQueue(int capacity) => _items = new T[capacity];

        public bool TryEnqueue(T item)
        {
            int currentTail;
            int nextTail;

            do
            {
                currentTail = _tail;

                nextTail = (currentTail + 1) % _items.Length;

                if (nextTail == _head) return false;
            }
            //使用 Interlocked.CompareExchange 实现原子更新 tail 指针
            // 如果当前 _tail 还是 currentTail（没被其他线程抢先动过），就把它更新为 nextTail
            // 如果被人抢先动了（CurrentTail 过期了），CAS 返回 false，重新循环再来一次！
            while (Interlocked.CompareExchange(ref _tail, nextTail, currentTail) != currentTail);

            _items[currentTail] = item; // 因为 tail 已被原子锁定，这里写入是安全的
            return true;
        }
    }
}
