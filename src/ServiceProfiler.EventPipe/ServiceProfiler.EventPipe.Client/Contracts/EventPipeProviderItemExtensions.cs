//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.ApplicationInsights.Profiler.Core.Contracts
{
    /// <summary>
    /// A serialization friendly object model of an event pipe provider
    /// </summary>
    internal static class EventPipeProviderItemExtensions
    {
        public static EventPipeProvider ToProvider(this EventPipeProviderItem item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return new EventPipeProvider(item.Name, item.EventLevel, item.Keywords, item.Arguments);
        }
    }
}
