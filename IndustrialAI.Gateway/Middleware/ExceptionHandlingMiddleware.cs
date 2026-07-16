using IndustrialAI.Core.Logging;
using IndustrialAI.Gateway.Models;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

namespace IndustrialAI.Gateway.Middleware
{
    //这是一个标准的中间件类，它必须有一个构造函数（接收 RequestDelegate）和一个 InvokeAsync 方法
    public class ExceptionHandlingMiddleware
    {
        // 这是“指向下一个工人的交接按钮”
        private readonly RequestDelegate _next;

        private readonly AsyncLogger _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, AsyncLogger logger)//是否在Host里注册RequestDelegate并DI自动注入？？？
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// 工人干活的地方
        /// </summary>
        /// <param name="context">HttpContext 就是那个“大包裹”，里面装着 Request 和 Response</param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);//按下按钮，把包裹沿着管道传递给后方下一个人
            }
            catch (Exception ex) //此处捕获的是沿着管道后方的代码出现的问题，所以作为一个捕获异常的中间件，需要放在管道最前端，这样才能捕获管道后面所有异常
            {
                //异常记录日志
                await _logger.LogAsync(LogEntry.CreateError($"API全局异常: {ex.Message}", "Middleware"));

                //输出固定格式响应，不将脏数据抛给客户端（不让客户端看到异常数据，仅仅告知结果？？？）
                var errorResponse = new ApiResponse<string>
                {
                    Success = false,
                    Message = "服务器内部处理失败，请稍后重试。"
                    // 这里你也可以加上 ex.Message，但产线环境建议隐藏细节，只输出固定的友好提示
                };

                //对象序列化为json字符串
                var jsonResponse = JsonSerializer.Serialize(errorResponse);

                //修改响应中的响应头中的数据类型，告知客户端响应json格式
                context.Response.ContentType = "application/json";

                //统一响应200，在body里区分
                context.Response.StatusCode = StatusCodes.Status200OK;

                //将json写入响应流
                await context.Response.WriteAsync(jsonResponse);
            }
        }
    }
}
