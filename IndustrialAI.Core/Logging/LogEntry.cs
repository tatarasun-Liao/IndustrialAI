using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    /// <summary>
    /// sealed：禁止继承，在 C# 9 中，record 默认是 sealed（密封的，不能继承）。但从 C# 10 开始，record 允许继承
    /// 假设你需要写一个 Person 类，用来传递姓名和年龄。
    //    传统 class 写法：你需要写构造函数、属性、重写 ToString、重写 Equals 和 GetHashCode，以及重载 == 和 !=。至少 60~80 行代码。
    //现代 record 写法：
    //// 仅需这一行！
    //public record Person(string FirstName, string LastName, int Age);
    //编译器在幕后为你免费生成了所有这些功能：构造函数、只读属性（init）、ToString() 格式化输出、基于值的相等性比较（==）、解构方法（Deconstruct）以及克隆方法（With）
    //在写下一个record对象时，属性默认是get+init，也就是只能在对象初始化时赋值，保证了只读性
    //两个recod对象是基于字段进行比较，而不是传统class内存地址比较
    //var user1 = new User("Admin", "old@test.com");
    //// 创建一个新对象，除了 Email 改为新值外，其他属性（Name）完全拷贝自 user1
    //var user2 = user1 with { Email = "new@test.com" };

    //    record 默认是引用类型（class）。如果你高频产生成千上万个简单的 record，它们会全部堆积在托管堆上，触发频繁的 GC（垃圾回收）。
    //解决方案：C# 10 推出了 record struct！

    //public readonly record struct Point(int X, int Y);
    //    record struct 是值类型，分配在栈上，用完即焚，零 GC 压力，适合高频计算场景（如游戏开发、坐标计算）。
    //规则：数据少且创建频繁，用 record struct；数据多且作为复杂业务实体，用 record class。
    /// </summary>
    public sealed record LogEntry
    {
        public DateTime Timestamp { get; init; }
        public string Level { get; init; }           // Info, Warning, Error
        public string Message { get; init; }
        public string? Source { get; init; }         // 可选，例如 "PLC-01"

        /// <summary>
        /// 工厂方法，简化创建（避免每次都 new 并设置属性）
        /// </summary>
        public static LogEntry CreateInfo(string message, string? source = null) =>
       new()
       {
           Timestamp = DateTime.Now,
           Level = "Info",
           Message = message,
           Source = source
       };

        public static LogEntry CreateError(string message, string? source = null) =>
        new()
        {
            Timestamp = DateTime.Now,
            Level = "Error",
            Message = message,
            Source = source
        };
    }
}
