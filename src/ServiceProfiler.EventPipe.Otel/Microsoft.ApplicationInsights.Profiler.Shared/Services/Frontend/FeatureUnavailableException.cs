using System;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

public class FeatureUnavailableException : InvalidOperationException
{
    public FeatureUnavailableException(string msg) : base(msg)
    {
        HResult = (int)ExitCode.Exception_FeatureUnavailable;
    }
}
