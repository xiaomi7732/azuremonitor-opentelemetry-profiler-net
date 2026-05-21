// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Monitor.Diagnostics.Profiler;

namespace Microsoft.ApplicationInsights.Profiler.Uploader;

/// <summary>
/// Service that can create <see cref="IProfilerClient"/> instances for uploading.
/// </summary>
internal interface IProfilerClientFactory
{
    /// <summary>
    /// Creates an instance of <see cref="IProfilerClient"/> configured for the given upload context.
    /// </summary>
    IProfilerClient Create(UploadContextExtension uploadContextExtension);
}
