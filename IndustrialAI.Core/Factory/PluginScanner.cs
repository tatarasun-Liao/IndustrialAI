using IndustrialAI.Core.Attributes;
using IndustrialAI.Core.Cache;
using IndustrialAI.Core.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Factory
{
    //用于创建对象，类似使用factory
    public class PluginScanner
    {
        public List<T> ScanAndCreateInstances<T>() where T : class, IDeviceDriver, new()
        {
            var result = new List<T>();

            //获取程序集内所有类型
            var types = Assembly.GetExecutingAssembly().GetTypes();

            foreach (var item in types)
            {
                //判断当前遍历的类型是否实现IDeviceDriver接口 && 不是接口 && 不是抽象类
                if (typeof(IDeviceDriver).IsAssignableFrom(item) && !item.IsInterface && !item.IsAbstract)
                {
                    //获取类型的特性缓存，声明GenericCache<CanDriver>时在内部已经缓存了CanDriver的DriverAttribute特性，调用GetAttribute可以直接获取
                    //此处使用具体类型仅作为泛型缓存调用尝试
                    var attr = GenericCache<CanDriver>.GetAttribute<DriverAttribute>();

                    var driverAttr = item.GetCustomAttribute<DriverAttribute>();

                    if (driverAttr != null)
                    {
                        T instance = new T();//利用了泛型约束new()所以可以直接实例化一个T对象

                        //使用反射创建遍历到的包含DriverAttribute特性的类型对象
                        var driverInstance = Activator.CreateInstance(item) as IDeviceDriver;

                        if (driverInstance != null)
                        {
                            result.Add((T)driverInstance);
                        }
                    }
                }

            }

            return result;
        }

        /// <summary>
        /// 简化版，与上面方法功能相同
        /// </summary>
        /// <returns></returns>
        public List<Type> ScanTypes()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();

            return types.Where(a => typeof(IDeviceDriver).IsAssignableFrom(a) && !a.IsInterface && !a.IsAbstract).Where(o=> o.GetCustomAttribute<DriverAttribute>()!= null).ToList();
        }
    }
}
