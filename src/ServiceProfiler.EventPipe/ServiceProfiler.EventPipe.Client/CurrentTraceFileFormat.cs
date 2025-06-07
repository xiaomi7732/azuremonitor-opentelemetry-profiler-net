using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;

namespace ServiceProfiler.EventPipe.Client;

/// <summary>
/// A class representing the current trace file format definition.
/// </summary>
internal class CurrentTraceFileFormat : ITraceFileFormatDefinition
{
    /// <inheritdoc />
    public string FileExtension => ".netperf";

    /// <inheritdoc />
    public string TraceFileFormatName => TraceFileFormat.Netperf;
}
