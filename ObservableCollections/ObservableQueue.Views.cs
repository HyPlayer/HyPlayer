using ObservableCollections.Internal;
using System.Collections;
using System.Collections.Specialized;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ObservableCollections
{
    public partial class ObservableQueue<T> : IReadOnlyCollection<T>, IObservableCollection<T>
    {
        public ISynchronizedView<T, TView> CreateView<TView>(Func<T, TView> transform)
        {
            return new View<TView>(this, transform);
        }

        class View<TView> : ISynchronizedView<T, TView>
        {
            readonly ObservableQueue<T> source;
            readonly Func<T, TView> selector;
            protected readonly Queue<(T, TView)> queue;
            int filteredCount;

            ISynchronizedViewFilter<T, TView> filter;

            public event NotifyViewChangedEventHandler<T, TView>? ViewChanged;
            public event Action<RejectedViewChangedAction, int, int>? RejectedViewChanged;
            public event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

            public object SyncRoot { get; }

            public ISynchronizedViewFilter<T, TView> Filter
            {
                get { lock (SyncRoot) return filter; }
            }

            public View(ObservableQueue<T> source, Func<T, TView> selector)
            {
                this.source = source;
                this.selector = selector;
                this.filter = SynchronizedViewFilter<T, TView>.Null;
                this.SyncRoot = new object();
                lock (source.SyncRoot)
                {
                    this.queue = new Queue<(T, TView)>(source.queue.Select(x => (x, selector(x))));
                    this.filteredCount = queue.Count;
                    this.source.CollectionChanged += SourceCollectionChanged;
                }
            }

            public int Count
            {
                get
                {
                    lock (SyncRoot)
                    {
                        return filteredCount;
                    }
                }
            }

            public int UnfilteredCount
            {
                get
                {
                    lock (SyncRoot)
                    {
                        return queue.Count;
                    }
                }
            }

            public void AttachFilter(ISynchronizedViewFilter<T, TView> filter)
            {
                if (filter.IsNullFilter())
                {
                    ResetFilter();
                    return;
                }

                lock (SyncRoot)
                {
                    this.filter = filter;
                    this.filteredCount = 0;
                    foreach (var (value, view) in queue)
                    {
                        if (filter.IsMatch(value, view))
                        {
                            filteredCount++;
                        }
                    }
                    ViewChanged?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Reset, true));
                }
            }

            public void ResetFilter()
            {
                lock (SyncRoot)
                {
                    this.filter = SynchronizedViewFilter<T, TView>.Null;
                    this.filteredCount = queue.Count;
                    ViewChanged?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Reset, true));
                }
            }

            public ISynchronizedViewList<TView> ToViewList()
            {
                return new FiltableSynchronizedViewList<T, TView>(this, isSupportRangeFeature: true);
            }

            public NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged()
            {
                return new FiltableSynchronizedViewList<T, TView>(this, isSupportRangeFeature: false);
            }

            public NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged(ICollectionEventDispatcher? collectionEventDispatcher)
            {
                return new FiltableSynchronizedViewList<T, TView>(this, isSupportRangeFeature: false, collectionEventDispatcher);
            }

            public IEnumerator<TView> GetEnumerator()
            {
                lock (SyncRoot)
                {
                    foreach (var item in queue)
                    {
                        if (filter.IsMatch(item))
                        {
                            yield return item.Item2;
                        }
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<(T Value, TView View)> Filtered
            {
                get
                {
                    lock (SyncRoot)
                    {
                        foreach (var item in queue)
                        {
                            if (filter.IsMatch(item))
                            {
                                yield return item;
                            }
                        }
                    }
                }
            }

            public IEnumerable<(T Value, TView View)> Unfiltered
            {
                get
                {
                    lock (SyncRoot)
                    {
                        foreach (var item in queue)
                        {
                            yield return item;
                        }
                    }
                }
            }

            public void Dispose()
            {
                this.source.CollectionChanged -= SourceCollectionChanged;
            }

            private void SourceCollectionChanged(in NotifyCollectionChangedEventArgs<T> e)
            {
                lock (SyncRoot)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            // Add(Enqueue, EnqueueRange)
                            if (e.IsSingleItem)
                            {
                                var v = (e.NewItem, selector(e.NewItem));
                                queue.Enqueue(v);
                                this.InvokeOnAdd(ref filteredCount, ViewChanged, RejectedViewChanged, v, e.NewStartingIndex);
                            }
                            else
                            {
                                var i = e.NewStartingIndex;
                                foreach (var item in e.NewItems)
                                {
                                    var v = (item, selector(item));
                                    queue.Enqueue(v);
                                    this.InvokeOnAdd(ref filteredCount, ViewChanged, RejectedViewChanged, v, i++);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            // Dequeue, DequeuRange
                            if (e.IsSingleItem)
                            {
                                var v = queue.Dequeue();
                                this.InvokeOnRemove(ref filteredCount, ViewChanged, RejectedViewChanged, v.Item1, v.Item2, 0);
                            }
                            else
                            {
                                var len = e.OldItems.Length;
                                for (int i = 0; i < len; i++)
                                {
                                    var v = queue.Dequeue();
                                    this.InvokeOnRemove(ref filteredCount, ViewChanged, RejectedViewChanged, v.Item1, v.Item2, 0);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Reset:
                            queue.Clear();
                            this.InvokeOnReset(ref filteredCount, ViewChanged);
                            break;
                        case NotifyCollectionChangedAction.Replace:
                        case NotifyCollectionChangedAction.Move:
                        default:
                            break;
                    }

                    CollectionStateChanged?.Invoke(e.Action);
                }
            }
        }
    }
}