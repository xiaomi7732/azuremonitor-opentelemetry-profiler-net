//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IRemoteSettingsService
{
    SettingsContract CurrentSettings { get; }

    event Action<SettingsContract> SettingsUpdated;

    Task<bool> WaitForInitializedAsync(TimeSpan timeout);
}