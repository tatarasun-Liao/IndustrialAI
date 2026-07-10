using IndustrialAI.Core.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Cache
{
    public static class GenericCache<T> where T : class
    {
        //定义线程安全字典类型静态资源
        //普通 Dictionary：绝对线程不安全。多个线程同时写入会抛出 IndexOutOfRangeException 或数据损坏，甚至导致死循环。

        //ConcurrentDictionary：允许并发读写。在读取（TryGetValue）时，其他线程可以同时写入，不会阻塞。
        //底层原理是：
        //1.进行分段锁定（Segment Locking），将数据分为多个段，每个段有自己的锁，允许多个线程同时访问不同的段，线程访问A时只锁住A所在的哈希值，而不会阻碍哈希值B的访问
        //2.使用乐观锁（Optimistic Locking），在写入时，先检查数据是否被其他线程修改，如果没有修改则进行写入，否则重试，避免了长时间的锁定
        //3.使用了无锁读取的方式，使用volatile关键字保证内存可见性，使用比较并交换的工作方式在读取时直接读取在内存中已经提交的数据，确保不会被写入操作阻塞
        //核心方法：
        //TryAdd：尝试添加一个键值对，如果键已存在则返回false
        //GetOrAdd：如果键存在则返回对应的值，如果不存在则添加一个新的键值对并返回新值
        //addOrUpdate：如果键存在则更新对应的值，如果不存在则添加一个新的键值对并返回新值

        //ConcurrentDictionary的枚举器会在数据修改的时候在底层生成一份快照，保证枚举器在遍历时不会抛出异常，就算在过程中字典内在被线程操作数据，但是遍历的仍然是开始遍历瞬间的快照数据

        //ConcurrentDictionary 的本质是 “用极小的原子级 CAS 开销换取极高并发吞吐量” 的一种哈希表，通过分段锁定和无锁读取消除并发锁竞争。
        private static readonly ConcurrentDictionary<Type, object> _attributeCache = new();

        static GenericCache()
        {
            var attr = typeof(T).GetCustomAttribute<DriverAttribute>();

            if (attr != null)
            {
                //添加数据时只是用TryAdd这种封装好的原子方法，避免自行加锁导致的线程安全问题
                _attributeCache.TryAdd(typeof(DriverAttribute), attr);//缓存特性
            }

            Console.WriteLine($"[缓存] {typeof(T).Name} 的元数据已加载。");
        }

        /// <summary>
        /// 获取缓存的特性，由于特性必须搭配反射使用，而反射比较消耗性能，所以使用缓存来提高性能。
        /// </summary>
        /// <typeparam name="TAttr"></typeparam>
        /// <returns></returns>
        public static TAttr? GetAttribute<TAttr>() where TAttr : Attribute
        {
            if (_attributeCache.TryGetValue(typeof(TAttr), out var value))
            {

                return value as TAttr;
            }

            return default;
        }
    }
}
