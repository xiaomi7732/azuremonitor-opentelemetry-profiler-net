using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    /// <summary>
    /// An adapter for <see cref="System.Environment" /> methods to be mockable.
    /// </summary>
    internal class SystemEnvironment : IEnvironment
    {
        /// <inheritdoc />
        public string ExpandEnvironmentVariables(string name) => Environment.ExpandEnvironmentVariables(name);
    }
}
