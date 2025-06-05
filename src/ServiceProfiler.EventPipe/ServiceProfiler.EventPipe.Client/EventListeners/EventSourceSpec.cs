//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Core.TraceControls
{
    internal class EventSourceSpec : IEquatable<EventSourceSpec>
    {
        public EventSourceSpec() { }
        public EventSourceSpec(string name, Guid providerId, Int64 keyword, uint level)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or empty.", nameof(name));
            }

            Name = name;
            ProviderGuid = providerId;
            Keyword = keyword;
            Level = level;
        }

        public string Name { get; }
        public Guid ProviderGuid { get; }
        public Int64 Keyword { get; }
        public uint Level { get; }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj is EventSourceSpec another)
            {
                return Equals(another);
            }

            return false;
        }

        public bool Equals(EventSourceSpec other)
        {
            if (other == null) { return false; }

            if (string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
                Keyword == other.Keyword &&
                Level == other.Level)
            {
                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode() ^ ProviderGuid.GetHashCode() ^ Keyword.GetHashCode() ^ Level.GetHashCode();
        }

        public static implicit operator EventPipeProvider(EventSourceSpec rhs)
        {
            return new EventPipeProvider(rhs.Name, (EventLevel)rhs.Level, rhs.Keyword, null);
        }
    }
}
