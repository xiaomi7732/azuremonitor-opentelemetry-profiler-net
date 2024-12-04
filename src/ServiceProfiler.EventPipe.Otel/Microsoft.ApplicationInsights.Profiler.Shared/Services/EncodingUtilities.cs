//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static System.Globalization.CultureInfo;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal static class EncodingUtilities
{
    public static string Base64Encode(string plainText)
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return System.Convert.ToBase64String(plainTextBytes);
    }

    public static string Base64Decode(string base64EncodedData)
    {
        var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
        return System.Text.Encoding.UTF8.GetString(base64EncodedBytes, 0, base64EncodedBytes.Length);
    }

    public static string Sha256(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Sha256(bytes, bytesNeeded: 16);
    }

    public static string Sha256(Guid guid)
    {
        return Sha256(guid.ToByteArray(), bytesNeeded: 16);
    }

    /// <summary>
    /// Compute a one-way hash of the input string to serve as a stable anonymous identifier.
    /// Use this to anonymize PII.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>Anonymized hash of the input string.</returns>
    public static string Anonymize(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Sha256(bytes, bytesNeeded: 16);
    }

    /// <summary>
    /// Compute a one-way hash of the input string to serve as a stable anonymous identifier.
    /// Use this to anonymize PII.
    /// This version returns a more compact version of <see cref="Anonymize(string)"/>.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>Anonymized hash of the input string.</returns>
    public static string AnonymizeBase64(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        // bytesNeeded should be a multiple of 3 to get the most efficiency
        // out of Base64 encoding (every 3 bytes encodes to 4 chars).
        // Otherwise the result will be padded with '=' chars.
        return Sha256Base64(bytes, bytesNeeded: 9);
    }

    public static string Anonymize(Guid input)
    {
        return Sha256(input.ToByteArray(), bytesNeeded: 16);
    }

    public static string MarkAsPII(string input)
    {
        return input != null ? PIIMarker + input : input;
    }

    public static bool IsPII(string input)
    {
        return input != null && input.IndexOf(PIIMarker, StringComparison.Ordinal) >= 0;
    }

    #region Private
    private static string Sha256(byte[] input, int bytesNeeded)
    {
        var encryptedBytes = s_sha256Encoder.ComputeHash(input);
        return string.Join("", encryptedBytes.Take(bytesNeeded).Select(b => b.ToString("x2", InvariantCulture)));
    }

    private static string Sha256Base64(byte[] input, int bytesNeeded)
    {
        var encryptedBytes = s_sha256Encoder.ComputeHash(input);
        return Convert.ToBase64String(encryptedBytes, 0, Math.Min(bytesNeeded, encryptedBytes.Length));
    }

    /// <summary>
    /// Returns a random sequence of bytes suitable for creating unique IDs
    /// like session IDs.
    /// </summary>
    /// <returns>A random sequence of bytes of the given length.</returns>
    public static byte[] GetRandomBytes(int length)
    {
        var buff = new byte[length];
        using (var rnd = RandomNumberGenerator.Create())
        {
            rnd.GetBytes(buff);
            return buff;
        }
    }

    private static readonly HashAlgorithm s_sha256Encoder = SHA256.Create();

    private const string PIIMarker = "[PII]";
    #endregion
}

