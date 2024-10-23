using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.ApplicationInsights.Profiler.Shared;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByEnvironmentVariable>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorInUserCache>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByUnzipping>();
        services.TryAddTransient<IUploaderPathProvider, UploaderPathProvider>();

        services.AddTransient<OutOfProcCaller>();
        services.AddSingleton<IOutOfProcCallerFactory, OutOfProcCallerFactory>();

        return services;
    }
}