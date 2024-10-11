using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Salvavida.DefaultImpl
{
    /// <summary>
    /// This is a slight modified version of Microsoft.Extensions.ObjectPool.DefaultObjectPool
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DefaultObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Func<T> _createFunc;
        private readonly Action<T>? _returnFunc;
        private readonly int _maxCapacity;
        private int _numItems;

        private protected readonly ConcurrentQueue<T> _items = new();
        private protected T? _fastItem;

        /// <summary>
        /// Creates an instance of <see cref="DefaultObjectPool{T}"/>.
        /// </summary>
        /// <param name="policy">The pooling policy to use.</param>
        public DefaultObjectPool(Func<T> createFn, Action<T>? returnFn = null, int maximumRetained = 0)
        {
            _createFunc = createFn;
            _returnFunc = returnFn;
            if (maximumRetained <= 0)
                maximumRetained = Environment.ProcessorCount;
            _maxCapacity = maximumRetained - 1;
        }


        /// <inheritdoc />
        public T Get()
        {
            var item = _fastItem;
            if (item == null || Interlocked.CompareExchange(ref _fastItem, null, item) != item)
            {
                if (_items.TryDequeue(out item))
                {
                    Interlocked.Decrement(ref _numItems);
                    return item;
                }

                // no object available, so go get a brand new one
                return _createFunc();
            }

            return item;
        }

        public IObjectPool<T>.UsingScope Get(out T val)
        {
            val = Get();
            return new IObjectPool<T>.UsingScope(this, val);
        }

        /// <inheritdoc />
        public void Return(T obj)
        {
            _returnFunc?.Invoke(obj);
            if (_fastItem != null || Interlocked.CompareExchange(ref _fastItem, obj, null) != null)
            {
                if (Interlocked.Increment(ref _numItems) <= _maxCapacity)
                {
                    _items.Enqueue(obj);
                    return;
                }

                // no room, clean up the count and drop the object on the floor
                Interlocked.Decrement(ref _numItems);
            }
        }
    }
}
