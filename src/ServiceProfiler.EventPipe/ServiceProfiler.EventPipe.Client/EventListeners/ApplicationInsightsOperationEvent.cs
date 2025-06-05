//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Text.Json.Serialization;

namespace Microsoft.ApplicationInsights.Profiler.Core.EventListeners
{
    internal class ApplicationInsightsOperationEvent : EventObjectBase
    {
        [JsonIgnore]
        public string RequestId { get; set; }
        [JsonIgnore]
        public string OperationId { get; set; }
        [JsonIgnore]
        public string OperationName { get; set; }
        [JsonPropertyName("TimeStamp")]
        public DateTimeOffset TimeStamp { get; set; }
    }
}
