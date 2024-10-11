using System;

namespace Salvavida
{
    public interface IObjectPool<T> where T : class
    {
        T Get();
        UsingScope Get(out T val);
        void Return(T obj);

        public struct UsingScope : IDisposable
        {
            public UsingScope(IObjectPool<T> pool, T obj)
            {
                if (pool == null)
                    throw new ArgumentNullException(nameof(pool));
                _pool = pool;
                Value = obj;
            }

            private readonly IObjectPool<T> _pool;
            public T? Value { get; private set; }

            public void Dispose()
            {
                if (Value is not null)
                    _pool.Return(Value);
                Value = null;
            }
        }
    }
}
