using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ObservableCollections
{
    public delegate void NotifyCollectionChangedEventHandler<T>(in NotifyCollectionChangedEventArgs<T> e);
    public delegate void NotifyViewChangedEventHandler<T, TView>(in SynchronizedViewChangedEventArgs<T, TView> e);
    public delegate T WritableViewChangedEventHandler<T, TView>(TView newView, T originalValue, ref bool setValue);

    public interface IObservableCollection<T> : IReadOnlyCollection<T>
    {
        event NotifyCollectionChangedEventHandler<T>? CollectionChanged;
        object SyncRoot { get; }
        ISynchronizedView<T, TView> CreateView<TView>(Func<T, TView> transform);
    }

    public interface IReadOnlyObservableList<T> :
        IReadOnlyList<T>, IObservableCollection<T>
    {
    }

    public interface IReadOnlyObservableDictionary<TKey, TValue> :
        IReadOnlyDictionary<TKey, TValue>, IObservableCollection<KeyValuePair<TKey, TValue>>
    {
    }

    public enum RejectedViewChangedAction
    {
        Add, Remove, Move
    }

    public interface ISynchronizedView<T, TView> : IReadOnlyCollection<TView>, IDisposable
    {
        object SyncRoot { get; }
        ISynchronizedViewFilter<T, TView> Filter { get; }
        IEnumerable<(T Value, TView View)> Filtered { get; }
        IEnumerable<(T Value, TView View)> Unfiltered { get; }
        int UnfilteredCount { get; }

        event NotifyViewChangedEventHandler<T, TView>? ViewChanged;
        event Action<RejectedViewChangedAction, int, int>? RejectedViewChanged;
        event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

        void AttachFilter(ISynchronizedViewFilter<T, TView> filter);
        void ResetFilter();
        ISynchronizedViewList<TView> ToViewList();
        NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged();
        NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged(ICollectionEventDispatcher? collectionEventDispatcher);
    }

    public interface IWritableSynchronizedView<T, TView> : ISynchronizedView<T, TView>
    {
        (T Value, TView View) GetAt(int index);
        void SetViewAt(int index, TView view);
        void SetToSourceCollection(int index, T value);
        void AddToSourceCollection(T value);
        void InsertIntoSourceCollection(int index, T value);
        bool RemoveFromSourceCollection(T value);
        void RemoveAtSourceCollection(int index);
        void ClearSourceCollection();
        IWritableSynchronizedViewList<TView> ToWritableViewList(WritableViewChangedEventHandler<T, TView> converter);
        NotifyCollectionChangedSynchronizedViewList<TView> ToWritableNotifyCollectionChanged();
        NotifyCollectionChangedSynchronizedViewList<TView> ToWritableNotifyCollectionChanged(WritableViewChangedEventHandler<T, TView> converter);
        NotifyCollectionChangedSynchronizedViewList<TView> ToWritableNotifyCollectionChanged(ICollectionEventDispatcher? collectionEventDispatcher);
        NotifyCollectionChangedSynchronizedViewList<TView> ToWritableNotifyCollectionChanged(WritableViewChangedEventHandler<T, TView> converter, ICollectionEventDispatcher? collectionEventDispatcher);
    }

    public interface ISynchronizedViewList<out TView> : IReadOnlyList<TView>, IDisposable
    {
    }

    public interface IWritableSynchronizedViewList<TView> : ISynchronizedViewList<TView>
    {
        new TView this[int index] { get; set; }
    }

    // only for compatibility, use NotifyCollectionChangedSynchronizedViewList insetad.
    // [Obsolete] in future
    public interface INotifyCollectionChangedSynchronizedViewList<TView> : IList<TView>, IList, ISynchronizedViewList<TView>, INotifyCollectionChanged, INotifyPropertyChanged
    {
    }

    // IColleciton<T>.Count and ICollection.Count will be ambigious so use abstract class instead of interface
    // Keep this hierarchy partial: instances are marshalled to XAML through the CsWinRT ABI.
    public abstract partial class NotifyCollectionChangedSynchronizedViewList<TView> :
        INotifyCollectionChangedSynchronizedViewList<TView>,
        IWritableSynchronizedViewList<TView>,
        IList<TView>,
        IList
    {
        protected readonly object gate = new object();

        public abstract TView this[int index] { get; set; }

        object? IList.this[int index]
        {
            get
            {
                return this[index];
            }
            set => ((IList<TView>)this)[index] = (TView)value!;
        }

        public abstract int Count { get; }
        public virtual bool IsReadOnly { get; } = true;
        public bool IsFixedSize => IsReadOnly;
        public bool IsSynchronized => true;
        public object SyncRoot => gate;

        public abstract event NotifyCollectionChangedEventHandler? CollectionChanged;
        public abstract event PropertyChangedEventHandler? PropertyChanged;

        public abstract void Add(TView item);

        int IList.Add(object? value)
        {
            Add((TView)value!);
            return Count - 1;
        }

        public abstract void Insert(int index, TView item);
        public abstract bool Remove(TView item);
        public abstract void RemoveAt(int index);
        public abstract void Clear();

        public abstract bool Contains(TView item);

        bool IList.Contains(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return Contains((TView)value!);
            }
            return false;
        }

        public abstract void Dispose();
        public abstract IEnumerator<TView> GetEnumerator();
        public abstract int IndexOf(TView item);

        int IList.IndexOf(object? item)
        {
            if (IsCompatibleObject(item))
            {
                return IndexOf((TView)item!);
            }
            return -1;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        static bool IsCompatibleObject(object? value)
        {
            return value is TView || value == null && default(TView) == null;
        }

        void ICollection<TView>.Clear()
        {
            Clear();
        }
        void IList.Clear()
        {
            Clear();
        }

        void ICollection<TView>.CopyTo(TView[] array, int arrayIndex) => throw new NotSupportedException();
        void ICollection.CopyTo(Array array, int index) => throw new NotSupportedException();

        void IList<TView>.Insert(int index, TView item)
        {
            Insert(index, item);
        }

        void IList.Insert(int index, object? value)
        {
            Insert(index, (TView)value!);
        }

        bool ICollection<TView>.Remove(TView item)
        {
            return Remove(item!);
        }

        void IList.Remove(object? value)
        {
            Remove((TView)value!);
        }

        void IList.RemoveAt(int index)
        {
            RemoveAt(index);
        }

        void IList<TView>.RemoveAt(int index) => throw new NotSupportedException();
    }

    public static class ObservableCollectionExtensions
    {
        public static ISynchronizedViewList<T> ToViewList<T>(this IObservableCollection<T> collection)
        {
            return ToViewList(collection, static x => x);
        }

        public static ISynchronizedViewList<TView> ToViewList<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform)
        {
            // Optimized for non filtered
            return new NonFilteredSynchronizedViewList<T, TView>(collection.CreateView(transform), isSupportRangeFeature: true, null, null);
        }

        public static NotifyCollectionChangedSynchronizedViewList<T> ToNotifyCollectionChanged<T>(this IObservableCollection<T> collection)
        {
            return ToNotifyCollectionChanged(collection, null);
        }

        public static NotifyCollectionChangedSynchronizedViewList<T> ToNotifyCollectionChanged<T>(this IObservableCollection<T> collection, ICollectionEventDispatcher? collectionEventDispatcher)
        {
            return ToNotifyCollectionChanged(collection, static x => x, collectionEventDispatcher);
        }

        public static NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform)
        {
            return ToNotifyCollectionChanged(collection, transform, null!);
        }

        public static NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform, ICollectionEventDispatcher? collectionEventDispatcher)
        {
            // Optimized for non filtered
            return new NonFilteredSynchronizedViewList<T, TView>(collection.CreateView(transform), isSupportRangeFeature: false, collectionEventDispatcher, null);
        }
    }
}
