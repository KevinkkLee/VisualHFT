﻿using System.Collections;
using System.Collections.ObjectModel;
using VisualHFT.Commons.Model;
using VisualHFT.Commons.Pools;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VisualHFT.Helpers
{
    public class CachedCollection<T> : IDisposable, IEnumerable<T> where T : class, new()
    {
        private readonly object _lock = new object();
        private List<T> _internalList;
        private List<T> _cachedReadOnlyCollection;
        private CachedCollection<T> _takeList;
        private Comparison<T> _comparison;
        
        public CachedCollection(IEnumerable<T> initialData = null)
        {
            _internalList = initialData?.ToList() ?? new List<T>();
        }
        public CachedCollection(Comparison<T> comparison = null, int listSize = 0)
        {
            if (listSize > 0)
                _internalList = new List<T>(listSize);
            else
                _internalList = new List<T>();
            _comparison = comparison;
        }

        public CachedCollection(IEnumerable<T> initialData = null, Comparison<T> comparison = null)
        {
            _internalList = initialData?.ToList() ?? new List<T>();
            _comparison = comparison;
            if (_comparison != null)
            {
                _internalList.Sort(_comparison);
            }
        }

        public void Update(IEnumerable<T> newData)
        {
            lock (_lock)
            {
                _internalList = new List<T>(newData);
                Sort();
                _cachedReadOnlyCollection = null; // Invalidate the cache
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _internalList.Clear();
                _cachedReadOnlyCollection = null; // Invalidate the cache
            }
        }
        public int Count()
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.Count;
                else
                    return _internalList.Count;
            }
        }
        public void Add(T item)
        {
            lock (_lock)
            {
                _internalList.Add(item);
                Sort();
                _cachedReadOnlyCollection = null; // Invalidate the cache
            }
        }
        public bool Remove(T item)
        {
            lock (_lock)
            {
                var result = _internalList.Remove(item);
                if (result)
                {
                    _cachedReadOnlyCollection = null; // Invalidate the cache
                }
                return result;
            }
        }
        public bool RemoveAll(Predicate<T> predicate)
        {
            return Remove(predicate);
        }
        public bool Remove(Predicate<T> predicate)
        {
            lock (_lock)
            {
                bool removed = false;
                for (int i = _internalList.Count - 1; i >= 0; i--)
                {
                    if (predicate(_internalList[i]))
                    {
                        var item = _internalList[i];
                        _internalList.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed)
                {
                    _cachedReadOnlyCollection = null; // Invalidate the cache
                }
                return removed;
            }
        }

        public T FirstOrDefault()
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.FirstOrDefault();
                else
                    return _internalList.FirstOrDefault();
            }
        }
        public T FirstOrDefault(Func<T, bool> predicate)
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.FirstOrDefault(predicate);
                else
                    return _internalList.FirstOrDefault(predicate);
            }
        }
        public CachedCollection<T> Take(int count)
        {
            lock (_lock)
            {
                if (count <= 0)
                {
                    return null;
                }

                if (_takeList == null)
                    _takeList = new CachedCollection<T>(_comparison);

                _takeList.Clear();
                if (_cachedReadOnlyCollection != null)
                {
                    for (int i = 0; i < Math.Min(count, _cachedReadOnlyCollection.Count); i++)
                    {
                        _takeList.Add(_cachedReadOnlyCollection[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < Math.Min(count, _internalList.Count); i++)
                    {
                        _takeList.Add(_internalList[i]);
                    }
                }

                return _takeList;
            }
        }
        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.GetEnumerator();
                else
                    return _internalList.GetEnumerator(); // Create a copy to ensure thread safety during enumeration
            }
        }

        public List<T> ToList()
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.ToList();
                else
                    return _internalList.ToList();
            }
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        private class TakeEnumerable : IEnumerable<T>
        {
            private readonly List<T> _source;
            private readonly int _count;

            public TakeEnumerable(List<T> source, int count)
            {
                _source = source;
                _count = count;
            }

            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < _count && i < _source.Count; i++)
                {
                    yield return _source[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;
            var coll = (obj as CachedCollection<T>);
            if (coll == null)
                return false;

            for (int i = 0; i < coll.Count(); i++)
            {
                if (!_internalList[i].Equals(coll[i]))
                    return false;
            }


            return true;
        }

        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    if (_cachedReadOnlyCollection != null)
                    {
                        return _cachedReadOnlyCollection[index];
                    }
                    else
                    {
                        return _internalList[index];
                    }
                }
            }

        }
        public bool Update(Func<T, bool> predicate, Action<T> actionUpdate)
        {
            lock (_lock)
            {
                T itemFound = _internalList.FirstOrDefault(predicate);
                if (itemFound != null)
                {
                    //execute actionUpdate
                    actionUpdate(itemFound);
                    Sort();
                    InvalidateCache();
                    return true;
                }

                return false;
            }
        }
        public long IndexOf(T element)
        {
            lock (_lock)
            {
                if (_cachedReadOnlyCollection != null)
                    return _cachedReadOnlyCollection.IndexOf(element);
                else
                    return _internalList.IndexOf(element);
            }
        }
        public void InvalidateCache()
        {
            _cachedReadOnlyCollection = null; // Invalidate the cache
        }
        public void Sort()
        {
            lock (_lock)
            {
                if (_comparison != null)
                {
                    _internalList.Sort(_comparison);
                    InvalidateCache();
                }
            }
        }
        public void Dispose()
        {
            _internalList.Clear();
            _cachedReadOnlyCollection?.Clear();
            _takeList.Dispose();
        }

        public void TruncateItemsAfterPosition(int v)
        {
            lock (_lock)
            {
                // If v is negative, clear the entire list.
                if (v < 0)
                {
                    Clear();
                    return;
                }

                // If v is within the bounds of the list, remove items after position v.
                if (v < _internalList.Count - 1)
                {
                    _internalList.RemoveRange(v + 1, _internalList.Count - (v + 1));
                    InvalidateCache();
                }
                // If v is greater than or equal to the last index, nothing to truncate.
            }
        }
    }
}
