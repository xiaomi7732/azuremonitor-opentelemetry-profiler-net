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
        const int headerSize = sizeof(long) + sizeof(long); // UtcTicks + Offset.Ticks
        int machineNameByteCount = machineName.Length * sizeof(char);
        int size = headerSize + machineNameByteCount;
        Span<byte> input = size <= 256 ? stackalloc byte[size] : new byte[size];

        BitConverter.TryWriteBytes(input, sessionId.UtcTicks);
        BitConverter.TryWriteBytes(input.Slice(sizeof(long)), sessionId.Offset.Ticks);
        Encoding.Unicode.GetBytes(machineName, input.Slice(headerSize));

        Span<byte> hash = stackalloc byte[16];
        XxHash128.Hash(input, hash);
        return new Guid(hash);
    }
}
