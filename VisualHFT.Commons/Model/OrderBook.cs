﻿using VisualHFT.Commons.Model;
using VisualHFT.Commons.Pools;
using VisualHFT.Helpers;
using VisualHFT.Studies;
using VisualHFT.Enums;
using VisualHFT.Model;
using System.Linq;

namespace VisualHFT.Model
{
    public partial class OrderBook : ICloneable, IResettable, IDisposable
    {
        private bool _disposed = false; // to track whether the object has been disposed
        private OrderFlowAnalysis lobMetrics = new OrderFlowAnalysis();

        protected OrderBookData _data;
        protected static readonly log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        protected CustomObjectPool<BookItem> _poolBookItems
            = new CustomObjectPool<BookItem>(2000);

        // Add counters for level changes
        private long _addedLevels = 0;
        private long _deletedLevels = 0;
        private long _updatedLevels = 0;
        // Properties to expose counters
        public long AddedLevels => _addedLevels;
        public long DeletedLevels => _deletedLevels;
        public long UpdatedLevels => _updatedLevels;

        public OrderBook()
        {
            _data = new OrderBookData();
            FilterBidAskByMaxDepth = true;
        }

        public OrderBook(string symbol, int priceDecimalPlaces, int maxDepth)
        {
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth), "maxDepth must be greater than zero.");
            _data = new OrderBookData(symbol, priceDecimalPlaces, maxDepth);
            FilterBidAskByMaxDepth = true;
        }

        ~OrderBook()
        {
            Dispose(false);
        }

        public string Symbol
        {
            get => _data.Symbol;
            set => _data.Symbol = value;
        }

        public int MaxDepth
        {
            get => _data.MaxDepth;
            set => _data.MaxDepth = value;
        }

        public int PriceDecimalPlaces
        {
            get => _data.PriceDecimalPlaces;
            set => _data.PriceDecimalPlaces = value;
        }

        public int SizeDecimalPlaces
        {
            get => _data.SizeDecimalPlaces;
            set => _data.SizeDecimalPlaces = value;
        }

        public double SymbolMultiplier => _data.SymbolMultiplier;

        public int ProviderID
        {
            get => _data.ProviderID;
            set => _data.ProviderID = value;
        }

        public string ProviderName
        {
            get => _data.ProviderName;
            set => _data.ProviderName = value;
        }

        public eSESSIONSTATUS ProviderStatus
        {
            get => _data.ProviderStatus;
            set => _data.ProviderStatus = value;
        }

        public double MaximumCummulativeSize
        {
            get => _data.MaximumCummulativeSize;
            set => _data.MaximumCummulativeSize = value;
        }

        public CachedCollection<BookItem> Asks
        {
            get
            {
                lock (_data.Lock)
                {
                    if (_data.Asks == null)
                        return null;
                    if (MaxDepth > 0 && FilterBidAskByMaxDepth)
                        return _data.Asks.Take(MaxDepth);
                    else
                        return _data.Asks;
                }
            }
            set => _data.Asks.Update(value); //do not remove setter: it is used to auto parse json
        }

        public CachedCollection<BookItem> Bids
        {
            get
            {
                lock (_data.Lock)
                {
                    if (_data.Bids == null)
                        return null;
                    if (MaxDepth >0 && FilterBidAskByMaxDepth)
                        return _data.Bids.Take(MaxDepth);
                    else
                        return _data.Bids;
                }
            }
            set => _data.Bids.Update(value); //do not remove setter: it is used to auto parse json
        }

        public BookItem GetTOB(bool isBid)
        {
            lock (_data.Lock)
            {
                return _data.GetTOB(isBid);
            }
        }

        public double MidPrice
        {
            get
            {
                return _data.MidPrice;
            }
        }
        public double Spread
        {
            get
            {
                return _data.Spread;
            }
        }
        public bool FilterBidAskByMaxDepth
        {
            get
            {
                return _data.FilterBidAskByMaxDepth;
            }
            set
            {
                _data.FilterBidAskByMaxDepth = value;
            }
        }
        public void GetAddDeleteUpdate(ref CachedCollection<BookItem> inputExisting, bool matchAgainsBids)
        {
            if (inputExisting == null)
                return;
            lock (_data.Lock)
            {
                IEnumerable<BookItem> listToMatch = (matchAgainsBids ? _data.Bids : _data.Asks);
                if (listToMatch.Count() == 0)
                    return;

                if (inputExisting.Count() == 0)
                {
                    foreach (var item in listToMatch)
                    {
                        inputExisting.Add(item);
                    }

                    return;
                }

                IEnumerable<BookItem> inputNew = listToMatch;
                List<BookItem> outAdds;
                List<BookItem> outUpdates;
                List<BookItem> outRemoves;

                var existingSet = inputExisting;
                var newSet = inputNew;

                outRemoves = inputExisting.Where(e => !newSet.Contains(e)).ToList();
                outUpdates = inputNew.Where(e =>
                    existingSet.Contains(e) && e.Size != existingSet.FirstOrDefault(i => i.Equals(e)).Size).ToList();
                outAdds = inputNew.Where(e => !existingSet.Contains(e)).ToList();

                foreach (var b in outRemoves)
                    inputExisting.Remove(b);
                foreach (var b in outUpdates)
                {
                    var itemToUpd = inputExisting.Where(x => x.Price == b.Price).FirstOrDefault();
                    if (itemToUpd != null)
                    {
                        itemToUpd.Size = b.Size;
                        itemToUpd.ActiveSize = b.ActiveSize;
                        itemToUpd.CummulativeSize = b.CummulativeSize;
                        itemToUpd.LocalTimeStamp = b.LocalTimeStamp;
                        itemToUpd.ServerTimeStamp = b.ServerTimeStamp;
                    }
                }

                foreach (var b in outAdds)
                    inputExisting.Add(b);
            }
        }

        public void CalculateMetrics()
        {
            lock (_data.Lock)
            {
                lobMetrics.LoadData(_data.Asks, _data.Bids, MaxDepth);
            }
            _data.ImbalanceValue = lobMetrics.Calculate_OrderImbalance();
        }
        public bool LoadData(IEnumerable<BookItem> asks, IEnumerable<BookItem> bids)
        {
            bool ret = true;
            lock (_data.Lock)
            {
                #region Bids
                if (bids != null)
                {
                    _data.Bids.Update(bids
                        .Where(x => x != null && x.Price.HasValue && x.Size.HasValue)
                        .OrderByDescending(x => x.Price.Value)
                    );
                }
                #endregion
                #region Asks
                if (asks != null)
                {
                    _data.Asks.Update(asks
                        .Where(x => x != null && x.Price.HasValue && x.Size.HasValue)
                        .OrderBy(x => x.Price.Value)
                    );
                }
                #endregion
                _data.CalculateAccummulated();
            }
            CalculateMetrics();

            return ret;
        }

        public double GetMaxOrderSize()
        {
            double _maxOrderSize = 0;

            lock (_data.Lock)
            {
                if (_data.Bids != null)
                    _maxOrderSize = _data.Bids.Where(x => x.Size.HasValue).DefaultIfEmpty(new BookItem()).Max(x => x.Size.Value);
                if (_data.Asks != null)
                    _maxOrderSize = Math.Max(_maxOrderSize, _data.Asks.Where(x => x.Size.HasValue).DefaultIfEmpty(new BookItem()).Max(x => x.Size.Value));
            }
            return _maxOrderSize;
        }

        public Tuple<double, double> GetMinMaxSizes()
        {
            lock (_data.Lock)
            {
                return _data.GetMinMaxSizes();
            }
        }

        public virtual object Clone()
        {
            var clone = new OrderBook(_data.Symbol, _data.PriceDecimalPlaces, _data.MaxDepth);
            clone.ProviderID = _data.ProviderID;
            clone.ProviderName = _data.ProviderName;
            clone.SizeDecimalPlaces = _data.SizeDecimalPlaces;
            clone._data.ImbalanceValue = _data.ImbalanceValue;
            clone.ProviderStatus = _data.ProviderStatus;
            clone.MaxDepth = _data.MaxDepth;
            clone.LoadData(Asks, Bids);
            return clone;
        }

        public void PrintLOB(bool isBid)
        {
            lock (_data.Lock)
            {
                int _level = 0;
                foreach (var item in isBid ? _data.Bids : _data.Asks)
                {
                    Console.WriteLine($"{_level} - {item.FormattedPrice} [{item.Size}]");
                    _level++;
                }
            }
        }

        public double ImbalanceValue
        {
            get => _data.ImbalanceValue;
            set => _data.ImbalanceValue = value;
        }
        public long Sequence { get; set; }

        private void InternalClear()
        {
            for (int i = 0; i < _data.Asks.Count();)
            {
                var ask = _data.Asks[i];
                if (ask.Price != 0)
                {
                    DeleteLevel(new DeltaBookItem() { IsBid = false, Price = ask.Price });
                }
                else
                {
                    i++;
                }
            }

            for (int i = 0; i < _data.Bids.Count();)
            {
                var bid = _data.Bids[i];
                if (bid.Price != 0)
                {
                    DeleteLevel(new DeltaBookItem() { IsBid = true, Price = bid.Price });
                }
                else
                {
                    i++;
                }
            }

            GetAndResetChangeCounts();
        }


        public void Clear()
        {
            lock (_data.Lock)
            {
                InternalClear();
                _data.Clear();
            }
        }
        public void Reset()
        {
            lock (_data.Lock)
            {
                InternalClear();
                _data?.Reset();
            }

        }



        public virtual void AddOrUpdateLevel(DeltaBookItem item)
        {
            if (!item.IsBid.HasValue)
                return;
            eMDUpdateAction eAction = eMDUpdateAction.None;

            lock (_data.Lock)
            {
                var _list = (item.IsBid.HasValue && item.IsBid.Value ? _data.Bids : _data.Asks);
                var itemFound = _list.FirstOrDefault(x => x.Price == item.Price);
                if (itemFound == null)
                    eAction = eMDUpdateAction.New;
                else
                    eAction = eMDUpdateAction.Change;
            }

            if (eAction == eMDUpdateAction.Change)
                UpdateLevel(item);
            else
                AddLevel(item);

        }
        public virtual void AddLevel(DeltaBookItem item)
        {
            if (!item.IsBid.HasValue)
                return;

            lock (_data.Lock)
            {
                // Check if it is appropriate to add a new item to the Limit Order Book (LOB). 
                // If the item exceeds the depth scope defined by MaxDepth, it should not be added.
                // If the item is within the acceptable depth, truncate the LOB to ensure it adheres to the MaxDepth limit.
                bool willNewItemFallOut = false;

                
                var list = item.IsBid.Value ? _data.Bids : _data.Asks;
                var listCount = list.Count();
                if (item.IsBid.Value)
                {
                    willNewItemFallOut = listCount > this.MaxDepth && item.Price < list.Min(x => x.Price);
                }
                else
                {
                    willNewItemFallOut = listCount > this.MaxDepth && item.Price > list.Max(x => x.Price);
                }

                if (!willNewItemFallOut)
                {
                    var _level = _poolBookItems.Get();
                    _level.EntryID = item.EntryID;
                    _level.Price = item.Price;
                    _level.IsBid = item.IsBid.Value;
                    _level.LocalTimeStamp = item.LocalTimeStamp;
                    _level.ProviderID = _data.ProviderID;
                    _level.ServerTimeStamp = item.ServerTimeStamp;
                    _level.Size = item.Size;
                    _level.Symbol = _data.Symbol;
                    _level.PriceDecimalPlaces = this.PriceDecimalPlaces;
                    _level.SizeDecimalPlaces = this.SizeDecimalPlaces;
                    list.Add(_level);
                    listCount++;
                    Interlocked.Increment(ref _addedLevels);
                    //truncate last item if we exceeded the MaxDepth
                    if (listCount > this.MaxDepth)
                    {
                        _poolBookItems.Return(list.TakeLast(listCount - this.MaxDepth));
                        list.TruncateItemsAfterPosition(MaxDepth-1);
                    }
                }

            }
        }
        public virtual void UpdateLevel(DeltaBookItem item)
        {
            lock (_data.Lock)
            {
                (item.IsBid.HasValue && item.IsBid.Value ? _data.Bids : _data.Asks).Update(x => x.Price == item.Price,
                    existingItem =>
                    {
                        if (existingItem.Size > item.Size) //added
                            Interlocked.Increment(ref _addedLevels);
                        else if (existingItem.Size < item.Size) //deleted
                            Interlocked.Increment(ref _deletedLevels);

                        existingItem.Price = item.Price;
                        existingItem.Size = item.Size;
                        existingItem.LocalTimeStamp = item.LocalTimeStamp;
                        existingItem.ServerTimeStamp = item.ServerTimeStamp;
                    });
            }

        }
        public virtual void DeleteLevel(DeltaBookItem item)
        {
            if (string.IsNullOrEmpty(item.EntryID) && (!item.Price.HasValue || item.Price.Value == 0))
                throw new Exception("DeltaBookItem cannot be deleted since has no price or no EntryID.");
            lock (_data.Lock)
            {
                BookItem _itemToDelete = null;


                if (!string.IsNullOrEmpty(item.EntryID))
                {
                    _itemToDelete = (item.IsBid.HasValue && item.IsBid.Value ? _data.Bids : _data.Asks)
                        .FirstOrDefault(x => x.EntryID == item.EntryID);
                }
                else if (item.Price.HasValue && item.Price > 0)
                {
                    _itemToDelete = (item.IsBid.HasValue && item.IsBid.Value ? _data.Bids : _data.Asks)
                        .FirstOrDefault(x => x.Price == item.Price);
                }


                if (_itemToDelete != null)
                {
                    (item.IsBid.HasValue && item.IsBid.Value ? _data.Bids : _data.Asks).Remove(_itemToDelete);
                    _poolBookItems.Return(_itemToDelete);
                    Interlocked.Increment(ref _deletedLevels);
                }
            }
        }
        public (long added, long deleted, long updated) GetAndResetChangeCounts()
        {
            var result = (_addedLevels, _deletedLevels, _updatedLevels);
            _addedLevels = 0;
            _deletedLevels = 0;
            _updatedLevels = 0;
            return result;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _data?.Dispose();
                }
                _disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}

