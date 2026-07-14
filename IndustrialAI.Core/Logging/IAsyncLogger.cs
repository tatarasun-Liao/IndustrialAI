using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    public interface IAsyncLogger
    {
        //使用 ValueTask 而不是 Task，因为大多数情况下写入 Channel 是同步完成的，
        /// 可以避免分配 Task 对象，减少 GC 压力
        /// 
        /// <summary>
        /// 异步写入日志（非阻塞，立即返回）。
        /// </summary>
        /// <param name="entry">日志条目</param>
        /// <param name="cancellationToken">外部取消令牌（用于在应用程序关闭时拒绝新日志）</param>
        ValueTask LogAsync(LogEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试完成写入通道（表示不再接受新日志），并等待所有剩余日志被处理完毕。
        /// </summary>
        Task FlushAsync(CancellationToken cancellationToken = default);
    }
}
