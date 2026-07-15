using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    /// <summary>
    /// 给 AsyncLogger 加上可配置参数（容量、批量大小、重试次数），并使用 .NET 官方的 IOptions 模式
    /// Options 结尾表示配置类。
    /// 使用 System.ComponentModel.DataAnnotations 提供验证逻辑（基础但极其实用）。
    /// 
    /// .NET 配置系统通过反射读取 LoggerOptions 类型的属性列表，然后用 JSON 解析器把同名的键值对填充进去。这个过程发生在 Host.CreateDefaultBuilder() 构建时，也就是程序启动阶段。
    /// IValidateOptions<TOptions> 是 .NET 提供的一个验证接口，专门用来校验配置对象的合法性。你可以在里面写任何验证逻辑
    /// public class LoggerOptionsValidator : IValidateOptions<LoggerOptions>
    //{
    //    public ValidateOptionsResult Validate(string? name, LoggerOptions options)
    //    {
    //        if (options.ChannelCapacity < 10)
    //            return ValidateOptionsResult.Fail("通道容量不能小于10");
    //        return ValidateOptionsResult.Success;
    //    }
    //}
    //只要你把这个类注册到 DI 容器，.NET 在解析 IOptions < LoggerOptions > 时就会自动调用它
    /// </summary>
    public sealed class LoggerOptions
    {
        /// <summary>
        /// 使用Range可以快速校验条件
        /// </summary>
        [Range(10, 10000, ErrorMessage = "通道容量必须在 10 到 10000 之间。")]
        public int ChannelCapacity { get; set; } = 1000;

        [Range(1, 500, ErrorMessage = "批量大小必须在 1 到 500 之间。")]
        public int BatchSize { get; set; } = 50;

        [Range(100, 5000, ErrorMessage = "批量间隔必须在 100ms 到 5000ms 之间。")]
        public double BatchIntervalMilliseconds { get; set; } = 500; // double 用于 TimeSpan 转换

        [Range(1, 5, ErrorMessage = "最大重试次数必须在 1 到 5 之间。")]
        public int MaxRetryAttempts { get; set; } = 3;

        // 将 double 毫秒转换为 TimeSpan 的计算属性（只读）
        public TimeSpan BatchInterval => TimeSpan.FromMilliseconds(BatchIntervalMilliseconds);

        public string LogDirectory { get; set; } = "Logs";

        public string LogFileName { get; set; } = "app.log";

        // 【底层原理】单位 MB。当文件大小超过此值，触发滚动。
        // 工业场景建议 10~50 MB，避免单个文件过大导致加载慢或磁盘碎片。
        [Range(1, 50)]
        public int MaxFileSizeMB { get; set; } = 10;

        // 计算属性：将 MB 转换为字节（long）
        public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;
    }
}
