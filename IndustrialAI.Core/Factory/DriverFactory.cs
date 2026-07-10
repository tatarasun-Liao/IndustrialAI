using IndustrialAI.Core.Attributes;
using IndustrialAI.Core.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Factory
{
    public static class DriverFactory
    {
        //类型缓存
        private static readonly ConcurrentDictionary<string, Type> _protocolMap = new();

        static DriverFactory()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                //使用IsAssignableFrom检查type是否实现了IDeviceDriver接口，并且过滤接口与抽象类，避免Activator.CreateInstance()报错
                if (typeof(IDeviceDriver).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
                {
                    var attr = type.GetCustomAttribute<DriverAttribute>();

                    if (attr != null)
                    {
                        _protocolMap.TryAdd(attr.Protocol, type);
                    }
                }
            }

            //Assembly.GetExecutingAssembly().GetTypes().Where(o=> typeof(IDeviceDriver).IsAssignableFrom(o) && o is { IsInterface: false, IsAbstract: false }).Where(a=> a.GetCustomAttribute<DriverAttribute>()!=null).ToList();
        }


        /// <summary>
        /// 根据协议名称创建实例
        /// </summary>
        /// <param name="protocol"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDeviceDriver? Create(string protocol)
        {
            if (string.IsNullOrEmpty(protocol))
                throw new ArgumentException("协议不能为空", nameof(protocol));

            if (_protocolMap.TryGetValue(protocol, out var type))
            {
                // 【底层原理】Activator.CreateInstance 在运行时通过无参构造函数创建实例。
                // 如果类没有显式定义构造函数，编译器会自动生成一个公共无参构造函数。
                return Activator.CreateInstance(type) as IDeviceDriver;
            }

            return null;
        }
    }
}
