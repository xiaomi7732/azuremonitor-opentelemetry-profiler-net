//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal static class NativeMethods
{
#if !NETFRAMEWORK
    public const int HKEY_LOCAL_MACHINE = unchecked((int)0x80000002);
    public const uint RRF_RT_DWORD = 0x18;
    public const uint REG_DWORD = 4;

    [DllImport("advapi32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int RegGetValueW(IntPtr hKey, string subKey, string value, uint flags, out uint type, out uint data, ref uint pcbData);
#endif

    /// <summary>
    /// This is a convenience function.  If you unpack native dlls, you may want to simply LoadLibrary them
    /// so that they are guaranteed to be found when needed.  
    /// </summary>
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr LoadLibraryW(string lpFileName);
}

