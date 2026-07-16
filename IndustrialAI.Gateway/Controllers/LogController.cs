using IndustrialAI.Core.Logging;
using IndustrialAI.Core.Observability;
using IndustrialAI.Gateway.Models;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialAI.Gateway.Controllers
{
    /// <summary>
    /// ApiController 特性，告诉框架这个类对外提供 HTTP API
    /// </summary>
    [ApiController]
    //路由模板，[controller] 会被替换为类名去掉 Controller 的部分，即 "log"
    //路由的访问类似洋葱一层一层，localhost:xxxx/api/log/方法名标注的特性名称  **************************************
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        private readonly AsyncLogger _logger;

        private readonly GatewayMetrics _metrics;
        //构造函数注入
        //没有new AsyncLogger，容器在创建 Controller 时，自动把单例的 AsyncLogger 送进来了
        public LogController(AsyncLogger logger, GatewayMetrics metrics) // 只要参数类型在容器里注册过，框架就会自动传
        {
            _logger = logger;

            _metrics= metrics;
        }

        [HttpGet("write")]
        public async Task<IActionResult> WriteLog(string message)
        {
            // 记录请求数
            _metrics.RecordRequest();

            //throw new Exception("测试抛出异常！");//模拟异常
            await _logger.LogAsync(LogEntry.CreateInfo($"API调用: {message}"));

            // 记录写入了 1 条日志
            _metrics.RecordLogsWritten(1);

            return Ok(new ApiResponse<string> { Success = true, Message = "message" });
        }

        /// <summary>
        /// 混合来源绑定
        /// 客户端只需调用 POST /api/log/batch/PLC-01，Body 只放消息列表，来源自动被路由参数填充
        /// </summary>
        /// <param name="batch"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        [HttpGet("batch/{batch}")]
        public async Task<IActionResult> LogBatch
            (
            [FromRoute] string source, // 从 /batch/5 中取 5,
            [FromBody] List<string> messages,// 从 Body 取 ["msg1", "msg2"]
            [FromQuery] string filter// 从 ?filter=error 中取 error
            )
        {
            foreach (var msg in messages)
            {
                await _logger.LogAsync(LogEntry.CreateInfo(msg, source));
            }
            return Accepted();
        }

        [HttpPost("batch")]
        public async Task<IActionResult> LogBatch([FromBody] List<BatchLogEntryDto> logs)//传入对象
        {
            // 如果传空数组，直接返回 400，不要往里走
            if (logs == null || logs.Count == 0)
                return BadRequest(new { Error = "日志列表不能为空" });

            foreach (var dto in logs)
            {
                // 根据数据对象 的 Level 动态创建不同的 LogEntry
                var entry = dto.Level?.ToLower() switch
                {
                    "error" => LogEntry.CreateError(dto.Message, dto.Source),
                    _ => LogEntry.CreateInfo(dto.Message, dto.Source)
                };
                await _logger.LogAsync(entry);
            }
            //日志是异步写入磁盘的，调用 API 时日志可能还在 Channel 里排队，尚未落盘。此时返回 200 OK 其实有点“撒谎”。工业级网关通常返回 202 Accepted，表示“我给你收下了，正在排队处理”
            //return Ok(new { Status = "Logged", Count = logs.Count });

            return Accepted(new ApiResponse<string> { Success = true, Message = "Queued" });
        }

    }
}
