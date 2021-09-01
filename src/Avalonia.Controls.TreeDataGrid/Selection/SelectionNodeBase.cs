﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia.Controls.Utils;

#nullable enable

namespace Avalonia.Controls.Selection
{
    /// <summary>
    /// Base class for selection models.
    /// </summary>
    /// <typeparam name="T">The type of the element being selected.</typeparam>
    public abstract class SelectionNodeBase<T> : ICollectionChangedListener
    {
        private IEnumerable? _source;
        private bool _rangesEnabled;
        private List<IndexRange>? _ranges;
        private int _collectionChanging;

        /// <summary>
        /// Gets or sets the source collection.
        /// </summary>
        protected IEnumerable? Source
        {
            get => _source;
            set
            {
                if (_source != value)
                {
                    ItemsView?.RemoveListener(this);
                    _source = value;
                    ItemsView = value is object ? ItemsSourceViewFix<T>.GetOrCreate(value) : null;
                    ItemsView?.AddListener(this);
                }
            }
        }

        /// <summary>
        /// Gets an <see cref="ItemsSourceView{T}"/> of the <see cref="Source"/>.
        /// </summary>
        protected internal ItemsSourceViewFix<T>? ItemsView { get; set; }

        /// <summary>
        /// Gets a value indicating whether the <see cref="Source"/> collection is currently
        /// changing.
        /// </summary>
        protected bool IsSourceCollectionChanging => _collectionChanging > 0;

        /// <summary>
        /// Gets or sets a value indicating whether range selection is currently enabled for
        /// the selection node.
        /// </summary>
        protected bool RangesEnabled
        {
            get => _rangesEnabled;
            set
            {
                if (_rangesEnabled != value)
                {
                    _rangesEnabled = value;

                    if (!_rangesEnabled)
                    {
                        _ranges = null;
                    }
                }
            }
        }

        // TODO: Work out a way to expose this without exposing IndexRange.
        internal IReadOnlyList<IndexRange> Ranges
        {
            get
            {
                if (!RangesEnabled)
                {
                    throw new InvalidOperationException("Ranges not enabled.");
                }

                return _ranges ??= new List<IndexRange>();
            }
        }

        void ICollectionChangedListener.PreChanged(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
        {
            ++_collectionChanging;
        }

        void ICollectionChangedListener.Changed(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
        {
            OnSourceCollectionChanged(e);
        }

        void ICollectionChangedListener.PostChanged(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
        {
            if (--_collectionChanging == 0)
            {
                OnSourceCollectionChangeFinished();
            }
        }

        /// <summary>
        /// Called when the <see cref="Source"/> collection changes.
        /// </summary>
        /// <param name="e">The details of the collection change.</param>
        /// <remarks>
        /// The implementation in <see cref="SelectionNodeBase{T}"/> calls
        /// <see cref="OnItemsAdded(int, IList)"/> and <see cref="OnItemsRemoved(int, IList)"/>
        /// in order to calculate how the collection change affects the currently selected items.
        /// It then calls <see cref="OnIndexesChanged(int, int)"/> and
        /// <see cref="OnSelectionRemoved(int, int, IReadOnlyList{T})"/> if necessary, according
        /// to the <see cref="CollectionChangeState"/> returned by those methods.
        /// 
        /// Override this method and <see cref="OnSourceCollectionChangeFinished"/> to provide
        /// custom handling of source collection changes.
        /// </remarks>
        protected virtual void OnSourceCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            var shiftDelta = 0;
            var shiftIndex = -1;
            List<T?>? removed = null;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        var change = OnItemsAdded(e.NewStartingIndex, e.NewItems);
                        shiftIndex = change.ShiftIndex;
                        shiftDelta = change.ShiftDelta;
                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        var change = OnItemsRemoved(e.OldStartingIndex, e.OldItems);
                        shiftIndex = change.ShiftIndex;
                        shiftDelta = change.ShiftDelta;
                        removed = change.RemovedItems;
                        break;
                    }
                case NotifyCollectionChangedAction.Replace:
                    {
                        var removeChange = OnItemsRemoved(e.OldStartingIndex, e.OldItems);
                        var addChange = OnItemsAdded(e.NewStartingIndex, e.NewItems);
                        shiftIndex = removeChange.ShiftIndex;
                        shiftDelta = removeChange.ShiftDelta + addChange.ShiftDelta;
                        removed = removeChange.RemovedItems;
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    OnSourceReset();
                    break;
            }

            if (shiftDelta != 0)
                OnIndexesChanged(shiftIndex, shiftDelta);
            if (removed is object)
                OnSelectionRemoved(shiftIndex, -shiftDelta, removed);
        }

        /// <summary>
        /// Called when the source collection has finished changing, and all CollectionChanged
        /// handlers have run.
        /// </summary>
        /// <remarks>
        /// Override this method to respond to the end of a collection change instead of acting at
        /// the end of <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>
        /// in order to ensure that all UI subscribers to the source collection change event have
        /// had chance to run.
        /// </remarks>
        protected virtual void OnSourceCollectionChangeFinished()
        {
        }

        /// <summary>
        /// Called by <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>,
        /// detailing the indexes changed by the collection changing.
        /// </summary>
        /// <param name="shiftIndex">The first index that was shifted.</param>
        /// <param name="shiftDelta">
        /// If positive, the number of items inserted, or if negative the number of items removed.
        /// </param>
        protected virtual void OnIndexesChanged(int shiftIndex, int shiftDelta)
        {
        }

        /// <summary>
        /// Called by <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>,
        /// on collection reset.
        /// </summary>
        protected abstract void OnSourceReset();

        /// <summary>
        /// Called by <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>,
        /// detailing the items removed by a collection change.
        /// </summary>
        protected virtual void OnSelectionRemoved(int index, int count, IReadOnlyList<T?> deselectedItems)
        {
        }

        /// <summary>
        /// If <see cref="RangesEnabled"/>, adds the specified range to the selection.
        /// </summary>
        /// <param name="begin">The inclusive index of the start of the range to select.</param>
        /// <param name="end">The inclusive index of the end of the range to select.</param>
        /// <returns>The number of items selected.</returns>
        protected int CommitSelect(int begin, int end)
        {
            if (RangesEnabled)
            {
                _ranges ??= new List<IndexRange>();
                return IndexRange.Add(_ranges, new IndexRange(begin, end));
            }

            return 0;
        }

        /// <summary>
        /// If <see cref="RangesEnabled"/>, removes the specified range from the selection.
        /// </summary>
        /// <param name="begin">The inclusive index of the start of the range to deselect.</param>
        /// <param name="end">The inclusive index of the end of the range to deselect.</param>
        /// <returns>The number of items selected.</returns>
        protected int CommitDeselect(int begin, int end)
        {
            if (RangesEnabled)
            {
                _ranges ??= new List<IndexRange>();
                return IndexRange.Remove(_ranges, new IndexRange(begin, end));
            }

            return 0;
        }

        /// <summary>
        /// Called by <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>
        /// when items are added to the source collection.
        /// </summary>
        /// <returns>
        /// A <see cref="CollectionChangeState"/> struct containing the details of the adjusted
        /// selection.
        /// </returns>
        /// <remarks>
        /// The implementation in <see cref="SelectionNodeBase{T}"/> adjusts the selected ranges, 
        /// assigning new indexes. Override this method to carry out additional computation when
        /// items are added.
        /// </remarks>
        protected virtual CollectionChangeState OnItemsAdded(int index, IList items)
        {
            var count = items.Count;
            var shifted = false;

            if (_ranges is object)
            {
                List<IndexRange>? toAdd = null;

                for (var i = 0; i < Ranges!.Count; ++i)
                {
                    var range = Ranges[i];

                    // The range is after the inserted items, need to shift the range right
                    if (range.End >= index)
                    {
                        int begin = range.Begin;

                        // If the index left of newIndex is inside the range,
                        // Split the range and remember the left piece to add later
                        if (range.Contains(index - 1))
                        {
                            range.Split(index - 1, out var before, out _);
                            (toAdd ??= new List<IndexRange>()).Add(before);
                            begin = index;
                        }

                        // Shift the range to the right
                        _ranges[i] = new IndexRange(begin + count, range.End + count);
                        shifted = true;
                    }
                }

                if (toAdd is object)
                {
                    foreach (var range in toAdd)
                    {
                        IndexRange.Add(_ranges, range);
                    }
                }
            }

            return new CollectionChangeState
            {
                ShiftIndex = index,
                ShiftDelta = shifted ? count : 0,
            };
        }

        /// <summary>
        /// Called by <see cref="OnSourceCollectionChanged(NotifyCollectionChangedEventArgs)"/>
        /// when items are removed from the source collection.
        /// </summary>
        /// <returns>
        /// A <see cref="CollectionChangeState"/> struct containing the details of the adjusted
        /// selection.
        /// </returns>
        /// <remarks>
        /// The implementation in <see cref="SelectionNodeBase{T}"/> adjusts the selected ranges, 
        /// assigning new indexes. Override this method to carry out additional computation when
        /// items are removed.
        /// </remarks>
        private protected virtual CollectionChangeState OnItemsRemoved(int index, IList items)
        {
            var count = items.Count;
            var removedRange = new IndexRange(index, index + count - 1);
            bool shifted = false;
            List<T?>? removed = null;

            if (_ranges is object)
            {
                var deselected = new List<IndexRange>();

                if (IndexRange.Remove(_ranges, removedRange, deselected) > 0)
                {
                    removed = new List<T?>();

                    foreach (var range in deselected)
                    {
                        for (var i = range.Begin; i <= range.End; ++i)
                        {
                            removed.Add((T?)items[i - index]);
                        }
                    }
                }

                for (var i = 0; i < Ranges!.Count; ++i)
                {
                    var existing = Ranges[i];

                    if (existing.End > removedRange.Begin)
                    {
                        _ranges[i] = new IndexRange(existing.Begin - count, existing.End - count);
                        shifted = true;
                    }
                }
            }

            return new CollectionChangeState
            {
                ShiftIndex = index,
                ShiftDelta = shifted ? -count : 0,
                RemovedItems = removed,
            };
        }

        /// <summary>
        /// Details the results of a collection change on the current selection;
        /// </summary>
        protected class CollectionChangeState
        {
            /// <summary>
            /// Gets or sets the first index that was shifted as a result of the collection
            /// changing.
            /// </summary>
            public int ShiftIndex { get; set; }

            /// <summary>
            /// Gets or sets a value indicating how the indexes after <see cref="ShiftIndex"/>
            /// were shifted.
            /// </summary>
            public int ShiftDelta { get; set; }

            /// <summary>
            /// Gets or sets the items removed by the collection change, if any.
            /// </summary>
            public List<T?>? RemovedItems { get; set; }
        }
    }
}
