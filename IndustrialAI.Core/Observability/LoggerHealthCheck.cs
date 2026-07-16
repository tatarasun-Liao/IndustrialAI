using IndustrialAI.Core.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Observability
{
    public class LoggerHealthCheck : IHealthCheck//必须实现IHealthCheck来检查channel队列负载健康程度
                                                 //（是否是通过亚健康状态来通知客户端底层任务队列繁忙来减缓任务调用数量避免达到channel有界通道的上限触发任务等待？？？）
    {
        private readonly AsyncLogger _logger;
        private int _channelCapacity;
        private readonly int _capacityThresholdPercent = 80; // 当队列堆积超过 80% 时触发亚健康

        public LoggerHealthCheck(AsyncLogger logger, IOptions<LoggerOptions> options)
        {
            _logger = logger;
            _channelCapacity = options.Value.ChannelCapacity;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        {
            // 1. 读取当前队列深度（因为我们在 AsyncLogger 里添加了 PendingCount）
            int pending = _logger.PendingCount;

            int _limitCount = _channelCapacity * _capacityThresholdPercent / 100;

            if (pending > _limitCount)
            {
                //当负载超过80%触发亚健康状态
                return Task.FromResult(HealthCheckResult.Degraded(
                    description: $"日志队列积压严重 ({pending} 条待处理)"));
            }
            else if (pending > _limitCount / 4)
            {
                //当负载大于20%标记正常状态
                return Task.FromResult(HealthCheckResult.Healthy(
                    description: $"日志队列正常 ({pending} 条待处理)"));
            }
            else
            {
                //负载小于20%标记通畅
                return Task.FromResult(HealthCheckResult.Healthy("日志管道运行通畅"));
            }
        }
    }
}
