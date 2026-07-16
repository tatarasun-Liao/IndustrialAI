using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Observability
{
    /// <summary>
    /// 用来记录任务指标，注入到controller中
    /// </summary>
    public class GatewayMetrics
    {
        //创建一个指标计量器
        private readonly Meter _meter;

        // 2. 定义计数器（只增不减）
        private readonly Counter<int> _totalRequestsCounter;//总请求数
        private readonly Counter<long> _totalLogsWrittenCounter;//总写入log数

        public GatewayMetrics(IMeterFactory meterFactory)
        {
            //创建Meter(是否是通过反射在命名空间下创建了实现IMeterFactory的Meter对象？？？)
            _meter = meterFactory.Create("IndustrialAI.Gateway");

            // 初始化计数器
            //CreateCounter第一个参数是什么意思？？？？
            //仅仅是一个命名，用于标识指标，微软建议使用.来分割层级，实际上可以随意命名
            //需要注意，一个Meter内，不可重名
            _totalRequestsCounter = _meter.CreateCounter<int>("gateway.requests.total", description: "API 请求累计总数");
            _totalLogsWrittenCounter = _meter.CreateCounter<long>("gateway.logs.written.total", description: "已写入日志累计总数");
        }

        // 对外暴露的“增加计数”方法，供 Controller 调用
        public void RecordRequest() => _totalRequestsCounter.Add(1);
        public void RecordLogsWritten(int count) => _totalLogsWrittenCounter.Add(count);
    }
}
