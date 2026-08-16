using System;

namespace ObservableCollections
{
    // Obsolete...
    [Obsolete("this interface is obsoleted. Use ISynchronizedViewFilter<T, TView> instead.")]
    public interface ISynchronizedViewFilter<T>
    {
        bool IsMatch(T value);
    }

    public interface ISynchronizedViewFilter<T, TView>
    {
        bool IsMatch(T value, TView view);
    }

    internal class SynchronizedViewValueOnlyFilter<T, TView>(Func<T, bool> isMatch) : ISynchronizedViewFilter<T, TView>
    {
        public bool IsMatch(T value, TView view) => isMatch(value);

        class NullViewFilter : ISynchronizedViewFilter<T, TView>
        {
            public bool IsMatch(T value, TView view) => true;
        }
    }

    public class SynchronizedViewFilter<T, TView>(Func<T, TView, bool> isMatch) : ISynchronizedViewFilter<T, TView>
    {
        public static readonly ISynchronizedViewFilter<T, TView> Null = new NullViewFilter();

        public bool IsMatch(T value, TView view) => isMatch(value, view);

        class NullViewFilter : ISynchronizedViewFilter<T, TView>
        {
            public bool IsMatch(T value, TView view) => true;
        }
    }
}
