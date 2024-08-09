using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

public static class OpenTelemetryBuilderExtesnions
{
  public static IOpenTelemetryBuilder UseServiceProfiler(this OpenTelemetryBuilder builder, Action<ServiceProfilerOptions> configureServiceProfiler)
    => UseServiceProfiler((IOpenTelemetryBuilder)builder, configureServiceProfiler);

  public static IOpenTelemetryBuilder UseServiceProfiler(this IOpenTelemetryBuilder builder, Action<ServiceProfilerOptions> configureServiceProfiler)
  {
    builder.Services.AddHostedService<ProfilerBackgroundService>();
    return builder;
  }
}


