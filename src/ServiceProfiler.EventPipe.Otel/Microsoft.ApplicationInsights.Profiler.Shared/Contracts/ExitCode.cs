//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

/// <summary>
/// Possible uploader exit codes.
/// </summary>
internal enum ExitCode
{
    Unknown = 0xffff,
    Unavailable = 0xfffe,
    Success = 0,
    InvalidArgs = unchecked((int)0x87FF0001),
    //MissingIKey = unchecked((int)0x87FF0002),
    IKeyIsNotGUID = unchecked((int)0x87FF0003),
    Exception_WebProxy = unchecked((int)0x87FF0004),
    Exception_CreateDumpFolder = unchecked((int)0x87FF0005),
    ProcessWatcherCouldNotStart = unchecked((int)0x87FF0006),
    AnotherInstanceRunning = unchecked((int)0x87FF0007),
    InvalidProcessId = unchecked((int)0x87FF0008),
    Exception_FeatureUnavailable = unchecked((int)0x87FF0009),
    Exception_Handled = unchecked((int)0x87FF0010),
    Exception_RequestTimeout = unchecked((int)0x87FF0011),
    MissingConnectionStringAndIkey = unchecked((int)0x87FF0012),
    UnableToParseConnectionString = unchecked((int)0x87FF0013),
    UnableToParseAuthenticationString = unchecked((int)0x87FF0014),
    AadAuthenticationFailed = unchecked((int)0x87FF0015),

    //https://docs.microsoft.com/en-us/dotnet/framework/interop/how-to-map-hresults-and-exceptions
    Exception_InvalidOperationException = unchecked((int)0x87FF1509),

    Exception_RemoteCouldNotBeResolved = unchecked(-2013326080),

    UnhandledException = unchecked((int)0xE0434352),
}

internal static class ExitCodeParser
{
    public static ExitCode Parse(int exitCode)
    {
        return !Enum.IsDefined(typeof(ExitCode), exitCode) ? ExitCode.Unknown : (ExitCode)exitCode;
    }

    // Extract the code from the HRESULT and map it to an ExitCode
    // https://msdn.microsoft.com/en-us/library/cc231198.aspx
    public static ExitCode FromException(Exception ex)
    {
        if (ex == null)
        {
            return ExitCode.Unknown;
        }

        ExitCode exitCode = Parse(unchecked((int)0x87FF0000) | (ex.HResult & 0xffff));

        if (exitCode == ExitCode.Unknown)
        {
            if (ex.InnerException != null)
            {
                // if there's an inner exception try to parse
                exitCode = FromException(ex.InnerException);
            }
            else
            {
                // if there's no mapping between ExitCode and HResult then use a custom exit code
                exitCode = ExitCode.Exception_Handled;
            }
        }

        return exitCode;
    }
}

