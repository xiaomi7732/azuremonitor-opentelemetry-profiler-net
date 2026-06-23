// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Describes the outcome of validating the configured connection string, without
/// exposing the raw connection string value itself.
/// </summary>
internal enum ConnectionStringValidationResult
{
    /// <summary>
    /// The connection string is present and parsed into a usable instrumentation key.
    /// </summary>
    Valid,

    /// <summary>
    /// No connection string was provided (the raw value is <see langword="null"/>).
    /// </summary>
    NotConfigured,

    /// <summary>
    /// A connection string was provided but is empty or whitespace.
    /// </summary>
    Empty,

    /// <summary>
    /// The connection string is present but its instrumentation key is missing or malformed.
    /// </summary>
    InvalidInstrumentationKey,

    /// <summary>
    /// The connection string is present but could not be parsed for some other reason.
    /// </summary>
    Malformed,
}
