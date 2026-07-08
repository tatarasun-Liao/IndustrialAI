// See https://aka.ms/new-console-template for more information
using IndustrialAI.Core;

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