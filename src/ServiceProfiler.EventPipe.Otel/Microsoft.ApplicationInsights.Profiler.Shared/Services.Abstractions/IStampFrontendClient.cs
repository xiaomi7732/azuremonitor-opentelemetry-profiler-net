//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

public interface IStampFrontendClient
{
    /// <summary>
    /// The requested feature version in the valid version format, e.g. '1.0.0'. '1' is invalid. In Antares, this from one app setting.
    /// </summary>
    string FeatureVersion { get; }

    /// <summary>
    /// Gets the Stamp ID of the front-end instanced based on location.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InstrumentationKeyInvalidException">The ikey is empty (<see cref="Guid.Empty"/>).</exception>
    /// <returns>The Stamp ID</returns>
    Task<string> GetStampIdAsync(CancellationToken cancellationToken);

    string StampID { get; }

    bool FeatureEnabled { get; }
}
