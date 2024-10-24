using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.IPC;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Microsoft.ApplicationInsights.Profiler.Shared;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IUploadContextValidator, UploadContextValidator>();

        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByEnvironmentVariable>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorInUserCache>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByUnzipping>();
        services.TryAddTransient<IUploaderPathProvider, UploaderPathProvider>();

        services.TryAddSingleton<IOutOfProcCallerFactory, OutOfProcCallerFactory>();

        services.TryAddSingleton<IFile, SystemFile>();

        services.TryAddSingleton<IRoleNameSource, EnvRoleName>();
        services.TryAddSingleton<IEnvironment, SystemEnvironment>();
        services.TryAddSingleton<IZipFile, SystemZipFile>();

        // Named pipe client
        services.TryAddTransient<ISerializationProvider, HighPerfJsonSerializationProvider>();
        services.TryAddTransient<IPayloadSerializer, HighPerfJsonSerializationProvider>();
        services.TryAddTransient<ISerializationOptionsProvider<JsonSerializerOptions>, HighPerfJsonSerializationProvider>();
        services.TryAddSingleton<INamedPipeClientFactory, NamedPipeClientFactory>();

        services.TryAddSingleton<IMetadataWriter, MetadataWriter>();

        return services;
    }
}