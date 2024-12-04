// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

public sealed class ProfilerFrontendClient : StampFrontendClient, IProfilerFrontendClient
{
    /// <summary>
    /// The client dealing with communication with the profiler frontend.
    /// </summary>
    /// <param name="host">Frontend url.</param>
    /// <param name="instrumentationKey">Application Insights instrumentation key.</param>
    /// <param name="machineName">Machine name.</param>
    /// <param name="featureVersion">The requested minimum feature version in the valid version format, e.g. '1.0.0'. '1' is invalid.</param>
    /// <param name="userAgent">User agent string to identify different agents.</param>
    /// <param name="tokenCredential">Token credential to provide authentication StampFrontend.</param>
    /// <param name="skipCertificateValidation">Set to true to skip certificate validation.</param>
    /// <param name="enableAzureCoreDiagnostics">Set to true to enable Azure.Core diagnostics logging.</param>
    public ProfilerFrontendClient(
        Uri host,
        Guid instrumentationKey,
        string machineName,
        string featureVersion = null,
        string userAgent = null,
        TokenCredential tokenCredential = null,
        bool skipCertificateValidation = false,
        bool enableAzureCoreDiagnostics = false)
        : base(host, instrumentationKey, machineName, featureVersion, userAgent, tokenCredential, skipCertificateValidation, enableAzureCoreDiagnostics)
    { }

    /// <inheritdoc/>
    public override async Task<string> GetStampIdAsync(CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        Response response = await Pipeline.GetAndEnsureSucceededAsync(GetStampIDPath(), cancellationToken).ConfigureAwait(false);
        string stampID = response.Content.ToString();
        if (!string.IsNullOrEmpty(stampID))
        {
            StampID = stampID;
        }

        return StampID;
    }

    /// <inheritdoc/>
    public async Task<SettingsContract> GetProfilerSettingsAsync(CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        Response response = await Pipeline.GetAndEnsureSucceededAsync(GetProfilerSettingsPath(), cancellationToken).ConfigureAwait(false);

        // The service may return 304 (Not Modified) to indicate that settings are up-to-date.
        if (response.Status == (int)HttpStatusCode.NotModified)
        {
            return null;
        }

        _currentProfilerSettings = await response.DeserializeAsync<SettingsContract>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return _currentProfilerSettings;
    }

    /// <inheritdoc/>
    public async Task<BlobAccessPass> GetEtlUploadAccessAsync(DateTimeOffset etlStartTime, CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        BlobAccessPass blobAccessPass = await Pipeline.GetAndEnsureSucceededAsync<BlobAccessPass>(GetEtlUploadStartPath(etlStartTime), cancellationToken).ConfigureAwait(false);
        return blobAccessPass;
    }

    /// <inheritdoc/>
    public async Task<bool> ReportEtlUploadFinishAsync(StampBlobUri blobUri, CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        _ = await Pipeline.PostAndEnsureSucceededAsync(GetEtlUploadStopPath(), blobUri, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SymbolUploadToken>> GetSymbolBatchTokensAsync(IEnumerable<PDBSignature> pdbSignatures, CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        if (pdbSignatures is null || !pdbSignatures.Any())
        {
            throw new ArgumentException("At least one PDB file must be offered for upload.", nameof(pdbSignatures));
        }

        Response response = await Pipeline.PostAndEnsureSucceededAsync(
            GetSymbolBatchGetTokenPath(),
            new SymbolBatchGetTokenRequest
            {
                Signatures = pdbSignatures
            },
            cancellationToken).ConfigureAwait(false);

        SymbolBatchGetTokenResponse symbolBatchGetTokenResponse = await response.DeserializeAsync<SymbolBatchGetTokenResponse>(cancellationToken).ConfigureAwait(false);
        return symbolBatchGetTokenResponse.Tokens;
    }

    /// <inheritdoc/>
    public async Task<SymbolBatchCommitResponse> CommitSymbolBatchUploadAsync(IEnumerable<SymbolCommitRequest> commitRequests, CancellationToken cancellationToken)
    {
        ThrowIfInstrumentationKeyIsEmpty(InstrumentationKey);

        if (commitRequests is null || !commitRequests.Any())
        {
            throw new ArgumentException("Must provide at least one commit request.", nameof(commitRequests));
        }

        Response response = await Pipeline.PostAndEnsureSucceededAsync(
            GetSymbolBatchCommitPath(),
            new SymbolBatchCommitRequest
            {
                Values = commitRequests
            },
            cancellationToken
            ).ConfigureAwait(false);

        return await response.DeserializeAsync<SymbolBatchCommitResponse>(cancellationToken).ConfigureAwait(false);
    }

    protected override Uri GetStampIDPath()
       => new($"{ProfilerApiPrefix}/stampid?iKey={InstrumentationKey}&machineName={MachineName}", UriKind.Relative);

    #region Private
    private Uri GetProfilerSettingsPath()
    {
        string path = $"{ProfilerApiPrefix}/settings?iKey={InstrumentationKey}&featureVersion={FeatureVersion}";

        if (_currentProfilerSettings != null)
        {
            path += $"&oldTimestamp={TimestampContract.TimestampToString(_currentProfilerSettings.LastModified)}";
        }

        return new Uri(path, UriKind.Relative);
    }

    private Uri GetEtlUploadStartPath(DateTimeOffset etlStartTime)
        => new($"{ProfilerApiPrefix}/etlupload/start?iKey={InstrumentationKey}&stampId={StampID}&machineName={MachineName}&etlStartTime={TimestampContract.TimestampToString(etlStartTime)}", UriKind.Relative);

    private Uri GetEtlUploadStopPath()
        => new($"{ProfilerApiPrefix}/etlupload/stop?iKey={InstrumentationKey}&stampId={StampID}", UriKind.Relative);

    private Uri GetSymbolBatchGetTokenPath()
        => new($"api/apps/{InstrumentationKey}/artifactkinds/symbol/batch/actions/gettoken?api-version=2023-09-01-preview", UriKind.Relative);

    private Uri GetSymbolBatchCommitPath()
        => new($"api/apps/{InstrumentationKey}/artifactkinds/symbol/batch/actions/commit?api-version=2023-09-01-preview", UriKind.Relative);

    private SettingsContract _currentProfilerSettings;

    private const string ProfilerApiPrefix = "api/profileragent/v4";
    private const string SnapshotApiPrefix = "api/snapshotagent/v4";
    #endregion
}
