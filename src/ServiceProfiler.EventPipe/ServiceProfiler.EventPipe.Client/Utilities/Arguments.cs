//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    /// <summary>
    /// Utility class for argument validation.
    /// </summary>
    internal static class ArgumentValidation
    {
        [Obsolete("Using editor generated code for null check. This method will be gone in the future codebase.", error: true)]
        public static T ThrowIfNull<T>(this T value, string paramName) where T : class => value ?? throw new ArgumentNullException(paramName);

        [Obsolete("Using editor generated code for null or empty check. This method will be gone in the future codebase.", error: true)]
        public static string ThrowIfNullOrEmpty(this string value, string paramName)
        {
            value.ThrowIfNull(paramName);

            return (value.Length != 0 && value[0] != '\0') ? value :
                throw new ArgumentException("Argument cannot be an empty string (\"\") or start with the null character.", paramName);
        }
    }
}
