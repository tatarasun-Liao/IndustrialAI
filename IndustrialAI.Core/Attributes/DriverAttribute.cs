using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Attributes
{
    /// <summary>
    /// 约束特性适用范围：不可继承类型,不允许重复标记
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false,AllowMultiple = false)]
    public class DriverAttribute : Attribute
    {
        public string Protocol { get; }
        public string Station { get; set; } = "Unknown";

        //.net8 高级写法，只有一行的函数可以使用表达式=>
        public DriverAttribute(string protocol) => Protocol = protocol;
    }
}
