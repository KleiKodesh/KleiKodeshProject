using System;

namespace WpfLib.Helpers
{
    public class TEventArgs<T> : EventArgs
    {
        public T Value { get; private set; }

        public TEventArgs(T val)
        {
            Value = val;
        }
    }
}
