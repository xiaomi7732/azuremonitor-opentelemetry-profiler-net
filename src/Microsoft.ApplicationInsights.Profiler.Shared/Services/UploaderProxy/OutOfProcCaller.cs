//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;

internal class OutOfProcCaller : IOutOfProcCaller
{
    private readonly ILogger _logger;
    private readonly string _fileName;
    private readonly string _arguments;    

    public OutOfProcCaller(
        string fileName,
        string arguments,
        ILogger<OutOfProcCaller> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException($"'{nameof(fileName)}' cannot be null or empty.", nameof(fileName));
        }
        _fileName = fileName;

        if (string.IsNullOrEmpty(arguments))
        {
            throw new ArgumentException($"'{nameof(arguments)}' cannot be null or empty.", nameof(arguments));
        }
        _arguments = arguments;
    }


    public int ExecuteAndWait(ProcessPriorityClass processPriorityClass = ProcessPriorityClass.Normal)
    {
        using Process p = ExecuteImp(processPriorityClass);
        // Read stdout and stderr asynchronously to avoid deadlock when pipe buffers fill.
        string stderr = string.Empty;
        string stdout = string.Empty;
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr += e.Data + Environment.NewLine; };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout += e.Data + Environment.NewLine; };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogDebug("Uploader stdout: {stdout}", stdout);
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogError("Uploader stderr: {stderr}", stderr);
        }
        if (p.ExitCode != 0)
        {
            _logger.LogError("Uploader process exited with code {exitCode}.", p.ExitCode);
        }

        return p.ExitCode;
    }


    private Process ExecuteImp(ProcessPriorityClass processPriorityClass)
    {
        _logger.LogDebug("Calling execute out of proc on {fileName} with arguments: {arguments}. Intended priority: {intendedPriority}", _fileName, _arguments, processPriorityClass);

        ProcessStartInfo startInfo = new ProcessStartInfo(_fileName, _arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Process process = Process.Start(startInfo);
        try
        {
            process.PriorityClass = processPriorityClass;
        }
        catch (Exception ex) when (
            ex is Win32Exception ||
            ex is NotSupportedException)
        {
            // This could happen in restrained environment like Antares.
            _logger.LogDebug(ex, "Can't set process priority to {intendedPriority}.", processPriorityClass);
        }

        return process;
    }
}
