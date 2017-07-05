﻿using MvvmCross.Platform.WeakSubscription;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.ComponentModel;

namespace CollectionViews.NETStandard
{
    public class CollectionView : ICollectionView
    {
        //-----------------------------------------------------
        #region Fields
        private MvxNotifyCollectionChangedEventSubscription _subscription;
        private FastObservableCollection<GroupDescription> _groupDescriptions;
        private FastObservableCollection<SortDescription> _sortDescriptions;
        private static readonly string HeaderNameForNullItems = "Nulls";
        private FastObservableCollection<IGroupData> _groups;
        private IList _sourceCollection;
        private List<object> _internalSource;
        private Predicate<object> _filter;
        private int _count;
        #endregion
        //-----------------------------------------------------
        #region Construction
        public CollectionView(IList sourceCollection)
        {
            _sourceCollection = sourceCollection ?? throw new ArgumentNullException(nameof(sourceCollection));
            _groups = new FastObservableCollection<IGroupData>();
            Groups = new ReadOnlyObservableCollection<IGroupData>(_groups);
            _groupDescriptions = new FastObservableCollection<GroupDescription>();
            GroupDescriptions.CollectionChanged += _groupDescriptions_CollectionChanged;
            _sortDescriptions = new FastObservableCollection<SortDescription>();
            SortDescriptions.CollectionChanged += _sortDescriptions_CollectionChanged;
            var newObservable = _sourceCollection as INotifyCollectionChanged;
            if (newObservable != null)
            {
                _subscription = newObservable.WeakSubscribe(OnSourceCollection_CollectionChanged);
            }
            Init();
        }
        #endregion
        //-----------------------------------------------------
        #region Properties & Methods
        public object this[int index] { get => GetObjectValueAt(index); set => throw new NotSupportedException("The collection is read only."); }

        public ReadOnlyObservableCollection<IGroupData> Groups { get; private set; }

        public Predicate<object> Filter
        {
            get => _filter;
            set
            {
                if (_filter != value)
                {
                    _filter = value; OnFilterChanged();
                }
            }
        }

        public bool IsFixedSize => false;

        public bool IsReadOnly => true;

        public int Count => _count;

        public bool IsSynchronized => false;

        public object SyncRoot => null;

        public FastObservableCollection<GroupDescription> GroupDescriptions { get => _groupDescriptions; }
        public FastObservableCollection<SortDescription> SortDescriptions { get => _sortDescriptions; }

        public bool CanGroup => true;

        public bool CanSort => true;

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public int Add(object value)
        {
            throw new NotSupportedException("The collection is read only.");
        }

        public void Clear()
        {
            throw new NotSupportedException("The collection is read only.");
        }

        public bool Contains(object value)
        {
            foreach (GroupData group in _groups)
            {
                foreach (var item in group)
                {
                    if (value.Equals(item))
                        return true;
                }
            }
            return false;
        }

        public void CopyTo(Array array, int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "'index' can not be a negative value.");
            if (Count > array.Length - index - 1)
                throw new ArgumentOutOfRangeException(nameof(index), "The number of elements in the source collection is greater than the available space from index to the end of the destination array.");
            for (int i = 0; i < Count; i++)
            {
                array.SetValue(GetObjectValueAt(i), index++);
            }
        }

        public IEnumerator GetEnumerator()
        {
            foreach (GroupData group in _groups)
            {
                foreach (var item in group)
                {
                    yield return item;
                }
            }
            //int currentIndex = 0;
            //Tuple<int, int> groupPairIndex = ConvertToGroupIndex(currentIndex);
            //while (groupPairIndex.Item1 >= 0)
            //{
            //    yield return (_groups[groupPairIndex.Item1] as GroupData).GetObjectAt(groupPairIndex.Item2);
            //    currentIndex++;
            //    groupPairIndex = ConvertToGroupIndex(currentIndex);
            //}
        }

        public int IndexOf(object value)
        {
            return GetIndexInCollectionView(value);
        }

        public void Insert(int index, object value)
        {
            throw new NotSupportedException("The collection is read only.");
        }

        public void Refresh()
        {
            Init();
            NotifyCollectionReset();
        }

        public void Remove(object value)
        {
            throw new NotSupportedException("The collection is read only.");
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException("The collection is read only.");
        }

        #endregion
        //-----------------------------------------------------
        #region Private Methods
        private void Init()
        {
            CreateInternalSource();
            SortCollection(_internalSource);
            IEnumerable<IGroupData> groupedData = GroupCollectionItems(_internalSource);
            _groups.ReplaceRange(groupedData);
            UpdateCount();
        }

        private void _sortDescriptions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_sortDescriptions.Count == 0)
            {
                Init(); // We need to recreate this collectionView in order to revert back to sorting of the source collection.
            }
            else
            {
                SortCollection(_internalSource);//Updates internal source collection
                foreach (var group in _groups)
                {
                    ((GroupData)group).SortItems(SourceItemsComparison);
                }
            }
            NotifyCollectionReset();
        }

        private void _groupDescriptions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            IEnumerable<IGroupData> groupedData = GroupCollectionItems(_internalSource);
            _groups.ReplaceRange(groupedData);
            UpdateCount();
            NotifyCollectionReset();
        }

        private void OnSourceCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncViewBySourceChanges(e);
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        protected virtual void SortCollection(IEnumerable<object> collection)
        {
            if (_sortDescriptions.Count > 0)
            {
                if (collection is List<object>)
                    ((List<object>)collection).Sort(SourceItemsComparison);
                else if (collection is object[])
                {
                    Array.Sort((object[])collection, SourceItemsComparison);
                }
                else
                {
                    collection.OrderBy(i => i, new SourceItemsComparer(_sortDescriptions));
                }
            }

        }

        protected virtual int SourceItemsComparison(object object1, object object2)
        {
            int result = 0;
            int lvl = 0;
            if (_sortDescriptions.Count == 0)
                return result;
            while (lvl < _sortDescriptions.Count)
            {
                SortDescription currentSort = _sortDescriptions[lvl];
                int smaller = currentSort.Direction == ListSortDirection.Ascending ? -1 : 1;
                int greater = -smaller;
                if (object1 == null && object2 == null)
                {
                    lvl++;
                    continue;//this sort is match, continue to the next sort criteria
                }
                else if (object1 == null)
                    return smaller;
                else if (object2 == null)
                    return greater;


                if (!string.IsNullOrEmpty(currentSort.PropertyName))
                {
                    object val1 = object1.GetType().GetRuntimeProperty(currentSort.PropertyName)?.GetValue(object1);
                    object val2 = object2.GetType().GetRuntimeProperty(currentSort.PropertyName)?.GetValue(object2);
                    if (val1 == null && val2 == null)
                    {
                        lvl++;
                        continue;//this sort is match, continue to the next sort criteria
                    }
                    else if (val1 == null)
                        return smaller;
                    else if (val2 == null)
                        return greater;
                    IComparable x = (IComparable)val1;//items must be IComparable
                    result = x.CompareTo(val2) * greater;
                    if (result != 0)
                        return result;
                    lvl++;
                }
                else
                {
                    IComparable x = (IComparable)object1;//items must be IComparable
                    result = x.CompareTo(object2) * greater;
                    if (result != 0)
                        return result;
                    lvl++;
                }
            }
            return result;
        }

        protected virtual IEnumerable<IGroupData> GroupCollectionItems(IEnumerable<object> collection)
        {
            List<GroupData> groups = new List<GroupData>();
            foreach (var item in collection)
            {
                object[] groupNames = GetItemGroupNames(item);
                GroupData alreadyCreated = FindMatchingGroup(groups, groupNames) as GroupData;
                if (alreadyCreated != null)
                {
                    alreadyCreated.AddItemToGroup(item, 1, groupNames.Skip(1));
                }
                else
                {
                    object header = groupNames.Length > 0 ? groupNames[0] ?? HeaderNameForNullItems : null;
                    GroupData group = new GroupData(header);
                    group.AddItemToGroup(item, 1, groupNames.Skip(1));
                    groups.Add(group);
                }
            }
            var sorted = SortGroupHeaders(groups);

            return sorted;
        }

        internal IEnumerable<IGroupData> SortGroupHeaders(List<GroupData> groups)
        {
            if (_groupDescriptions.Count == 0)
                return groups;
            GroupDescription currentGroup = _groupDescriptions[0];
            IComparer<object> comparer = currentGroup.GroupHeaderComparer;
            if (comparer == null)
                return groups;

            var sorted = groups.OrderBy(g => g.Header, comparer);

            //_groups.ReplaceRange(sorted);

            foreach (GroupData group in sorted)
            {
                group.SortHeaders(_groupDescriptions);
            }
            return sorted;
        }

        protected virtual object[] GetItemGroupNames(object item)
        {
            object[] names = new object[_groupDescriptions.Count];
            for (int i = 0; i < _groupDescriptions.Count; i++)
            {
                names[i] = _groupDescriptions[i].GroupNameFromItem(item, i, System.Globalization.CultureInfo.CurrentCulture);
            }
            return names;
        }

        protected virtual IGroupData FindMatchingGroup(IEnumerable<IGroupData> groups, object[] groupNames)
        {
            var alreadyCreatedGroup = groups.FirstOrDefault(g =>
            {
                if (groupNames.Length == 0)
                {
                    return true;
                }
                else
                    return g.Header.Equals(groupNames[0]);
            }
            );
            return alreadyCreatedGroup;
        }

        protected virtual void OnFilterChanged()
        {
            Init();
            NotifyOfPropertyChange(nameof(Filter));
            NotifyCollectionReset();
        }

        protected virtual void CreateInternalSource()
        {
            _internalSource = GetFilteredItems(_sourceCollection);
        }

        #region Index mapping
        /// <summary>
        /// Returns the group which contains an item corresponding to the index of this collectionView or null if the index in out of bound.
        /// </summary>
        private GroupData GetGroupFromIndex(int index)
        {
            foreach (GroupData group in _groups)
            {
                if (index <= group.LastIndex)
                    return group;
                index -= group.Count;
            }
            return null;
        }
        /// <summary>
        /// Converts an index of this collectionView to the position in the groups
        /// </summary>
        private Tuple<int, int> ConvertToGroupIndex(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "'index' can not be a negative value.");
            int groupIndex = 0;
            foreach (GroupData group in _groups)
            {
                if (index <= group.LastIndex)
                    return new Tuple<int, int>(groupIndex, index);
                index -= group.Count;
                groupIndex++;
                if (index < 0 || groupIndex >= _groups.Count)
                    break;
            }
            return new Tuple<int, int>(-1, -1);
        }

        private int GetIndexInCollectionView(object value)
        {
            int index = -1;
            foreach (GroupData group in _groups)
            {
                foreach (var item in group)
                {
                    index++;
                    if (value.Equals(item))
                        return index;
                }
            }
            return -1;
        }

        private int GetIndexInCollectionView(IGroupData existingGroup, int indexInGroup)
        {
            if (existingGroup == null)
                throw new ArgumentNullException(nameof(existingGroup));
            if (indexInGroup < 0)
                throw new ArgumentException("indexInGroup can not be a negative value.");
            int indexInCollectionView = 0;
            int groupIndex = _groups.IndexOf(existingGroup);
            if (groupIndex < 0)
                throw new ArgumentException($"the given IGroupData does not exists in the {nameof(Groups)} collection.");
            for (int i = 0; i < groupIndex; i++)
            {
                indexInCollectionView += _groups[i].Count;
            }
            indexInCollectionView += indexInGroup;
            return indexInCollectionView;
        }

        private int GetIndexInCollectionView(IGroupData existingGroup, object itemInGroup)
        {
            if (existingGroup == null)
                throw new ArgumentNullException(nameof(existingGroup));
            if (itemInGroup == null)
                throw new ArgumentNullException(nameof(itemInGroup));
            int indexInGroup = ((GroupData)existingGroup).IndexOf(itemInGroup);
            if (indexInGroup < 0)
                throw new InvalidOperationException("The given item does not exists in the given IGroupData");
            return GetIndexInCollectionView(existingGroup, indexInGroup);
        }
        #endregion

        #region Source Changes Synchronization
        protected virtual void SyncViewBySourceChanges(NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove: //can affect grouping of items if groups become empty
                    RemoveItems(e.OldStartingIndex, e.OldItems);
                    break;
                case NotifyCollectionChangedAction.Replace: //can affect both sorting and grouping of items based on the new values
                    ReplaceItems(e.OldItems, e.NewItems, e.OldStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Add: //can affect both sorting and grouping of items based on the item values
                    AddItems(e.NewItems, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Move: //only affects position of the items in the collectionView if currently no sorting is applied
                    MoveItems(e.OldItems ?? e.NewItems, e.OldStartingIndex, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Reset:
                default:
                    Init();
                    NotifyCollectionReset();
                    break;
            }
        }

        private void RemoveItems(int oldStartingIndex, IList oldItems)
        {
            if (_sortDescriptions.Count > 0 || _groupDescriptions.Count > 0 || Filter != null)
            {
                //Most likely ordering of items in this collectionView changed according to the source collection. Hence we remove items individually
                foreach (var item in oldItems)
                {
                    RemoveObject(item);
                }
            }
            else
            {
                //In this case, this collectionView mimics the source collection
                RemoveObjects(oldStartingIndex, oldItems);
            }
        }

        private void RemoveObject(object toBeRemoved)
        {
            if (Filter != null && !Filter(toBeRemoved)) //if this item is not in the collectionview
                return;
            int index = -1;
            List<GroupData> emptiedGroups = null;
            foreach (GroupData group in _groups)
            {
                index = group.IndexOf(toBeRemoved);
                if (index >= 0)
                {//the item is part of this group
                    emptiedGroups = group.RemoveObjectAt(index);
                    _internalSource.Remove(toBeRemoved); //updating the internal source
                    index = GetIndexInCollectionView(group, index); //updating this index so it represents index of removed object in this collectionView
                    break;
                }
            }
            if (emptiedGroups != null && emptiedGroups.Count > 0)
            {//we emptiedGroups the empty group from the collectionView
                _groups.Remove(emptiedGroups.Last());//the emptiedGroup is not necessarily direct child of the _groups collection
                UpdateCount();
                NotifyOfPropertyChange("Item[]");
                try
                {
                    //We have to report removed indexes in the descending order, so the views can adjust their items properly.
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, toBeRemoved, index));
                    foreach (var emptiedGroup in emptiedGroups)
                    {
                        if (emptiedGroup.Header != null)//if the removed group had header
                        {   //header was the item before the removed object
                            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, emptiedGroup.Header, --index));
                        }

                    }
                }
                catch (InvalidOperationException ex)
                {
                    //https://stackoverflow.com/questions/37698854/system-invalidoperationexception-n-index-in-collection-change-event-is-not-val
                    //It seems that in WPF ListCollectionView checks that the indexes provided in the eventArgs belong to the collection; if not, the exception in the subject is thrown.
                    //Since we might have already removed this indexes from the collectionView and collectionChanged event is calling asynchronously in the WPF, this in inevitable.
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
                return;
            }
            if (index >= 0) // since item was in the collection viwe this alwasy must be true
            {
                UpdateCount();
                NotifyOfPropertyChange("Item[]");
                try
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, toBeRemoved, index));
                }
                catch (InvalidOperationException ex)
                {
                    //same as above
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }
        //this method only will be called when this collectionView is completely filled like the source collection
        private void RemoveObjects(int oldStartingIndex, IList removedItems)
        {
            if (oldStartingIndex >= 0)//check the source collection to ensure that it raised the 'CollectionChanged' event properly
            {
                ((GroupData)_groups[0]).RemoveRangeAt(oldStartingIndex, removedItems.Count);
                _internalSource.RemoveRange(oldStartingIndex, removedItems.Count);
                NotifyOfPropertyChange("Item[]");
                UpdateCount();
                try
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems, oldStartingIndex));
                }
                catch (NotSupportedException ex)
                {
                    //WPF listView does not support range actions :
                    //for (int i = removedItems.Count - 1; i >= 0; i--)
                    //{
                    //    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems[i], oldStartingIndex + i));
                    //}
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
            else
            {
                foreach (var item in removedItems)
                {
                    int index = _internalSource.IndexOf(item);
                    ((GroupData)_groups[0]).RemoveObjectAt(index);
                    _internalSource.Remove(item);
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                }
                NotifyOfPropertyChange("Item[]");
                UpdateCount();
            }
        }

        private void ReplaceItems(IList oldItems, IList newItems, int startIndex)
        {
            if (oldItems.Count == newItems.Count)
            {
                if (_sortDescriptions.Count > 0 || _groupDescriptions.Count > 0)
                {
                    Init();
                    NotifyCollectionReset();
                }
                else if (Filter != null || startIndex < 0) //if 
                {
                    List<object> replaced = new List<object>();
                    List<object> newValues = new List<object>();
                    for (int i = 0; i < oldItems.Count; i++)
                    {
                        int index = _internalSource.IndexOf(oldItems[i]);
                        if (index >= 0)// if items is not filtered and exists in the internal source
                        {
                            replaced.Add(oldItems[i]);
                            newValues.Add(newItems[i]);
                            _internalSource[index] = newItems[i];
                            ((GroupData)_groups[0]).ReplaceAt(index, newItems[i]);
                        }
                    }
                    if (replaced.Count > 0)
                    {
                        NotifyOfPropertyChange("Item[]");
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newValues, replaced));
                    }
                }
                else
                {
                    //this collection should match the source collection
                    int index = startIndex;
                    for (int i = 0; i < oldItems.Count; i++)
                    {
                        _internalSource[index] = newItems[i];
                        ((GroupData)_groups[0]).ReplaceAt(index, newItems[i]);
                        index++;
                    }
                    NotifyOfPropertyChange("Item[]");
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItems, oldItems, startIndex));
                }
            }
            else
            {
                //It can not be a valid replace action for collectionchanged event; We need to recreate the collectionview
                Init();
                NotifyCollectionReset();
            }
        }

        private void AddItems(IList newItems, int newStartingIndex)
        {
            if (newItems == null)
                throw new ArgumentNullException(nameof(newItems));
            if (newItems.Count == 0)
                return;

            if (_sortDescriptions.Count > 0 || _groupDescriptions.Count > 0)
            {
                if (_groupDescriptions.Count == 0)
                {
                    //only sorting is applied
                    List<object> filtered = GetFilteredItems(newItems);
                    foreach (var item in filtered)
                    {
                        int insertAt = GetInsertIndex(_internalSource, item);
                        _internalSource.Insert(insertAt, item);
                        ((GroupData)_groups[0]).Insert(insertAt, item);
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, insertAt));
                    }
                    UpdateCount();
                    NotifyOfPropertyChange("Item[]");
                }
                else
                {
                    Init();
                    NotifyCollectionReset();
                }
            }
            else
            {
                newStartingIndex = newStartingIndex < 0 ? _internalSource.Count : newStartingIndex;
                List<object> filtered = GetFilteredItems(newItems);
                _internalSource.InsertRange(newStartingIndex, filtered);
                ((GroupData)_groups[0]).InsertRange(newStartingIndex, filtered);
                UpdateCount();
                NotifyOfPropertyChange("Item[]");
                try
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, filtered, newStartingIndex));
                }
                catch (NotSupportedException ex)
                {
                    //WPF listView does not support range actions :
                    for (int i = 0; i < newItems.Count; i++)
                    {
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems[i], newStartingIndex + i));
                    }
                    //OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }

        private void MoveItems(IList items, int oldIndex, int newIndex)
        {
            if (_sortDescriptions.Count == 0)
            {
                var filtered = GetFilteredItems(items);
                int startIndex = oldIndex;
                int endIndex = newIndex;
                foreach (var item in filtered)
                {
                    _internalSource.RemoveAt(startIndex);
                    _internalSource.Insert(endIndex, item);
                    IGroupData group = GetGroupFromItem(item);
                    if (endIndex > startIndex)
                    {
                        for (int i = endIndex - 1; i >= startIndex; i--) //check all affected items in the internal source
                        {
                            if (group.Items.Contains(_internalSource[i])) //if corresponding group contains the affected item
                            {
                                int collectionViewOldIndex = GetIndexInCollectionView(item);
                                int groupOldIndex = group.Items.IndexOf(item);
                                int groupNewIndex = group.Items.IndexOf(_internalSource[i]);
                                int collectionViewNewIndex = collectionViewOldIndex + groupNewIndex - groupOldIndex;
                                ((GroupData)group).Move(groupOldIndex, groupNewIndex);
                                NotifyOfPropertyChange("Item[]");
                                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, collectionViewNewIndex, collectionViewOldIndex));
                                break;
                            }
                        }
                    }
                    else if (endIndex < startIndex)
                    {
                        for (int i = endIndex + 1; i <= startIndex; i++)
                        {
                            if (group.Items.Contains(_internalSource[i]))
                            {
                                int collectionViewOldIndex = GetIndexInCollectionView(item);
                                int groupOldIndex = group.Items.IndexOf(item);
                                int groupNewIndex = group.Items.IndexOf(_internalSource[i]);
                                int collectionViewNewIndex = collectionViewOldIndex + groupNewIndex - groupOldIndex;
                                ((GroupData)group).Move(groupOldIndex, groupNewIndex);
                                NotifyOfPropertyChange("Item[]");
                                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, collectionViewNewIndex, collectionViewOldIndex));
                                break;
                            }
                        }
                    }
                    startIndex++;
                    endIndex++;
                }
            }
        }
        #endregion
        /// <summary>
        /// Returns the object at the given index in the collectionView
        /// </summary>
        /// <param name="index">index of item in the collectionCiew</param>
        /// <returns></returns>
        private object GetObjectValueAt(int index)
        {
            Tuple<int, int> groupPairIndex = ConvertToGroupIndex(index);
            if (groupPairIndex.Item1 >= 0)
                return (_groups[groupPairIndex.Item1] as GroupData).GetObjectAt(groupPairIndex.Item2);
            else
                throw new IndexOutOfRangeException($"The given {nameof(index)} value is out of range of valid indexes for this collection.");
        }
        /// <summary>
        /// Returns the index that item should be placed in the collection based on the sorting criterias 
        /// </summary>
        private int GetInsertIndex(IList<object> items, object newItem)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (SourceItemsComparison(newItem, items[i]) <= 0)
                    return i;
            }
            return items.Count;
        }
        /// <summary>
        /// Returns count of the items in the collectionview
        /// </summary>
        /// <returns></returns>
        private int GetCount()
        {
            return _groups.Sum(g => g.Count);
        }
        private void UpdateCount()
        {
            _count = GetCount();
            NotifyOfPropertyChange(nameof(Count));
        }
        private List<object> GetFilteredItems(IEnumerable items)
        {
            List<object> filtered = new List<object>();
            if (Filter != null)
                foreach (var item in items)
                {
                    if (Filter(item))
                        filtered.Add(item);
                }
            else
                foreach (var item in items)
                    filtered.Add(item);
            return filtered;
        }
        /// <summary>
        /// Returns the group which its Items collection contains the given item
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private IGroupData GetGroupFromItem(object item)
        {
            foreach (GroupData group in _groups)
            {
                GroupData parent = group.GetParentGroup(item);
                if (parent != null)
                    return parent;
            }
            return null;
        }

        private void NotifyCollectionReset()
        {
            NotifyOfPropertyChange("Item[]");
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Notifies subscribers of the property change.
        /// </summary>
        /// <param name = "propertyName">Name of the property.</param>
        public virtual void NotifyOfPropertyChange([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }
        #endregion
        //-----------------------------------------------------

    }
}