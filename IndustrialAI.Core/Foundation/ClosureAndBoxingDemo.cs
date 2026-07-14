using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Foundation
{
    public static class ClosureAndBoxingDemo
    {
        public static void Run()
        {
            Console.WriteLine("========== 内功早课：闭包陷阱与装箱 ==========\n");

            // ----- 1. 经典闭包陷阱（for 循环捕获变量） -----
            Console.WriteLine("--- 场景 1：错误用法（捕获同一个变量 i） ---");

            var tasks = new List<Task>();

            for (int i = 0; i < 5; i++)
            {
                // i 在循环中只有一个内存地址。当任务真正执行时，i 已经变为最终值 5。
                tasks.Add(Task.Run(() => Console.WriteLine($"错误打印: {i}")));
            }

            //在执行这一步时，由于Task延迟执行，i已经循环完毕到了5，且值类型自我覆盖，所以在waitall时只能打印出5
            Task.WaitAll(tasks.ToArray());

            Console.WriteLine("\n--- 场景 2：正确用法（局部副本） ---");
            tasks.Clear();

            for (int i = 0; i < 5; i++)
            {
                int copy = i; // 每次循环都创建新的局部变量，闭包捕获的是这个“副本”的内存地址。
                tasks.Add(Task.Run(() => Console.WriteLine($"正确打印: {copy}")));
            }
            Task.WaitAll(tasks.ToArray());

            // ----- 2. 值类型（struct）与引用类型（class）在委托中的装箱表现 -----
            Console.WriteLine("\n--- 场景 3：值类型作为委托参数时会装箱（分配堆内存） ---");

            int intValue = 42;          // 值类型，分配在栈上
            object boxed = intValue;    // 装箱：将 int 复制到堆中

            Action<object> printAction = (obj) => Console.WriteLine($"装箱后的值: {obj}");
            printAction(boxed); // 正常

            // 【底层原理】当 struct 实现接口并作为接口传递时，必然装箱。
            // 这对高频日志（每秒上万条）是灾难，因此我们的 LogEntry 使用了 record class（引用类型），而非 struct。
            Console.WriteLine("结论：高频数据传输场景，应优先使用 class（引用类型）避免装箱开销。");
            Console.WriteLine();
        }
    }
}
