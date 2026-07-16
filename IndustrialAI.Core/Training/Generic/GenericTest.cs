using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Training.Generic
{
    public sealed class GenericTest
    {
        public T CreateInstance<T>() where T : class, new()
        {
            T instance = new T();

            return instance;
        }
    }

    public class TestClass_0
    { }

    public interface IProducer<out T> { T Produce(); }
    public interface IConsumer<in T> { void Consume(T item); }

    public class Producer<T> : IProducer<T>
    {
        public T Produce()
        {
            throw new NotImplementedException();
        }
    }

    public class Consumer<T> : IConsumer<T>
    {

        public void Consume(T item)
        {
            throw new NotImplementedException();
        }
    }

    public class Broadcaster
    {
        public event Action? OnEvent;
        public void Trigger() => OnEvent?.Invoke();
    }

    public class Subscriber
    {
        public Subscriber(Broadcaster b) => b.OnEvent += () => Console.WriteLine("Hi");
    }
}
