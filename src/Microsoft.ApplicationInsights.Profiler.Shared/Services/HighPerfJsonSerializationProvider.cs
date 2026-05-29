using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// An implementation of JsonSerializationProvider based on System.Text.Json.
/// </summary>
internal class HighPerfJsonSerializationProvider : ISerializationProvider, IPayloadSerializer, ISerializationOptionsProvider<JsonSerializerOptions>
{
    private static readonly JsonSerializerOptions s_serializerOptions = BuildJsonSerializationOptions();
    private readonly ILogger _logger;

    public HighPerfJsonSerializationProvider()
        : this(NullLogger<HighPerfJsonSerializationProvider>.Instance)
    {
    }

    public HighPerfJsonSerializationProvider(ILogger<HighPerfJsonSerializationProvider> logger)
    {
        _logger = logger;
    }

    public JsonSerializerOptions Options => s_serializerOptions;

    public bool TryDeserialize<T>(string serialized, out T? obj)
    {
        obj = default;
        if (string.IsNullOrEmpty(serialized))
        {
            return false;
        }

        try
        {
            obj = JsonSerializer.Deserialize<T>(serialized, s_serializerOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to deserialize payload as {typeName}.", typeof(T).FullName);
            return false;
        }
    }
    public bool TrySerialize<T>(T obj, out string? serialized)
    {
        serialized = default;

        if (obj is null)
        {
            return false;
        }

        try
        {
            serialized = JsonSerializer.Serialize<T>(obj, s_serializerOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to serialize object of type {typeName}.", typeof(T).FullName);
            return false;
        }
    }

    private static JsonSerializerOptions BuildJsonSerializationOptions()
    {
        // Start with WebDefaults:
        // - Property names are treated as case-insensitive.
        // - "camelCase" name formatting should be employed.
        // - Quoted numbers (JSON strings for number properties) are allowed.
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(HighPerfJsonStringConverter.Instance);
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        return options;
    }
}
