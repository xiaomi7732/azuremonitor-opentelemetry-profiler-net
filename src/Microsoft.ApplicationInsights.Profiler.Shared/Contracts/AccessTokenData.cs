// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

/// <summary>
/// A serialization-safe data transfer object for <see cref="Azure.Core.AccessToken"/>.
/// Avoids serializing the raw Azure.Core struct, which may gain non-serializable
/// properties (e.g., X509Certificate2) in newer Azure.Core versions.
/// </summary>
internal record AccessTokenData
{
    public string? Token { get; init; }
    public DateTimeOffset ExpiresOn { get; init; }
}
