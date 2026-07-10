// See https://aka.ms/new-console-template for more information
using IndustrialAI.Core;
using IndustrialAI.Core.Attributes;
using IndustrialAI.Core.Cache;
using IndustrialAI.Core.Driver;
using IndustrialAI.Core.Events;
using IndustrialAI.Core.Factory;
using IndustrialAI.Core.Model;
using IndustrialAI.Core.Training.DelegateEvent;
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

#endif

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

Console.ReadKey();