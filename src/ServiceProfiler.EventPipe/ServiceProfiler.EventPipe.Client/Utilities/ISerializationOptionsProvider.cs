namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities;

public interface ISerializationOptionsProvider<T>
{
    T Options { get; }
}
