using ObservableCollections.Internal;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ObservableCollections
{
    public partial class ObservableHashSet<T> : IReadOnlyCollection<T>, IObservableCollection<T>
    {
        public ISynchronizedView<T, TView> CreateView<TView>(Func<T, TView> transform)
        {
            return new View<TView>(this, transform);
        }

        sealed class View<TView> : ISynchronizedView<T, TView>
        {
            public ISynchronizedViewFilter<T, TView> Filter
            {
                get { lock (SyncRoot) return filter; }
            }

            readonly ObservableHashSet<T> source;
            readonly Func<T, TView> selector;
            readonly Dictionary<T, (T, TView)> dict;
            int filteredCount;

            ISynchronizedViewFilter<T, TView> filter;

            public event NotifyViewChangedEventHandler<T, TView>? ViewChanged;
            public event Action<RejectedViewChangedAction, int, int>? RejectedViewChanged;
            public event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

            public object SyncRoot { get; }

            public View(ObservableHashSet<T> source, Func<T, TView> selector)
            {
                this.source = source;
                this.selector = selector;
                this.filter = SynchronizedViewFilter<T, TView>.Null;
                this.SyncRoot = new object();
                lock (source.SyncRoot)
                {
                    this.dict = source.set.ToDictionary(x => x, x => (x, selector(x)));
                    this.filteredCount = dict.Count;
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
                        return dict.Count;
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
                    foreach (var (_, (value, view)) in dict)
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
                    this.filteredCount = dict.Count;
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
                    foreach (var item in dict)
                    {
                        if (filter.IsMatch(item.Value))
                        {
                            yield return item.Value.Item2;
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
                        foreach (var item in dict)
                        {
                            if (filter.IsMatch(item.Value))
                            {
                                yield return item.Value;
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
                        foreach (var item in dict)
                        {
                            yield return item.Value;
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
                            if (e.IsSingleItem)
                            {
                                var v = (e.NewItem, selector(e.NewItem));
                                dict.Add(e.NewItem, v);
                                this.InvokeOnAdd(ref filteredCount, ViewChanged, RejectedViewChanged, v, -1);
                            }
                            else
                            {
                                var i = e.NewStartingIndex;
                                foreach (var item in e.NewItems)
                                {
                                    var v = (item, selector(item));
                                    dict.Add(item, v);
                                    this.InvokeOnAdd(ref filteredCount, ViewChanged, RejectedViewChanged, v, i++);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.IsSingleItem)
                            {
                                if (dict.Remove(e.OldItem, out var value))
                                {
                                    this.InvokeOnRemove(ref filteredCount, ViewChanged, RejectedViewChanged, value, -1);
                                }
                            }
                            else
                            {
                                foreach (var item in e.OldItems)
                                {
                                    if (dict.Remove(item, out var value))
                                    {
                                        this.InvokeOnRemove(ref filteredCount, ViewChanged, RejectedViewChanged, value, -1);
                                    }
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Reset:
                            dict.Clear();
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
