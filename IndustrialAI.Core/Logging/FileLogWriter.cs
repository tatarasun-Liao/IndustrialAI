using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    /// <summary>
    /// 日志批量写入并按大小滚动，由于是基于AsyncLogger的单消费者模式（singleReader = true），所以不用加锁
    /// </summary>
    public sealed class FileLogWriter : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly string _baseFileName;
        private readonly long _maxFileSizeBytes;
        private readonly string _fullBasePath; // 基础完整路径
        private StreamWriter? _writer;
        private string _currentFilePath = string.Empty;
        private bool _disposed = false;

        private const int _streamBufferSize = 4096; // 4KB 缓冲区常量

        public FileLogWriter(LoggerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _directory = options.LogDirectory;
            _baseFileName = options.LogFileName;
            _maxFileSizeBytes = options.MaxFileSizeBytes;

            _fullBasePath = Path.Combine(_directory, _baseFileName);
        }


        /// <summary>
        /// 创建一个新的异步写入文件流
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask OpenNewFileAsync(CancellationToken cancellationToken)
        {
            // Directory.CreateDirectory 不会抛出异常即使目录已存在。
            Directory.CreateDirectory(_directory);
            //FileMode.Append 表示追加模式，如果文件不存在则创建。
            // FileAccess.Write 仅写，FileShare.Read 允许其他进程读取（但不允许写入）。
            //useAsync:true开启异步IO
            var stream = new FileStream(_fullBasePath, FileMode.Append, FileAccess.Write, FileShare.Read, _streamBufferSize, useAsync: true);

            // StreamWriter 默认使用 UTF-8 编码且不加 BOM。
            // leaveOpen = false 表示释放时关闭 FileStream。
            _writer = new StreamWriter(stream, Encoding.UTF8, _streamBufferSize, leaveOpen: false);

            // 【底层原理】由于我们使用了异步 I/O（useAsync: true），
            // WriteAsync 会将 I/O 请求投递到 Windows IOCP 或 Linux epoll，
            // 真正实现非阻塞写入。
            await Task.CompletedTask; // 这里无需异步操作，占位保持签名统一。
        }

        /// <summary>
        /// 打开写入器，并执行滚动
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask EnsureWriterAndRotateAsync(CancellationToken cancellationToken)
        {
            if (_writer == null)//首次写入，直接创建
            {
                await OpenNewFileAsync(cancellationToken);

                return;
            }

            var fileInfo = new FileInfo(_fullBasePath);//检查基础路径的文件大小

            if (fileInfo.Exists && fileInfo.Length >= _maxFileSizeBytes)
            {
                // 执行滚动前，必须先强制 Flush 当前流，防止数据丢失。
                // 注意：我们是异步写入，要确保之前所有异步写入操作都已完成。
                await _writer.FlushAsync();

                _writer.Dispose(); // 关闭当前文件句柄

                // 重命名操作 Move 是原子操作（在同一个卷内）。
                // 但创建时间戳可能重复（极端情况），我们加上精确到毫秒。
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var archiveName = $"{Path.GetFileNameWithoutExtension(_baseFileName)}_{timestamp}{Path.GetExtension(_baseFileName)}";
                var archivePath = Path.Combine(_directory, archiveName);

                // 如果目标文件已存在（极少发生），需要重试或抛异常。
                // 这里简单处理：如果存在则追加GUID戳后缀。
                if (File.Exists(archivePath))
                {
                    archivePath = Path.Combine(_directory,
                        $"{Path.GetFileNameWithoutExtension(_baseFileName)}_{timestamp}_{Guid.NewGuid():N}{Path.GetExtension(_baseFileName)}");
                }

                File.Move(_fullBasePath, archivePath);
                Console.WriteLine($"[FileLog] 文件滚动: {_fullBasePath} -> {archivePath}");

                // 重新创建新文件
                await OpenNewFileAsync(cancellationToken);
            }
        }

        /// <summary>
        /// 异步追加写入
        /// </summary>
        /// <returns></returns>
        public async ValueTask AppendBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileLogWriter));

            if (entries == null || entries.Count == 0) return;

            // 检查文件大小，
            // 若超限则执行滚动（重命名 + 重新创建）。
            await EnsureWriterAndRotateAsync(cancellationToken);


            // 使用 StringBuilder 一次性构建整批日志文本，
            // 减少多次 Write 调用带来的上下文切换开销。
            var sb = new StringBuilder(entries.Count * 128);//预估的一整批日志占用容量

            foreach (var entry in entries)
            {
                // 【格式】[2026-01-15 14:30:00.123] [Info] Source: Message
                sb.Append('[')
                  .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append("] [")
                  .Append(entry.Level)
                  .Append("] ")
                  .Append(entry.Source ?? "Global")
                  .Append(": ")
                  .AppendLine(entry.Message);
            }

            //StreamWriter.WriteAsync 将数据写入 .NET 内部缓冲区，
            // 并未立即写入磁盘（进入操作系统页缓存）。
            await _writer!.WriteAsync(sb.ToString());

            // FlushAsync(false) 仅清空 .NET 内部缓冲区，将数据提交给操作系统。
            // 此时数据仍在页缓存中，并未持久化到物理磁盘。
            // 我们不使用 Flush(true) 强制刷盘，因为会严重降低吞吐量。
            await _writer.FlushAsync();
        }


        /// <summary>
        /// 强制将系统缓存页写入磁盘，只有当出现重大事故时才紧急调用
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask ForceFlushAndCloseAsync(CancellationToken cancellationToken = default)
        {
            if (_writer == null || _disposed) return;

            //FlushAsync(true) 会调用 FileStream.Flush(true),这个操作是同步的,即使是异步调用，底层也是串行化写入，所以可以直接使用dispose，并仅在关机前使用
            await _writer!.FlushAsync();
            _writer.Dispose();
            _writer = null;
            Console.WriteLine("[FileLog] 文件已强制刷新并关闭。");
        }

        /// <summary>
        /// 实现IDisposeAsync接口
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            _disposed = true;

            if (_writer != null)
            {
                //GC.SuppressFinalize 不需要我们显式调用
                //只需要保证异步释放完成
                await _writer!.DisposeAsync();

                _writer = null;
            }

            Console.WriteLine("[FileLog] 资源已释放。");
        }
    }
}
