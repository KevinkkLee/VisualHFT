﻿using VisualHFT.Enums;
using VisualHFT.Model;
using VisualHFT.UserSettings;

namespace VisualHFT.Studies.MarketResilience.Model
{
    public class PlugInSettings : ISetting
    {
        public string Symbol { get; set; }
        public Provider Provider { get; set; }
        public AggregationLevel AggregationLevel { get; set; }
        public int? MinShockTimeDifference { get; set; }
        public int? SpreadShockThresholdMultiplier { get; set; }
        public int? TradeSizeShockThresholdMultiplier { get; set; }
    }
}