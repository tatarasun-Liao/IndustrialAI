using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly IOptions<LoggerOptions> _options;

        private readonly AsyncLogger _asyncLogger;

        private readonly ILogger<LoggingBackgroundService>? _logger; // 可选，用于记录服务自身状态

#if false
        public LoggingBackgroundService(AsyncLogger asyncLogger, ILogger<LoggingBackgroundService>? logger = null)
        {
            _asyncLogger = asyncLogger;
            _logger = logger;
        }
#endif

        /// <summary>
        /// Services最好在注册是直接使用options，通过构造函数注入
        /// </summary>
        /// <param name="asyncLogger"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public LoggingBackgroundService(AsyncLogger asyncLogger, IOptions<LoggerOptions> options, ILogger<LoggingBackgroundService>? logger = null)
        {
            _asyncLogger = asyncLogger;
            _logger = logger;
            _options = options;
        }


#if false
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
#endif

        /// <summary>
        /// 在程序启动的时候注册了AsyncLogger单例对象和service，在host.AsyncStart()的时候会启动service中的ExcuteAsync内的方法，该方法内执行了ConsumeAndEmitAsync方法，
        /// 该方法内通过await readAllAsync方法实现了channel内数据的异步循环读取，readAllAsync会创建一个Task，该task在channel内有数据时被唤醒执行readAllAsync内部逻辑，
        /// 当channel内没有数据时该task被挂起，等待channel有数据时再次唤醒，并没有通过while轮询方式持续读取channel内是否有数据，而是通过channel内部的通知机制完成的
        /// 
        /// "ReadAllAsync 在没有数据时挂起"	源码中调用 MoveNextAsync()，发现队列空，会返回一个 ValueTask<bool>，其内部状态为 IValueTaskSource 挂起状态，不占用任何操作系统线程。
        //"有数据时通过 WriteAsync 唤醒"	你的生产者调用 Writer.TryWrite（或 WriteAsync），底层会执行 Interlocked 操作入队，然后调用 SemaphoreSlim.Release()。
        //"挂起的 Task 被唤醒执行内部逻辑"	SemaphoreSlim 释放信号量后，会从等待队列中取出挂起的 ValueTask，将其状态标记为 Completed，并立即触发 MoveNextAsync 的后续逻辑（即你的 foreach 循环体）
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger?.LogInformation("日志后台服务已启动。");

            var fileWriter = new FileLogWriter(_options.Value);

            try
            {
                //常驻监听，方法内有一个一直运行的foreach循环
                await _asyncLogger.ConsumeAndEmitAsync(
            batchEmitter: async (batch) =>
            {
                // 将批量日志写入磁盘文件。
                await fileWriter.AppendBatchAsync(batch, stoppingToken);

                // 【调试输出】保留少量控制台打印，方便观察滚动事件。
                Console.WriteLine($"--> [File] 已写入 {batch.Count} 条日志到 {_options.Value.LogFileName}");
            },
            externalCancellationToken: stoppingToken
        );
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Logger service cancelled, force flushing file...");
                // 取消时强制将页缓存刷入磁盘，防止数据丢失。
                await fileWriter.ForceFlushAndCloseAsync(stoppingToken);
                throw; // 重新抛出，让主机知道服务已停止。
            }
            finally
            {
                await fileWriter.DisposeAsync();
            }
            _logger?.LogInformation("Logger service stopped.");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger?.LogInformation("正在停止日志后台服务，刷新剩余日志...");
            await _asyncLogger.FlushAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}
