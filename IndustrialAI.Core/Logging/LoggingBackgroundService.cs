using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    /// <summary>
    /// BackgroundService 是 .NET Core 通用主机中执行后台任务的标准方式。
    /// 它会在应用启动时调用 StartAsync，关闭时调用 StopAsync。
    /// </summary>
    public sealed class LoggingBackgroundService : BackgroundService
    {
        private readonly AsyncLogger _asyncLogger;

        private readonly ILogger<LoggingBackgroundService>? _logger; // 可选，用于记录服务自身状态

        public LoggingBackgroundService(AsyncLogger asyncLogger, ILogger<LoggingBackgroundService>? logger = null)
        {
            _asyncLogger = asyncLogger;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger?.LogInformation("日志后台服务已启动。");

            try
            {
                //每次写入单条数据的异步执行方式
                //await _asyncLogger.ConsumeAsync(onLogRecieved: async (entry) =>
                //{
                //    await Task.Delay(1);//模拟磁盘写入等耗时行为
                //    Console.ForegroundColor = entry.Level == "Error" ? ConsoleColor.Red : ConsoleColor.Green;
                //    Console.WriteLine($"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Source}: {entry.Message}");
                //}, cancellationToken: stoppingToken);

                //批量写入数据处理方式
                await _asyncLogger.ConsumeAndEmitAsync(batchEmitter: async (batch) =>
                {
                    await Task.Delay(1);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"--> [批量写入] 共 {batch.Count} 条日志");
                    foreach (var entry in batch)
                    {
                        Console.ForegroundColor = entry.Level == "Error" ? ConsoleColor.Red : ConsoleColor.Green;
                        Console.WriteLine($"  [{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Source}: {entry.Message}");
                    }
                }, externalCancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "日志后台服务意外崩溃。");
                throw;
            }
            _logger?.LogInformation("日志后台服务已正常停止。");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger?.LogInformation("正在停止日志后台服务，刷新剩余日志...");
            await _asyncLogger.FlushAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}
