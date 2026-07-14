using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    /// <summary>
    /// 使用channel实现异步日志，因为channel内部使用无锁并发的数据结构
    /// </summary>
    public sealed class AsyncLogger : IAsyncLogger, IAsyncDisposable
    {
#if false
        /// <summary>
        /// 一般来说需要使用有界通道限制容量来防止内存溢出，但是此处防止日志突发写入，前期使用无界通道（容量无限）
        /// </summary>
        private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions//设置多渠道写入，单渠道读取
            {
                //允许单个消费者从通道中读取数据（我们的 BackgroundService 是唯一消费者）
                SingleReader = true,
                // 允许多个生产者（业务线程可以并发调用 LogAsync）
                SingleWriter = false
            }
            );
#endif

        //真正生产环境的话需要注重负载性，所以不可以使用无界通道，而是使用固定长度的有界通道，当队列满时（生产者速度大于消费者速度），生产者会异步等待
        //注意，容量设置需要结合实际业务，如果消费者处理很慢，而生产者处理很快，则容量满了会反压上游
        private readonly Channel<LogEntry> _channel;
        private readonly IOptions<LoggerOptions> _options;//注入Options
        private readonly CancellationTokenSource _drainCts = new(); // 用于主动排空时的超时控制
        private readonly int _maxRetryAttempts = 3;
        // 记录是否已经调用过 Complete()，防止重复关闭导致异常
        private bool _isCompleted = false;

        //限流（Throttling）：每秒最多写入 100 条日志，防止下游 I/O（如机械硬盘）被打爆。
        //批量写入（Batching）：攒够 50 条或 500ms 才调用一次 onLogReceived，大幅减少 I/O 次数
        // 【新增】批量/限流参数
        private readonly int _batchSize = 50;          // 每批最大条数
        private readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(500); // 批次间隔

        /// <summary>
        /// 通过options注入的方式，并且在启动的时候通过DI检查值，保证传值安全性
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public AsyncLogger(IOptions<LoggerOptions> options)
        {
            //注入options对象
            _options = options ?? throw new ArgumentNullException(nameof(options));//判断是否为空

            // 【底层原理】Options 验证：如果数值非法，DI 容器会在启动时抛出 OptionsValidationException。
            // 这比运行时才报错要安全得多。
            var opts = _options.Value;

            var channelOpts = new BoundedChannelOptions(opts.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _channel = Channel.CreateBounded<LogEntry>(channelOpts);
        }


#if false
        /// <summary>
        /// 不使用配置，直接构造函数注入的方式
        /// </summary>
        /// <param name="channelCapacity"></param>
        public AsyncLogger(int channelCapacity = 1000)
        {
            var options = new BoundedChannelOptions(channelCapacity)//创建长度1000的channel
            {
                //FullMode = Wait 是默认值，表示当通道满时，WriteAsync 会异步等待。
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _channel = Channel.CreateBounded<LogEntry>(options);
        }

        ///无界通道同步写入方法
        ///valueTask是一种值类型，Task是一个引用类型，每一次返回一个Task都会在内存托管堆上重新开辟一块空间，频繁的Task会频繁出发GC，这样会拖慢cpu速度，
        ///而valueTask通过联合体包装器的模式，当直接返回结果的时候会直接返回包含数据的结构体，不会真的生成Task，只有当真的需要进行耗时工作的时候才会
        ///创建Task，此时与Task性能一致，但是ValueTask的缺点之一是不能多次await，且无法返回接口，当绝大多数执行路径可以立即返回结果时可以使用ValueTask，
        ///且不要把ValueTask存为全局变量，因为他只在当前方法调用栈中使用
        public async ValueTask LogAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            if (_isCompleted)
                throw new InvalidOperationException("日志管道已关闭，无法写入新日志。");

            //ChannelWriter.TryWrite 尝试同步写入，如果通道未满则返回 true
            //对于无界通道，TryWrite 永远返回 true，除非通道被标记为完成。
            // 因此这里绝大多数情况走同步快速路径，避免了异步状态机开销
            if (_channel.Writer.TryWrite(entry))

                return;//表示已完成返回结果等同于 ValueTask.CompletedTask

            //如果 TryWrite 失败（几乎不会发生，因为是无界），则回退到异步写入
            await WriteAsyncInternal(entry, cancellationToken);
        }

        
        private async ValueTask WriteAsyncInternal(LogEntry entry, CancellationToken cancellationToken)
        {
            // await 配合 CancellationToken，如果取消发生则抛出 OperationCanceledException。
            await _channel.Writer.WriteAsync(entry, cancellationToken);
        }
#endif

        public async ValueTask LogAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            if (_isCompleted)

                throw new InvalidOperationException("日志管道已关闭");

            //对于有界通道，TryWrite 可能失败（当通道满时）。
            // 我们直接使用 WriteAsync，它会自动等待直到队列有空位。
            await _channel.Writer.WriteAsync(entry, cancellationToken);
        }

#if false
        /// <summary>
        /// 消费日志的“消费者方法”。由 BackgroundService 调用。
        /// 这个方法会一直运行，直到 cancellationToken 被触发。
        /// </summary>
        /// <param name="onLogReceived">处理日志的回调委托（如写入文件、控制台、数据库）</param>
        public async Task ConsumeAsync(Func<LogEntry, ValueTask> onLogRecieved, CancellationToken cancellationToken)
        {
            if (onLogRecieved == null)

                throw new ArgumentNullException(nameof(onLogRecieved));

            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await onLogRecieved(entry);
                }
            }
            catch (OperationCanceledException)
            {
                // 捕获取消异常后，必须继续尝试读取通道中剩余的数据。
                // 因为 ReadAllAsync 在取消时，会把通道 Reader 标记为完成，但可能还有数据未读。
                // 所以我们用 while 循环配合 TryRead 来耗尽剩余数据。
                Console.WriteLine("[AsyncLogger] 收到取消信号，开始处理剩余日志...");
                while (_channel.Reader.TryRead(out var remaining))
                {
                    await onLogRecieved(remaining);
                }
                Console.WriteLine("[AsyncLogger] 所有剩余日志已处理完毕。");
            }
        }
#endif

#if false

        /// <summary>
        /// 加入指数退避重试的消费者方法
        /// 添加限流和批量处理
        /// </summary>
        /// <param name="onLogRecieved"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task CosumeAsync(Func<LogEntry, ValueTask> onLogRecieved, CancellationToken cancellationToken)
        {
            if (onLogRecieved == null) throw new ArgumentNullException(nameof(onLogRecieved));

            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await ProcessWithRetryAsync(entry, onLogRecieved, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[AsyncLogger] 收到取消信号，处理剩余日志...");
                while (_channel.Reader.TryRead(out var remaining))
                {
                    await ProcessWithRetryAsync(remaining, onLogRecieved, cancellationToken);
                }
                Console.WriteLine("[AsyncLogger] 剩余日志已强制处理（忽略重试）。");
            }
        }
#endif

        /// <summary>
        /// 从options直接读取配置信息进行消费的消费者限流队列处理
        /// </summary>
        /// <param name="batchEmitter"></param>
        /// <param name="externalCancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task ConsumeAndEmitAsync(Func<IReadOnlyList<LogEntry>, ValueTask> batchEmitter, CancellationToken externalCancellationToken)
        {
            if (batchEmitter == null) throw new ArgumentNullException(nameof(batchEmitter));

            //批量处理数据长度
            var buffer = new List<LogEntry>(_options.Value.BatchSize);

            var batchSize = _options.Value.BatchSize;

            //批量处理时间间隔
            var interval = _options.Value.BatchInterval;

            //重试次数
            var maxAttempts = _options.Value.MaxRetryAttempts;

            using var timer = new System.Timers.Timer(interval.TotalMilliseconds);//计时器定时

            timer.AutoReset = false;
            var timerElapsed = false;
            //当计时器到达设定的时间间隔（Interval）时，就会触发这个事件
            //(_, _)：这是 C# 7.0 引入的 “弃元（Discard）” 语法。计时器的 Elapsed 事件原始签名是 (object sender, ElapsedEventArgs e)。因为你在逻辑里根本不需要知道“是谁触发的（sender）”和“带了什么参数（e）”，所以直接用两个 _ 占位，告诉编译器：“这两个参数我不要了，别给它们分配名字”。
            timer.Elapsed += (_, _) => timerElapsed = true;//到达定时，自动执行

            try
            {
                timer.Start();

                await foreach (var entry in _channel.Reader.ReadAllAsync(externalCancellationToken))
                {
                    buffer.Add(entry);

                    if (buffer.Count > batchSize)//如果是到达批量触发条件
                    {
                        await EmitBatchWithRetryAsync(buffer, batchEmitter, maxAttempts, externalCancellationToken);
                        buffer.Clear();
                        timer.Stop(); // 重置计时器
                        timer.Start();
                    }
                    else if (timerElapsed)//如果达到定时触发条件
                    {
                        timerElapsed = false;

                        if (buffer.Count > 0)
                        {
                            await EmitBatchWithRetryAsync(buffer, batchEmitter, maxAttempts, externalCancellationToken);
                            buffer.Clear();
                        }

                        timer.Start();//重置计时
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //强制排空剩余数据
                await DrainRemainingAsync(buffer, batchEmitter, maxAttempts);
            }
            finally
            {
                timer.Dispose();
            }
        }


        /// <summary>
        /// 包含重试的触发处理管道
        /// </summary>
        /// <param name="batch"></param>
        /// <param name="batchEmitter"></param>
        /// <param name="maxAttempts"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask EmitBatchWithRetryAsync(IReadOnlyList<LogEntry> batch, Func<IReadOnlyList<LogEntry>, ValueTask> batchEmitter, int maxAttempts, CancellationToken cancellationToken)
        {
            if (batch.Count == 0) return;

            int attempt = 0;
            Exception? lastError = null;

            while (attempt < maxAttempts)
            {

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await batchEmitter(batch);
                    return; // 成功
                }
                catch (Exception ex) when (attempt < maxAttempts - 1)
                {
                    lastError = ex;

                    attempt++;

                    //****************************************************8
                    // 【底层原理】指数退避使用左移运算符加速：2^attempt 秒。
                    // 左移比 Math.Pow 快，且完全基于整数，避免浮点误差。
                    var delaySeconds = 1 << attempt; // 2, 4, 8 秒

                    Console.WriteLine($"[Retry] Batch ({batch.Count}) failed. Retry in {delaySeconds}s...");

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }

            Console.WriteLine($"[Critical] Batch ({batch.Count}) dropped. Last Error: {lastError?.Message}");
        }

        /// <summary>
        /// 排空剩余数据
        /// </summary>
        /// <returns></returns>
        private async ValueTask DrainRemainingAsync(List<LogEntry> buffer, Func<IReadOnlyList<LogEntry>, ValueTask> batchEmitter, int maxAttempts)
        {
            Console.WriteLine("[AsyncLogger] Draining remaining logs...");

            if (buffer.Count > 0)
                //// 排空时不等待重试延迟（传入 CancellationToken.None 快速失败）********************************
                await EmitBatchWithRetryAsync(buffer, batchEmitter, maxAttempts, CancellationToken.None);

            while (_channel.Reader.TryRead(out var entry))
            {
                var singleBatch = new List<LogEntry> { entry };
                await EmitBatchWithRetryAsync(singleBatch, batchEmitter, maxAttempts, CancellationToken.None);
            }

            Console.WriteLine("[AsyncLogger] Drain completed.");
        }


        /// <summary>
        /// 关闭数据通道
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task CompleteAndDrainAsync(CancellationToken cancellationToken = default)
        {
            if (_isCompleted) return;

            _isCompleted = true;

            try
            {
                _channel.Writer.Complete();
            }
            catch (ChannelClosedException)
            {
                
            }

            await Task.CompletedTask;//结束线程在方法外部，此处仅作为标记
        }


        /// <summary>
        /// 消费者可以批量处理与限流
        /// </summary>
        /// <param name="onBatchReceived"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task CosumeAsync(Func<List<LogEntry>, ValueTask> onBatchReceived, CancellationToken cancellationToken)
        {
            if (onBatchReceived == null) throw new ArgumentNullException(nameof(onBatchReceived));

            var batch = new List<LogEntry>(_batchSize);

            var timer = new System.Timers.Timer(_batchInterval.TotalMilliseconds) { AutoReset = false };
            bool timerElapsed = false;

            // 定时器触发时，执行刷新
            timer.Elapsed += (_, _) =>
            {
                timerElapsed = true;
                // 注意：这里不能直接调用 ProcessBatchWithRetryAsync，因为这是非异步事件。
                // 我们用信号量来通知主循环。
            };

            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
                {

                    batch.Add(entry);

                    if (batch.Count >= _batchSize)//判断达到批次处理大小，则处理
                    {
                        await ProcessBatchWithRetryAsync(batch, onBatchReceived, cancellationToken);

                        batch.Clear();

                        timer.Stop();//重置计时器

                        timer.Start();
                    }
                    else
                    {
                        //若计时器还未启动，则开始计时器
                        if (!timerElapsed)

                            timer.Start();
                    }

                    //如果定时器触发，则触发执行
                    if (timerElapsed)
                    {
                        timerElapsed = false;

                        if (batch.Count > 0)
                        {
                            await ProcessBatchWithRetryAsync(batch, onBatchReceived, cancellationToken);

                            batch.Clear();
                        }

                        timer.Start();//通过该种方式执行后，重置计时器，避免重复触发
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[AsyncLogger] 收到取消信号，处理剩余日志...");

                //强制执行处理剩余数据
                if (batch.Count > 0)
                {
                    await ProcessBatchWithRetryAsync(batch, onBatchReceived, CancellationToken.None);//为了强制退出时不阻塞太久，传入CancellationToken.None，仅仅重试一次，不等待延迟
                }
                while (_channel.Reader.TryRead(out var remaining))
                {
                    await ProcessBatchWithRetryAsync(new List<LogEntry> { remaining }, onBatchReceived, CancellationToken.None);
                }
                Console.WriteLine("[AsyncLogger] 剩余日志已强制处理（忽略重试）。");
            }
            finally
            {
                timer.Dispose();
            }
        }

        /// <summary>
        /// 处理带指数退避重试的一批日志
        /// </summary>
        /// <param name="batch"></param>
        /// <param name="onBatchReceived"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask ProcessBatchWithRetryAsync(List<LogEntry> batch,
    Func<List<LogEntry>, ValueTask> onBatchReceived,
    CancellationToken cancellationToken = default)
        {
            if (batch.Count == 0)

                return;

            int attempt = 0;

            Exception? lastException = null;

            while (attempt < _maxRetryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await onBatchReceived(batch);

                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts - 1)//when是一种条件判断，只有满足条件才会进入catch执行逻辑
                {
                    lastException = ex;

                    attempt++;

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    Console.WriteLine($"[批量重试] 批次 ({batch.Count} 条) 失败，{delay}s 后重试...");

                    // 【修复点】传入 cancellationToken，外部取消时会立即抛出 OperationCanceledException
                    await Task.Delay(delay, cancellationToken);
                }
            }
            // 重试耗尽，记录严重错误
            Console.WriteLine($"[严重错误] 批次 ({batch.Count} 条) 最终失败，已丢弃。错误: {lastException?.Message}");
        }
        private async ValueTask ProcessWithRetryAsync(LogEntry entry,
        Func<LogEntry, ValueTask> onLogReceived,
        CancellationToken cancellationToken)
        {
            int attempt = 0;

            Exception? lastException = null;

            while (attempt < _maxRetryAttempts)
            {
                //如果外部取消，则立即退出，不再重试
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await onLogReceived(entry);

                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts - 1)
                {
                    lastException = ex;

                    attempt++;

                    //指数退避：计算等待时间 2^attempt 秒
                    var delaySeconds = Math.Pow(2, attempt);

                    Console.WriteLine($"[重试] 日志处理失败 (尝试 {attempt}/{_maxRetryAttempts})，" +
                                  $"等待 {delaySeconds}s 后重试。错误: {ex.Message}");

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
            // 如果所有重试都失败，记录最终错误并丢弃日志（防止死循环）
            Console.WriteLine($"[严重错误] 日志 {entry.Message} 在 {_maxRetryAttempts} 次重试后仍失败，" +
                              $"已丢弃。最终错误: {lastException?.Message}");
        }

        /// <summary>
        /// 标记通道完成，等待所有消费者处理完成所有数据
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_isCompleted) return;

            _isCompleted = true;

            try
            {
                //此处标记complete后reader会自然退出，但是如果reader非自然退出，此处complete会报错，所以使用
                //try catch包裹
                _channel.Writer.Complete();
            }
            catch (ChannelClosedException)
            {

            }

            //flush方法的核心就是设置writer为complete，用来触发消费者停止，但是实际上的停止需要在consumAsync中进行，而不是flush方法内部，flush方法仅代表写入方法结束，等待外部消费者方法返回
            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await FlushAsync();
    }
}
