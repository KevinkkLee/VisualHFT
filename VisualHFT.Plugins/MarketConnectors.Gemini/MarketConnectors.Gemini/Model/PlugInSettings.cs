﻿using System.Collections.Generic;
using VisualHFT.Enums;
using VisualHFT.UserSettings;

namespace MarketConnectors.Gemini.Model
{
    public class PlugInSettings : ISetting
    {

        public required string HostName { get; set; }
        public required string WebSocketHostName { get; set; }
        public required string WebSocketHostName_UserOrder { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; } 
        public List<string> Symbols { get; set; }
        public int DepthLevels { get; set; }
        public string Symbol { get; set; }
        public VisualHFT.Model.Provider Provider { get; set; }
        public AggregationLevel AggregationLevel { get; set; }

    }
}