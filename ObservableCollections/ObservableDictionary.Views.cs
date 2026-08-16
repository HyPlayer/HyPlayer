using ObservableCollections.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace ObservableCollections
{
    public partial class ObservableDictionary<TKey, TValue>
    {
        public ISynchronizedView<KeyValuePair<TKey, TValue>, TView> CreateView<TView>(Func<KeyValuePair<TKey, TValue>, TView> transform)
        {
            // reverse is no used.
            return new View<TView>(this, transform);
        }

        class View<TView> : ISynchronizedView<KeyValuePair<TKey, TValue>, TView>
        {
            readonly ObservableDictionary<TKey, TValue> source;
            readonly Func<KeyValuePair<TKey, TValue>, TView> selector;
            ISynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView> filter;
            readonly Dictionary<TKey, (TValue, TView)> dict;
            int filteredCount;

            public View(ObservableDictionary<TKey, TValue> source, Func<KeyValuePair<TKey, TValue>, TView> selector)
            {
                this.source = source;
                this.selector = selector;
                this.filter = SynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView>.Null;
                this.SyncRoot = new object();
                lock (source.SyncRoot)
                {
                    this.dict = source.dictionary.ToDictionary(x => x.Key, x => (x.Value, selector(x)));
                    this.filteredCount = dict.Count;
                    this.source.CollectionChanged += SourceCollectionChanged;
                }
            }

            public object SyncRoot { get; }
            public event NotifyViewChangedEventHandler<KeyValuePair<TKey, TValue>, TView>? ViewChanged;
            public event Action<RejectedViewChangedAction, int, int>? RejectedViewChanged;
            public event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

            public ISynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView> Filter
            {
                get { lock (SyncRoot) return filter; }
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

            public void Dispose()
            {
                this.source.CollectionChanged -= SourceCollectionChanged;
            }

            public void AttachFilter(ISynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView> filter)
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
                    foreach (var v in dict)
                    {
                        var value = new KeyValuePair<TKey, TValue>(v.Key, v.Value.Item1);
                        if (filter.IsMatch(value, v.Value.Item2))
                        {
                            filteredCount++;
                        }
                    }

                    ViewChanged?.Invoke(new SynchronizedViewChangedEventArgs<KeyValuePair<TKey, TValue>, TView>(NotifyCollectionChangedAction.Reset, true));
                }
            }

            public void ResetFilter()
            {
                lock (SyncRoot)
                {
                    this.filter = SynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView>.Null;
                    this.filteredCount = dict.Count;
                    ViewChanged?.Invoke(new SynchronizedViewChangedEventArgs<KeyValuePair<TKey, TValue>, TView>(NotifyCollectionChangedAction.Reset, true));
                }
            }

            public ISynchronizedViewList<TView> ToViewList()
            {
                return new FiltableSynchronizedViewList<KeyValuePair<TKey, TValue>, TView>(this, isSupportRangeFeature: true);
            }

            public NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged()
            {
                return new FiltableSynchronizedViewList<KeyValuePair<TKey, TValue>, TView>(this, isSupportRangeFeature: false);
            }

            public NotifyCollectionChangedSynchronizedViewList<TView> ToNotifyCollectionChanged(ICollectionEventDispatcher? collectionEventDispatcher)
            {
                return new FiltableSynchronizedViewList<KeyValuePair<TKey, TValue>, TView>(this, isSupportRangeFeature: false, collectionEventDispatcher);
            }

            public IEnumerator<TView> GetEnumerator()
            {
                lock (SyncRoot)
                {
                    foreach (var item in dict)
                    {
                        var v = (new KeyValuePair<TKey, TValue>(item.Key, item.Value.Item1), item.Value.Item2);
                        if (filter.IsMatch(v))
                        {
                            yield return v.Item2;
                        }
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<(KeyValuePair<TKey, TValue> Value, TView View)> Filtered
            {
                get
                {
                    lock (SyncRoot)
                    {
                        foreach (var item in dict)
                        {
                            var v = (new KeyValuePair<TKey, TValue>(item.Key, item.Value.Item1), item.Value.Item2);
                            if (filter.IsMatch(v))
                            {
                                yield return v;
                            }
                        }
                    }
                }
            }

            public IEnumerable<(KeyValuePair<TKey, TValue> Value, TView View)> Unfiltered
            {
                get
                {
                    lock (SyncRoot)
                    {
                        foreach (var item in dict)
                        {
                            var v = (new KeyValuePair<TKey, TValue>(item.Key, item.Value.Item1), item.Value.Item2);
                            yield return v;
                        }
                    }
                }
            }

            private void SourceCollectionChanged(in NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>> e)
            {
                // ObservableDictionary only provides single item operation and does not use int index.
                lock (SyncRoot)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            {
                                var v = selector(e.NewItem);
                                dict.Add(e.NewItem.Key, (e.NewItem.Value, v));
                                this.InvokeOnAdd(ref filteredCount, ViewChanged, RejectedViewChanged, e.NewItem, v, -1);
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            {
                                if (dict.Remove(e.OldItem.Key, out var v))
                                {
                                    this.InvokeOnRemove(ref filteredCount, ViewChanged, RejectedViewChanged, e.OldItem, v.Item2, -1);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Replace:
                            {
                                var v = selector(e.NewItem);
                                dict.Remove(e.OldItem.Key, out var ov);
                                dict[e.NewItem.Key] = (e.NewItem.Value, v);

                                this.InvokeOnReplace(ref filteredCount, ViewChanged, e.NewItem, v, e.OldItem, ov.Item2, -1);
                            }
                            break;
                        case NotifyCollectionChangedAction.Reset:
                            {
                                dict.Clear();
                                this.InvokeOnReset(ref filteredCount, ViewChanged);
                            }
                            break;
                        case NotifyCollectionChangedAction.Move: // ObservableDictionary have no Move operation.
                        default:
                            break;
                    }

                    CollectionStateChanged?.Invoke(e.Action);
                }
            }
        }
    }
}