
//与控制台项目中Host.CreateDefaultBuilder(args)一致，都是创建应用程序的构造器，只是后者额外内置了处理 HTTP 请求的管道。
//args是启动时的命令行传参，本质上是String数组，可以让程序支持启动时的配置用args参数覆盖从appconfig.json中读取到的配置参数
//内部依然使用的是Host.CreateDefaultBuilder(args)但是额外附加了Http服务器配置
using IndustrialAI.Core.Logging;
using IndustrialAI.Core.Observability;
using IndustrialAI.Gateway.Middleware;
using Microsoft.Extensions.Options;

//--------------------------*********************************-------------------------
//DI 容器的核心目的，从来不是为了存放“全局变量”。 它的核心目的是 “管理对象的生命周期和依赖关系”。
//1.如果你希望一个对象在多个类之间共享同一个实例（跨类复用），注册它。
//（比如 AsyncLogger，你的 Controller 和 BackgroundService 需要共用同一个队列。）
//2.如果你希望框架帮你自动清理资源（释放非托管句柄），注册它。
//（比如 HttpClient，注册后框架会在应用关闭时自动调用 Dispose。）
//3.注意，值类型或者短生命周期的类型不需要注册

//4.容器注册的不一定都是单例，也可以是我们希望调用后自动释放的资源，有一个特殊的生命周期叫 Scoped（作用域）。它的存活时间既不是整个进程（单例），也不是瞬间消亡（瞬时），而是 “一次 HTTP 请求”，
//因为你希望容器帮你创建它，并在请求结束时帮你销毁它（释放内存），而手动 new 则无法享受这个自动清理的福利
//5.数据库连接资源（特别是 EF Core 的 DbContext）绝对不能注册为单例（Singleton）
//内存泄漏（Change Tracker 膨胀）：DbContext 内部有一个“变更跟踪器”，它会记住所有查询过的实体对象，以便在 SaveChanges 时自动生成更新语句。如果它是单例，意味着整个程序的生命周期里只有一个 DbContext 实例，它会记住成百上千万条记录，直到内存爆掉。
//线程安全问题：DbContext 不是线程安全的。如果多个并发 HTTP 请求同时使用同一个单例 DbContext，会抛出 InvalidOperationException（“第二个操作在此上下文开始...”）。WebAPI 天然是多线程环境，单例 DbContext 等于自杀。
//连接池浪费：数据库底层有连接池（Connection Pool）。单例 DbContext 会长期霸占一个连接，而无法充分利用连接池的复用能力，导致高并发时连接不足。
//正确的做法（微软官方标准）：
//在 WebAPI 中，DbContext 必须注册为 Scoped（作用域）——即每个 HTTP 请求创建一个新的 DbContext 实例。
//

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<LoggerOptions>(options =>
{
    options.ChannelCapacity = 50;
    options.BatchSize = 10;
    options.BatchIntervalMilliseconds = 200;
    options.MaxRetryAttempts = 2;
    options.LogDirectory = "E:/RealtimeCurve/IndustrialAI.Core/IndustrialAI.ConsoleDemo/bin/Debug/net8.0/IndustrialLogs";
    options.LogFileName = "http_gateway.log";
    options.MaxFileSizeMB = 10;
});
builder.Services.AddSingleton<AsyncLogger>();//容器会自动扫描它的构造函数（需要 IOptions<LoggerOptions>），并自动把上面的配置传进去
builder.Services.AddHostedService<LoggingBackgroundService>();//后台服务：也是单例，由主机托管

//注册指标系统
builder.Services.AddMetrics();//注册IMeterFactory ，并用于后续构造函数自动注入
builder.Services.AddSingleton<GatewayMetrics>();//为什么注册指标类，框架就知道他应用于框架系统，是否是在AddMetrics时注册了IMeterFactory的实例，然后框架通过构造函数自动注入到GatewayMetrics？？？？

builder.Services.AddHealthChecks()
    .AddCheck<LoggerHealthCheck>("logger_check");//这一步是创建了一个健康检查集合，并在其中添加了一个健康检查实例命名为logger_check吗？？？是否表示我们可以注册多个健康检查类？？？
//是的，正确，而且在调用/health的时候会所有健康检查的结果，并且如果有一个是亚健康，则整体结果就是亚健康
//与builder.Services.AddSingleton<AsyncLogger>()完全一样的 DI 注册。前者注册了你的日志类，后者注册了 webapi 控制器相关的所有服务（比如模型绑定、格式化器、验证器等）。
builder.Services.AddControllers();


//为swagger准备的api元数据，swagger根据这两句话生成json文档，仅在调试接口阶段使用，可以弃用
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();//为swagger提供api元数据
builder.Services.AddSwaggerGen();//根据元数据生成文档


//将上面所有的服务和配置编译成一个可运行的http管道，生成应用程序实例
var app = builder.Build();

//将捕获异常中间件放在管道最前面，这样才能捕获整条管道的全部异常
//RequestDelegate 是在 UseMiddleware 这个扩展方法内部，由 ASP.NET Core 框架自动注入的，而不是显式的new 一个RequestDelegate，在运行时框架会分析中间件装载链条并动态生成向下一个中间件传递的委托RequestDelegate
//然后框架通过反射实例化ExceptionHandlingMiddleware类并将ExceptionHandlingMiddleware作为构造函数参数强行注入
//可以认为RequestDelegate是框架自动生成的管道基础设施，是框架根据调用顺序自动动态创建并强行注入中间件的，不需要手动创建，只需要写构造函数接收它，框架自然会识别并注入。这就是 ASP.NET Core 内置的 “特殊参数自动注入” 机制。
app.UseMiddleware<ExceptionHandlingMiddleware>();
//很多官方库会写 app.UseExceptionHandler() 或 app.UseCors()，实际上是调用了IApplicationBuilder 的扩展方法，本质上还是调用了UseMiddleware
//当后方管道内发生异常的时候，如果没有异常捕捉中间件，则会直接看到界面显示http异常，而经过中间件包裹后，不会报出http异常，而是可以在context中看到异常信息json

//中间件管道配置阶段，仅在开发环境下挂载 Swagger 测试页面，类似只在 Debug 模式下才打印某些日志一样
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
//强制将 HTTP 请求重定向到 HTTPS
app.UseHttpsRedirection();
//启用身份验证/授权中间件
app.UseAuthorization();
//路由映射阶段（核心），它告诉框架：“去扫描所有继承自 ControllerBase 的类，把他们身上的 [Route] 和 [HttpGet] 特性读出来，建立 URL 到具体方法的映射表。”
//与反射读取特性一样，框架在启动时扫描你的 LogController，发现它标记了 [Route("api/[controller]")]，就把 /api/log 这个 URL 和 WriteLog 方法绑定在一起
app.MapControllers();
app.MapHealthChecks("/health"); // 这样一来，访问 /health 就能看到网关的健康状态了  **********************************
//启动应用程序，阻塞当前线程，开始监听 HTTP 请求
app.Run();
