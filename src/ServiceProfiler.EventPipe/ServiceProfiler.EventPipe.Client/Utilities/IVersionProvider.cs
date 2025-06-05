using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal interface IVersionProvider
    {
        Version RuntimeVersion { get; }
    }
}