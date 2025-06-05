//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Microsoft.ApplicationInsights.Profiler.Core.Contracts
{
    /// <summary>
    /// A serialization friendly object model of an event pipe provider
    /// </summary>
    public class EventPipeProviderItem
    {
        /// <summary>
        /// Gets or sets the provider name. For example: Microsoft-Windows-DotNETRuntime.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the event level.
        /// </summary>
        public EventLevel EventLevel { get; set; }

        /// <summary>
        /// Gets or sets the keywords.
        /// </summary>
        public long Keywords { get; set; }

        /// <summary>
        /// Gets or sets the arguments for the event pipe provider.
        /// </summary>
        /// <value></value>
        public IDictionary<string, string> Arguments { get; set; } = null;
    }
}
