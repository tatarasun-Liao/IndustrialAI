// See https://aka.ms/new-console-template for more information
using IndustrialAI.Core;
using IndustrialAI.Core.Attributes;
using IndustrialAI.Core.Cache;
using IndustrialAI.Core.Driver;
using IndustrialAI.Core.Events;
using IndustrialAI.Core.Factory;
using IndustrialAI.Core.Model;
using System.Reflection;


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
}
#endregion