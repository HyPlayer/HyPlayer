#pragma warning disable CS9124

using System;
using System.Collections.Specialized;
using System.Data;
using System.Runtime.CompilerServices;

namespace ObservableCollections
{
    public readonly ref struct SynchronizedViewChangedEventArgs<T, TView>(
        NotifyCollectionChangedAction action,
        bool isSingleItem,
        (T Value, TView View) newItem = default!,
        (T Value, TView View) oldItem = default!,
        ReadOnlySpan<T> newValues = default!,
        ReadOnlySpan<TView> newViews = default!,
        ReadOnlySpan<T> oldValues = default!,
        ReadOnlySpan<TView> oldViews = default!,
        int newStartingIndex = -1,
        int oldStartingIndex = -1,
        SortOperation<T> sortOperation = default)
    {
        public readonly NotifyCollectionChangedAction Action = action;
        public readonly bool IsSingleItem = isSingleItem;
        public readonly (T Value, TView View) NewItem = newItem;
        public readonly (T Value, TView View) OldItem = oldItem;
        public readonly ReadOnlySpan<T> NewValues = newValues;
        public readonly ReadOnlySpan<TView> NewViews = newViews;
        public readonly ReadOnlySpan<T> OldValues = oldValues;
        public readonly ReadOnlySpan<TView> OldViews = oldViews;
        public readonly int NewStartingIndex = newStartingIndex;
        public readonly int OldStartingIndex = oldStartingIndex;
        public readonly SortOperation<T> SortOperation = sortOperation;

        public SynchronizedViewChangedEventArgs<T, TView> WithNewStartingIndex(int newStartingIndex)
        {
            // MEMO: struct copy and replace only newStartingIndex memory maybe fast.
            return new SynchronizedViewChangedEventArgs<T, TView>(
                action,
                IsSingleItem,
                newItem: NewItem,
                oldItem: OldItem,
                newValues: NewValues,
                newViews: NewViews,
                oldValues: OldValues,
                oldViews: OldViews,
                newStartingIndex: newStartingIndex, // replace
                oldStartingIndex: OldStartingIndex,
                sortOperation: SortOperation);
        }

        public SynchronizedViewChangedEventArgs<T, TView> WithOldStartingIndex(int oldStartingIndex)
        {
            return new SynchronizedViewChangedEventArgs<T, TView>(
                action,
                IsSingleItem,
                newItem: NewItem,
                oldItem: OldItem,
                newValues: NewValues,
                newViews: NewViews,
                oldValues: OldValues,
                oldViews: OldViews,
                newStartingIndex: NewStartingIndex,
                oldStartingIndex: oldStartingIndex, // replace
                sortOperation: SortOperation);
        }

        public SynchronizedViewChangedEventArgs<T, TView> WithNewAndOldStartingIndex(int newStartingIndex, int oldStartingIndex)
        {
            return new SynchronizedViewChangedEventArgs<T, TView>(
                action,
                IsSingleItem,
                newItem: NewItem,
                oldItem: OldItem,
                newValues: NewValues,
                newViews: NewViews,
                oldValues: OldValues,
                oldViews: OldViews,
                newStartingIndex: newStartingIndex, // replace
                oldStartingIndex: oldStartingIndex, // replace
                sortOperation: SortOperation);
        }
    }

    public static class SynchronizedViewExtensions
    {
        public static void AttachFilter<T, TView>(this ISynchronizedView<T, TView> source, Func<T, bool> filter)
        {
            source.AttachFilter(new SynchronizedViewValueOnlyFilter<T, TView>(filter));
        }

        public static void AttachFilter<T, TView>(this ISynchronizedView<T, TView> source, Func<T, TView, bool> filter)
        {
            source.AttachFilter(new SynchronizedViewFilter<T, TView>(filter));
        }

        public static bool IsNullFilter<T, TView>(this ISynchronizedViewFilter<T, TView> filter)
        {
            return filter == SynchronizedViewFilter<T, TView>.Null;
        }

        internal static bool IsMatch<T, TView>(this ISynchronizedViewFilter<T, TView> filter, (T, TView) item)
        {
            return filter.IsMatch(item.Item1, item.Item2);
        }

        internal static void InvokeOnAdd<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, (T value, TView view) value, int index)
        {
            InvokeOnAdd(collection, ref filteredCount, ev, ev2, value.value, value.view, index);
        }

        internal static void InvokeOnAdd<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, T value, TView view, int index)
        {
            var isMatch = collection.Filter.IsMatch(value, view);
            if (isMatch)
            {
                filteredCount++;
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Add, true, newItem: (value, view), newStartingIndex: index));
            }
            else
            {
                ev2?.Invoke(RejectedViewChangedAction.Add, index, -1);
            }
        }

        internal static void InvokeOnAddRange<T, TView>(this ISynchronizedView<T, TView> collection, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, ReadOnlySpan<T> values, ReadOnlySpan<TView> views, bool isMatchAll, ReadOnlySpan<bool> matches, int index)
        {
            if (isMatchAll)
            {
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Add, isSingleItem: false, newValues: values, newViews: views, newStartingIndex: index));
            }
            else
            {
                for (var i = 0; i < matches.Length; i++)
                {
                    if (matches[i])
                    {
                        var item = (values[i], views[i]);
                        ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Add, isSingleItem: true, newItem: item, newStartingIndex: index));
                    }
                    else
                    {
                        ev2?.Invoke(RejectedViewChangedAction.Add, index, -1);
                    }
                    index++;
                }
            }
        }

        internal static void InvokeOnRemove<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, (T value, TView view) value, int oldIndex)
        {
            InvokeOnRemove(collection, ref filteredCount, ev, ev2, value.value, value.view, oldIndex);
        }

        internal static void InvokeOnRemove<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, T value, TView view, int oldIndex)
        {
            var isMatch = collection.Filter.IsMatch(value, view);
            if (isMatch)
            {
                filteredCount--;
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Remove, true, oldItem: (value, view), oldStartingIndex: oldIndex));
            }
            else
            {
                ev2?.Invoke(RejectedViewChangedAction.Remove, oldIndex, -1);
            }
        }

        // only use for ObservableList
        internal static void InvokeOnRemoveRange<T, TView>(this ISynchronizedView<T, TView> collection, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, ReadOnlySpan<T> values, ReadOnlySpan<TView> views, bool isMatchAll, ReadOnlySpan<bool> matches, int index)
        {
            if (isMatchAll)
            {
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Remove, isSingleItem: false, oldValues: values, oldViews: views, oldStartingIndex: index));
            }
            else
            {
                for (var i = 0; i < matches.Length; i++)
                {
                    if (matches[i])
                    {
                        var item = (values[i], views[i]);
                        ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Remove, isSingleItem: true, oldItem: item, oldStartingIndex: index)); //remove for list, always same index
                    }
                    else
                    {
                        ev2?.Invoke(RejectedViewChangedAction.Remove, index, -1);
                    }
                }
            }
        }

        internal static void InvokeOnMove<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, (T value, TView view) value, int index, int oldIndex)
        {
            InvokeOnMove(collection, ref filteredCount, ev, ev2, value.value, value.view, index, oldIndex);
        }

        internal static void InvokeOnMove<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, Action<RejectedViewChangedAction, int, int>? ev2, T value, TView view, int index, int oldIndex)
        {
            // move does not changes filtered-count
            var isMatch = collection.Filter.IsMatch(value, view);
            if (isMatch)
            {
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Move, true, newItem: (value, view), newStartingIndex: index, oldStartingIndex: oldIndex));
            }
            else
            {
                ev2?.Invoke(RejectedViewChangedAction.Move, index, oldIndex);
            }
        }

        internal static void InvokeOnReplace<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, (T value, TView view) value, (T value, TView view) oldValue, int index, int oldIndex = -1)
        {
            InvokeOnReplace(collection, ref filteredCount, ev, value.value, value.view, oldValue.value, oldValue.view, index, oldIndex);
        }

        internal static void InvokeOnReplace<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev, T value, TView view, T oldValue, TView oldView, int index, int oldIndex = -1)
        {
            var oldMatched = collection.Filter.IsMatch(oldValue, oldView);
            var newMatched = collection.Filter.IsMatch(value, view);
            var bothMatched = oldMatched && newMatched;

            if (bothMatched)
            {
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Replace, true, newItem: (value, view), oldItem: (oldValue, oldView), newStartingIndex: index, oldStartingIndex: oldIndex >= 0 ? oldIndex : index));
            }
            else if (oldMatched)
            {
                // only-old is remove
                filteredCount--;
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Remove, true, oldItem: (value, view), oldStartingIndex: oldIndex));
            }
            else if (newMatched)
            {
                // only-new is add
                filteredCount++;
                ev?.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Add, true, newItem: (value, view), newStartingIndex: index));
            }
        }

        internal static void InvokeOnReset<T, TView>(this ISynchronizedView<T, TView> collection, ref int filteredCount, NotifyViewChangedEventHandler<T, TView>? ev)
        {
            filteredCount = 0;
            if (ev != null)
            {
                ev.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Reset, true));
            }
        }

        internal static void InvokeOnReverseOrSort<T, TView>(this ISynchronizedView<T, TView> collection, NotifyViewChangedEventHandler<T, TView>? ev, SortOperation<T> sortOperation)
        {
            if (ev != null)
            {
                ev.Invoke(new SynchronizedViewChangedEventArgs<T, TView>(NotifyCollectionChangedAction.Reset, true, sortOperation: sortOperation));
            }
        }
    }
}
