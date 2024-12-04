//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

public interface IProfilerFrontendClient : IStampFrontendClient
{
    /// <summary>
    /// Get the settings contract for profiler
    /// </summary>
    /// <exception cref="InstrumentationKeyInvalidException">The Instrumentation Key is empty.</exception>
    Task<SettingsContract> GetProfilerSettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Get the blob access token for uploading the etl file.
    /// </summary>
    /// <exception cref="InstrumentationKeyInvalidException">The Instrumentation Key is empty.</exception>
    Task<BlobAccessPass> GetEtlUploadAccessAsync(DateTimeOffset etlStartTime, CancellationToken cancellationToken);

    /// <summary>
    /// Report ETL Upload Finish
    /// </summary>
    /// <exception cref="InstrumentationKeyInvalidException">The Instrumentation Key is empty.</exception>
    /// <returns>True if the etl is accepted otherwise false</returns>
    Task<bool> ReportEtlUploadFinishAsync(StampBlobUri blobUri, CancellationToken cancellationToken);

    /// <summary>
    /// Get tokens for a batch upload of symbol files.
    /// The request indicates a list of symbol files that are being offered for upload.
    /// The response indicates which, if any, files should be uploaded.
    /// </summary>
    /// <param name="pdbSignatures">The signatures of the symbol files being offered for upload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InstrumentationKeyInvalidException">The Instrumentation Key is empty.</exception>
    /// <returns>The list of tokens that the server needs. This list may be empty.</returns>
    Task<IEnumerable<SymbolUploadToken>> GetSymbolBatchTokensAsync(IEnumerable<PDBSignature> pdbSignatures, CancellationToken cancellationToken);

    /// <summary>
    /// Indicate to the service that the requested symbol files have been uploaded.
    /// </summary>
    /// <param name="commitRequests">A collection of <see cref="SymbolCommitRequest"/> instances indicating which blobs were uploaded.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InstrumentationKeyInvalidException">The Instrumentation Key is empty.</exception>
    /// <returns>A <see cref="SymbolBatchCommitResponse"/> object with details about the committed blobs.</returns>
    Task<SymbolBatchCommitResponse> CommitSymbolBatchUploadAsync(IEnumerable<SymbolCommitRequest> commitRequests, CancellationToken cancellationToken);
}
