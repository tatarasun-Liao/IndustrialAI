// See https://aka.ms/new-console-template for more information
using IndustrialAI.Core;
using IndustrialAI.Core.Attributes;
using IndustrialAI.Core.Cache;
using IndustrialAI.Core.Driver;
using IndustrialAI.Core.Events;
using IndustrialAI.Core.Factory;
using IndustrialAI.Core.Logging;
using IndustrialAI.Core.Model;
using IndustrialAI.Core.Training.DelegateEvent;
using IndustrialAI.Core.Training.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;

#if false
#region Test Generic
{
    var Service = new DeviceService();

    //1.查询存在的设备的电压值
    Result<double> result1 = Service.ReadVoltage("PLC_V01");


    //函数模式匹配写法，可以在调用方法时明确指定参数名称，而不强制需要按照函数的参数顺序进行传参，通过强制写下参数名称可以提高代码的解释性
    string message1 = result1.Match<string>(
        onSuccess: voltage => $"电压读取成功：{voltage} V",//传入参数名称：onSuccess，参数值 voltage,返回string：$"电压读取成功：{voltage} V"
        onFailure: error => $"读取失败：{error}"
    );

    Console.WriteLine(message1);

    // 场景2：查询不存在的设备
    Result<double> result2 = Service.ReadVoltage("PLC-99");
    string message2 = result2.Match<string>(
        onSuccess: voltage => $"电压读取成功：{voltage} V",
        onFailure: error => $"读取失败：{error}"
    );
    Console.WriteLine(message2);

    Console.ReadKey();
}
#endregion

#region Test Attribute
{
    Console.WriteLine("=== 测试泛型缓存 ===");

    //泛型缓存，得到CanDriver的DriverAttribute特性，并执行特性构造函数
    var cacheAttr = GenericCache<CanDriver>.GetAttribute<DriverAttribute>();

    Console.WriteLine($"CanDriver 缓存的协议: {cacheAttr?.Protocol ?? "Null"}");

    Console.WriteLine("\n=== 测试协变 (out) ===");

    List<CanDriver> canlist = new();

    //IEnumerable底层实现了out协变，实现了左侧是IDeviceDriver参数类型，右侧是实现了IDeviceDriver的CanDriver参数类型的赋值过程，协变生效
    IEnumerable<IDeviceDriver> driverEnum = canlist;

    Console.WriteLine($"List<CanDriver> 成功赋值给 IEnumerable<IDeviceDriver>，协变生效。");

    Console.WriteLine("\n=== 测试逆变 (in) ===");

    IEventHandler<object> baseHandler = new UniversalLoggerHandler();

    //左侧是子类，右侧是父类，实现了类型作为输入参数且从父类隐式转换为子类的过程
    IEventHandler<DeviceOnlineEvent> derivedHandler = baseHandler;

    //调用的是右侧父类中的方法
    derivedHandler.Handle(new DeviceOnlineEvent { DeviceId = "PLC-01" });

    Console.WriteLine($"IEventHandler<object> 成功赋值给 IEventHandler<DeviceOnlineEvent>，逆变生效。");

    Console.WriteLine("\n=== 测试插件扫描 ===");

    var drivers = new PluginScanner().ScanTypes();

    Console.WriteLine($"发现 {drivers.Count} 个驱动:");

    foreach (var type in drivers)
    {
        var attr = type.GetCustomAttribute<DriverAttribute>();

        //只有实际开始调用的时候才会实际执行DriverAttribute内部逻辑
        Console.WriteLine($"- {type.Name} [协议: {attr?.Protocol}, 工站: {attr?.Station}]");

        //通过声明模式，将类型判断和变量赋值融合在一起
        //1、通过反射创建object
        //2.类型判断是否实现接口或者继承类
        //3、模式匹配赋值，若判断为true则执行隐式转换并赋值给driver，若为false则不会赋值并跳过整个if
        //语法糖
        if (Activator.CreateInstance(type) is IDeviceDriver driver)
        {
            driver.Connect();
            Console.WriteLine($"  读取: {driver.ReadData()}");
        }
    }
}
#endregion

#region Test Factory
{
    Console.WriteLine("\n=== 4. 工厂模式测试 ===");

    //读取之前创建过的CANDriver类型缓存，并创建对象
    var canDriver = DriverFactory.Create("CAN");

    if (canDriver != null)
    {
        canDriver.Connect();
        Console.WriteLine($"  读取数据: {canDriver.ReadData()}");
    }
    else
    {
        Console.WriteLine("创建 CAN 驱动失败。");
    }

    var unknown = DriverFactory.Create("PROFINET");
    Console.WriteLine($"未知协议 (PROFINET) 返回: {(unknown == null ? "null" : "实例")}");
}
#endregion

#region Test Event Bus
{
    Console.WriteLine("\n=== 6. 事件总线测试 ===");

    var eventbus = new EventBus();

    //1.同步委托订阅
    //创建一个实现IEvent的引用类型DeviceOnlineEvent作为类型参数的委托并缓存DeviceOnlineEvent与委托到_handlers
    eventbus.Subscribe<DeviceOnlineEvent>(evt =>
    {
        Console.WriteLine($"[订阅者-Lambda] 设备 {evt.DeviceId} 在 {evt.OnlineTime:HH:mm:ss} 上线。");
    });


    //2.使用Subscribe的重载，将实现IEventHandler<object>的UniversalLoggerHandler实例作为参数传入，并将Handle方法缓存到_handlers
    var loggerHandler = new UniversalLoggerHandler();

    eventbus.Subscribe<DeviceOnlineEvent>(loggerHandler);

    // 订阅者 3：模拟一个会抛出异常的处理器（测试异常隔离）
    eventbus.Subscribe<DeviceOnlineEvent>(evt =>
    {
        Console.WriteLine($"[订阅者-异常] 即将抛出异常...");
        throw new InvalidOperationException("模拟异常测试");
    });

    var testEvent = new DeviceOnlineEvent { DeviceId = "EOL-01" };

    await eventbus.PublishAsync(testEvent);

    Console.WriteLine("事件发布完成（异步）。");

    // 同步发布测试
    Console.WriteLine("\n同步发布测试：");

    eventbus.Publish(new DeviceOnlineEvent { DeviceId = "SYNC-TEST" });

    Console.WriteLine("事件总线测试完成。");
}
#endregion

#region Delegate Event
Console.WriteLine("========== 标准事件与多播委托专项训练 ==========\n");

PlcDataPublisher plcPublisher = new PlcDataPublisher();

// 2. 创建订阅者（使用 using 块确保训练结束后自动 Dispose 取消订阅）
//只有实现了IDispose的类才能使用该写法
using (var displaySub = new DataDisplaySubscriber(plcPublisher))
using (var loggerSub = new DataLoggerSubscriber(plcPublisher))
{

    Console.WriteLine("\n--- 场景 1: 正常发布，两个订阅者都会响应 ---");

    //发布者触发方法，内部事件被触发，按照订阅顺序执行
    plcPublisher.OnSimulateValueUpdate(24.5f);

    // 4. 手动取消日志订阅者的订阅（模拟业务逻辑主动取消）
    Console.WriteLine("\n--- 场景 2: 手动取消日志订阅者 ---");
    loggerSub.Unsubscribe(); // 此时日志订阅者不再响应

    // 再次发布数据，只有显示器订阅者响应
    Console.WriteLine("\n--- 场景 3: 再次发布数据 ---");
    plcPublisher.OnSimulateValueUpdate(12.8f);

    // 5. 显式展示多播委托特性：临时使用 Lambda 再附加一个订阅（展示动态添加）
    Console.WriteLine("\n--- 场景 4: 额外使用 Lambda 临时订阅（测试多播） ---");

    //重新定义一个相同类型的匿名委托，内部匿名方法
    PlcDataPublisher.DataChangeEvendHandler tempHandler = (sender, val) =>
    {
        Console.WriteLine($"[临时订阅者] 瞬时响应: {val} V");
    };

    plcPublisher.DataChanged += tempHandler;

    // 发布，此时应该有 2 个订阅者（显示器 + 临时）
    plcPublisher.OnSimulateValueUpdate(5.0f);

    // 6. 演示取消临时订阅（必须使用同一个委托变量）
    Console.WriteLine("\n--- 场景 5: 取消临时订阅者 ---");

    plcPublisher.DataChanged -= tempHandler; // 正确取消
    plcPublisher.OnSimulateValueUpdate(9.9f);
    Console.WriteLine("\n--- 场景 6: using 块结束，显示器订阅者自动取消订阅 ---");
}
Console.WriteLine("\n--- 场景 7: 全部取消后，发布者无响应 ---");
plcPublisher.OnSimulateValueUpdate(0.0f);
#endregion

#region Async logger
Console.WriteLine("========== Week 2 Day 1: 异步日志管道测试 ==========\n");

using IHost host = Host.CreateDefaultBuilder(args).ConfigureServices((context, services) => {
    //将AsyncLogger注册为单例，使整个应用共享
    services.AddSingleton<AsyncLogger>();
    //注册服务
    services.AddHostedService<LoggingBackgroundService>();
}).Build();

//启动host，这会触发启动LoggingBackgroundService中的ExcuteAsync
await host.StartAsync();

//获取注册过的AsyncLogger实例
var logger = host.Services.GetRequiredService<AsyncLogger>();

Console.WriteLine("开始模拟写入 100 条日志（观察控制台实时输出）...\n");

//模拟业务线程同时写入日志（高并发场景）
for (int i = 0; i < 100; i++)
{
    var entry = i % 10 == 0
        ? LogEntry.CreateError($"PLC-{i % 5} 通讯超时 (Code: 0x{i:X2})", $"PLC-{i % 5}")
        : LogEntry.CreateInfo($"采集到数据点 #{i}", $"Device-{i % 3}");

    await logger.LogAsync(entry); // 这里会立刻返回，不会阻塞循环
}

Console.WriteLine("\n所有日志已投递到队列，等待 2 秒让后台消费完...");
await Task.Delay(2000); // 给后台消费者一点时间处理

//优雅关闭主机（会触发 StopAsync -> FlushAsync）
Console.WriteLine("\n正在关闭主机，确保所有日志被刷新...");
await host.StopAsync();

Console.WriteLine("\n========== 测试完成，按任意键退出 ==========");
#endregion

#region Async logger with pressure
Console.WriteLine("========== Week 2 Day 2: 异步日志 + 背压 + 重试测试 ==========\n");

using IHost host = Host.CreateDefaultBuilder(args).ConfigureServices((context, services) =>
{
    services.AddSingleton<AsyncLogger>(sp => new AsyncLogger(channelCapacity: 50));//返回AsyncLogger单例，设置最大channel长度为50

    services.AddHostedService<LoggingBackgroundService>();//注册服务
}).Build();

await host.StartAsync();

var logger = host.Services.GetRequiredService<AsyncLogger>();//获取注册过的AsyncLogger单例

Console.WriteLine("开始模拟高并发写入 500 条日志（通道容量仅 50，会触发背压）...\n");

var stopwatch = Stopwatch.StartNew();

// 同时启动 10 个生产者任务，每个写 50 条 = 500 条
Task[] producers = new Task[10];
for (int i = 0; i < producers.Length; i++)
{
    int producerId = i;
    producers[i] = Task.Run(async () =>
    {
        for (int j = 0; j < 50; j++)
        {
            var entry = j % 5 == 0
                ? LogEntry.CreateError($"Producer-{producerId} 模拟故障", $"PLC-{producerId}")
                : LogEntry.CreateInfo($"Producer-{producerId} 数据点 #{j}", $"Device-{producerId}");

            // 【关键】LogAsync 内部会因背压而异步等待，所以总体写入时间会被拉长。
            await logger.LogAsync(entry);
        }
        Console.WriteLine($"生产者 {producerId} 完成。");
    });
}

await Task.WhenAll(producers);

stopwatch.Stop();

Console.WriteLine($"\n所有日志已投递完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");
Console.WriteLine("等待 3 秒让后台消费完...");
await Task.Delay(3000);

await host.StopAsync();
Console.WriteLine("\n测试完成，按任意键退出。");

#endregion

#region queue logs
IndustrialAI.Core.Foundation.ClosureAndBoxingDemo.Run();


IndustrialAI.Core.Foundation.MemoryBarrierDemo.Run();



Console.WriteLine("\n========== Week 2 Day 3: 日志批量/限流测试 ==========\n");

using IHost host = Host.CreateDefaultBuilder(args).ConfigureServices((context, services) =>
{
    services.Configure<LoggerOptions>(options => {
        options.ChannelCapacity = 50;
        options.BatchSize = 10;
        options.BatchIntervalMilliseconds = 200;
        options.MaxRetryAttempts = 2;
    });
    services.AddSingleton<AsyncLogger>();
    //*****************************************************************************************************
    //注意，虽然现在的AsyncLogger构造函数中有一个参数Ioptions，但是此处注册单例对象的时候并没有传入IOptions的参数
    //因为此时DI容器会优先选择参数最多的构造函数，然后找到需要IOptions<logOptions>类型的参数，此时容器会在注册表里寻找是否存在该类型服务
    //IOptions<T>是框架自动注册的，容器可以找到这样的实例，然后直接传给了AsyncLogger构造函数
    //这就好比你去餐厅点了一份“主厨推荐套餐”（AddSingleton<AsyncLogger>()），后厨（DI容器）会根据菜单（服务注册表）自动为你配好例汤、主菜和甜点（IOptions<LoggerOptions> 等依赖项）。
    //当构造函数中存在容器没有存在的类型参数，此时才需要手动处理

    services.AddHostedService<LoggingBackgroundService>();
}).Build();

await host.StartAsync();

var logger = host.Services.GetService<AsyncLogger>();

//此处会报错，因为host.Services.GetService<LoggerOptions>() 结果是 null，是因为此处根本没有把 LoggerOptions 这个“类”注册到 DI 容器里，只注册了 IOptions<LoggerOptions> 这个接口。
//Console.WriteLine($"写入 120 条日志（观察批量输出，每批约 {host.Services.GetService<LoggerOptions>()!.BatchSize} 条或 {host.Services.GetService<LoggerOptions>()!.BatchIntervalMilliseconds}ms 超时）...\n");


//正确写法：
Console.WriteLine($"写入 120 条日志（观察批量输出，每批约 {host.Services.GetService<IOptions<LoggerOptions>>()!.Value.BatchSize} 条或 {host.Services.GetService<IOptions<LoggerOptions>>()!.Value.BatchIntervalMilliseconds}ms 超时）...\n");

for (int i = 0; i < 120; i++)
{
    await logger.LogAsync(LogEntry.CreateInfo($"数据点 #{i}", $"Node-{i % 4}"));
    await Task.Delay(2); // 模拟业务间隔
}

Console.WriteLine("\n等待 1 秒让最后一批触发超时...");
await Task.Delay(1000);

await host.StopAsync();

Console.WriteLine("\n测试完成，按任意键退出。");
#endregion


#region practice generic
GenericTest genericTest = new GenericTest();
//如果我把 where T : new() 约束去掉，改用 Activator.CreateInstance<T>()，编译能通过吗？运行时如果 T 没有无参构造，会抛出什么异常？（答：编译通过，运行时抛 MissingMethodException）
//因为Activator.CreateInstance实际上就是调用了无参构造函数来创建对象
//genericTest.CreateInstance<string>();
genericTest.CreateInstance<TestClass_0>();

//这两个都不会报错
//A是协变，支持右侧子类参数类型传递给左侧有继承关系的泛型类中的父类参数类型
//B是逆变，支持右侧父类给到左侧子类
IProducer<object> producer = new Producer<string>();  // 行 A
IConsumer<string> consumer = new Consumer<object>();  // 行 B


var b = new Broadcaster();
for (int i = 0; i < 10; i++) { var s = new Subscriber(b); }
b.Trigger(); // 会打印几次 "Hi"？//十次，相当于+=了10次，并依次执行输出
GC.Collect();
b.Trigger(); // 现在打印几次？//还是10次，因为事件是强引用，需要显式-=才可以被GC回收，需要Subscriber实现IDisposeable接口并在Dispose方法内-=事件或者使用弱引用类型包裹事件来使用

//IDisposable（-=）：显式、确定性、简单，但需要订阅者合作。

//WeakReference（弱事件）：自动、无需清理，但会导致性能开销、更复杂的委托管理（代理），并且如果处理程序是 lambda，仍有弱引用问题。
//澄清“为什么我们用两者（在我们的项目中）？”：在 EventBus 中，我们使用 WeakReference，因为发布者（EventBus）是长期存在的单例。我们这样做是为了让订阅者即使忘记取消订阅也能被 GC。然而，对于 Broadcaster / Subscriber 这种简单的一对一强耦合，IDisposable 更简单。
//WeakReference 必须包裹订阅者实例，或者委托必须指向订阅者的实例方法（而不是匿名方法），才能彻底安全
#endregion


#endif

using IHost host = Host.CreateDefaultBuilder(args).ConfigureServices((context, services) =>
{

    services.Configure<LoggerOptions>(options =>
    {
        // 极端压力测试参数
        options.ChannelCapacity = 5;          // 队列极短（制造背压）
        options.BatchSize = 2;                // 攒够2条就写
        options.BatchIntervalMilliseconds = 10000; // 超时设为10秒（故意让它几乎不起作用，纯粹靠条数触发）
        options.MaxRetryAttempts = 3;         // 减少重试干扰

        //options.ChannelCapacity = 50;
        //options.BatchSize = 10;
        //options.BatchIntervalMilliseconds = 200;
        //options.MaxRetryAttempts = 2;

        //文件参数
        options.LogDirectory = "E:/RealtimeCurve/IndustrialAI.Core/IndustrialAI.ConsoleDemo/bin/Debug/net8.0/IndustrialLogs";  // 可改成你想要的路径
        options.LogDirectory = "E:/RealtimeCurve/IndustrialAI.Core/IndustrialAI.ConsoleDemo/bin/Debug/net8.0/ReadOnlyLogs";  // 改为只读路径
        options.LogFileName = "device_events.log";
        options.MaxFileSizeMB = 10;
    });

    services.AddSingleton<AsyncLogger>();//创建单例对象

    services.AddHostedService<LoggingBackgroundService>();//创建后台一直运行的服务
}).Build();

await host.StartAsync();

var logger = host.Services.GetService<AsyncLogger>();

Console.WriteLine($"写入 200 条日志（观察批量输出，每批约 {host.Services.GetService<IOptions<LoggerOptions>>()!.Value.BatchSize} 条或 {host.Services.GetService<IOptions<LoggerOptions>>()!.Value.BatchIntervalMilliseconds}ms 超时）...\n");

Console.WriteLine($"日志文件将保存到: E:/RealtimeCurve/IndustrialAI.Core/IndustrialAI.ConsoleDemo/bin/Debug/net8.0/ReadOnlyLogs\\device_events.log");
Console.WriteLine("开始模拟写入 200 条日志（文件达到 2MB 时会自动滚动）...\n");

var stopwatch = Stopwatch.StartNew();

// 模拟多个生产者并行写入（触发背压）
Task[] producers = new Task[10];
for (int i = 0; i < producers.Length; i++)
{
    int producerId = i;
    producers[i] = Task.Run(async () =>
    {
        //for (int j = 0; j < 20; j++) // 每个生产者写 20 条，共 200 条
        //{
        //    var entry = j % 5 == 0
        //        ? LogEntry.CreateError($"Producer-{producerId} 模拟故障码 0x{j:X2}", $"PLC-{producerId}")
        //        : LogEntry.CreateInfo($"Producer-{producerId} 采集数据点 #{j}", $"Device-{producerId}");

        //    await logger.LogAsync(entry);
        //}

        for (int i = 0; i < 2; i++) // 每个生产者写 2 条
        {
            var entry = LogEntry.CreateInfo($"压力测试 #{i}", $"P{producerId}");
            await logger.LogAsync(entry);
            // await Task.Delay(1);  // 这一行必须删掉或注释掉！
        }
        Console.WriteLine($"生产者 {producerId} 完成。");
    });
}

await Task.WhenAll(producers);
stopwatch.Stop();
Console.WriteLine($"\n所有日志投递完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");

// 等待几秒，让后台服务将剩余日志刷入文件
Console.WriteLine("等待 3 秒让后台批量写入完成...");
await Task.Delay(3000);

// 4. 优雅关闭主机（触发 LoggingBackgroundService.StopAsync -> ForceFlushAndCloseAsync）
Console.WriteLine("\n正在关闭主机，强制刷新页缓存到磁盘...");
await host.StopAsync();

Console.WriteLine("\n测试完成。请到 Logs 目录检查日志文件，并验证是否发生了滚动（超过 1MB 后生成新文件）。");
Console.WriteLine("按任意键退出。");



Console.ReadKey();