// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System.Runtime.InteropServices;
using Xunit;

namespace ServiceProfiler.EventPipe.Upload.Tests
{
    /// <summary>
    /// An xUnit <see cref="FactAttribute"/> that is skipped on non-Windows platforms. Use for tests
    /// that exercise genuinely Windows-only behavior (e.g. Win32 P/Invokes such as
    /// <c>CommandLineToArgvW</c>), so they run for real on Windows and are cleanly skipped elsewhere.
    /// </summary>
    public sealed class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "Windows-only: exercises Windows command-line tokenization (CommandLineToArgvW) semantics.";
            }
        }
    }
}
