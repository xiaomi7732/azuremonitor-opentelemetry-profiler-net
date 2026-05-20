// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.IO.Hashing;
using System.Text;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Derives a stable artifact ID from the session timestamp and machine name so that
/// retries produce the same ID (idempotent) while different machines won't collide.
/// </summary>
internal static class ArtifactIdDerivation
{
    public static Guid DeriveArtifactId(DateTimeOffset sessionId, string machineName)
    {
        int size = sizeof(long) + sizeof(long) + machineName.Length * sizeof(char);
        Span<byte> input = size <= 256 ? stackalloc byte[size] : new byte[size];

        if (!BitConverter.TryWriteBytes(input, sessionId.UtcTicks))
        {
            throw new InvalidOperationException("Buffer too small for UtcTicks.");
        }

        if (!BitConverter.TryWriteBytes(input.Slice(8), sessionId.Offset.Ticks))
        {
            throw new InvalidOperationException("Buffer too small for Offset.Ticks.");
        }

        Encoding.Unicode.GetBytes(machineName, input.Slice(16));

        Span<byte> hash = stackalloc byte[16];
        XxHash128.Hash(input, hash);
        return new Guid(hash);
    }
}
