// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.Core;

namespace Azure.Monitor.OpenTelemetry.Profiler.OOPHost;

public class ServiceProfilerOOPHostOptions: ServiceProfilerOptions
{
    /// <summary>
    /// Gets or sets the target process name
    /// </summary>
    public required string TargetProcessName { get; set; }
}