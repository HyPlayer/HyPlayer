using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace ObservableCollections;

public partial class ObservableList<T> : IList<T>, IReadOnlyObservableList<T>
{
    // override extension methods(IObservableCollection.cs ObservableCollectionExtensions)

    //public ISynchronizedViewList<T> ToViewList<T>(this IObservableCollection<T> collection)
    //{
    //    return ToViewList(collection, static x => x);
    //}

    //public static ISynchronizedViewList<TView> ToViewList<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform)
    //{
    //    return new NonFilteredSynchronizedViewList<T, TView>(collection.CreateView(transform));
    //}

    /// <summary>
    /// Create faster, compact INotifyCollectionChanged view, however it does not support ***Range.
    /// </summary>
    public NotifyCollectionChangedSynchronizedViewList<T> ToNotifyCollectionChangedSlim()
    {
        return new ObservableListSynchronizedViewList<T>(this, null);
    }

    /// <summary>
    /// Create faster, compact INotifyCollectionChanged view, however it does not support ***Range.
    /// </summary>
    public NotifyCollectionChangedSynchronizedViewList<T> ToNotifyCollectionChangedSlim(ICollectionEventDispatcher? collectionEventDispatcher)
    {
        return new ObservableListSynchronizedViewList<T>(this, collectionEventDispatcher);
    }

    //public static INotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform)
    //{
    //    return ToNotifyCollectionChanged(collection, transform, null!);
    //}

    //public static INotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged<T, TView>(this IObservableCollection<T> collection, Func<T, TView> transform, ICollectionEventDispatcher? collectionEventDispatcher)
    //{
    //    return new NonFilteredNotifyCollectionChangedSynchronizedViewList<T, TView>(collection.CreateView(transform), collectionEventDispatcher);
    //}
}

internal sealed partial class ObservableListSynchronizedViewList<T> : NotifyCollectionChangedSynchronizedViewList<T>
{
    static readonly PropertyChangedEventArgs CountPropertyChangedEventArgs = new("Count");
    static readonly Action<NotifyCollectionChangedEventArgs> raiseChangedEventInvoke = RaiseChangedEvent;

    readonly ObservableList<T> parent;
    readonly ICollectionEventDispatcher eventDispatcher;

    public override event NotifyCollectionChangedEventHandler? CollectionChanged;
    public override event PropertyChangedEventHandler? PropertyChanged;

    public ObservableListSynchronizedViewList(ObservableList<T> parent, ICollectionEventDispatcher? eventDispatcher)
    {
        this.parent = parent;
        this.eventDispatcher = eventDispatcher ?? InlineCollectionEventDispatcher.Instance;
        parent.CollectionChanged += Parent_CollectionChanged;
    }

    private void Parent_CollectionChanged(in NotifyCollectionChangedEventArgs<T> args)
    {
        if (CollectionChanged == null && PropertyChanged == null) return;

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (args.IsSingleItem)
                {
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Add, args.NewItem, args.NewStartingIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                }
                else
                {
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Add, args.NewItems.ToArray(), args.NewStartingIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (args.IsSingleItem)
                {
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Remove, args.OldItem, args.OldStartingIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                }
                else
                {
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Remove, args.OldItems.ToArray(), args.OldStartingIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Reset)
                {
                    Collection = this,
                    Invoker = raiseChangedEventInvoke,
                    IsInvokeCollectionChanged = true,
                    IsInvokePropertyChanged = true
                });
                break;
            case NotifyCollectionChangedAction.Replace:
                eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Replace, args.NewItem, args.OldItem, args.NewStartingIndex)
                {
                    Collection = this,
                    Invoker = raiseChangedEventInvoke,
                    IsInvokeCollectionChanged = true,
                    IsInvokePropertyChanged = false
                });
                break;
            case NotifyCollectionChangedAction.Move:
                eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Move, args.NewItem, args.NewStartingIndex, args.OldStartingIndex)
                {
                    Collection = this,
                    Invoker = raiseChangedEventInvoke,
                    IsInvokeCollectionChanged = true,
                    IsInvokePropertyChanged = false
                });
                break;
        }
    }

    static void RaiseChangedEvent(NotifyCollectionChangedEventArgs e)
    {
        var e2 = (CollectionEventDispatcherEventArgs)e;
        var self = (ObservableListSynchronizedViewList<T>)e2.Collection;

        if (e2.IsInvokeCollectionChanged)
        {
            self.CollectionChanged?.Invoke(self, e);
        }
        if (e2.IsInvokePropertyChanged)
        {
            self.PropertyChanged?.Invoke(self, CountPropertyChangedEventArgs);
        }
    }

    public override T this[int index]
    {
        get
        {
            return parent[index];
        }
        set
        {
            parent[index] = value;
        }
    }

    public override int Count => parent.Count;

    public override IEnumerator<T> GetEnumerator()
    {
        return parent.GetEnumerator();
    }

    public override void Dispose()
    {
        parent.CollectionChanged -= Parent_CollectionChanged;
    }

    public override void Add(T item)
    {
        parent.Add(item);
    }
    public override void Insert(int index, T item)
    {
        parent.Insert(index, item);
    }

    public override bool Remove(T item)
    {
        return parent.Remove(item);
    }

    public override void RemoveAt(int index)
    {
        parent.RemoveAt(index);
    }

    public override void Clear()
    {
        parent.Clear();
    }

    public override bool Contains(T item)
    {
        return parent.Contains(item);
    }

    public override int IndexOf(T item)
    {
        return parent.IndexOf(item);
    }
}
