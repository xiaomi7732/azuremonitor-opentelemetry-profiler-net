//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceProfiler.Common.Utilities;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{
    public class TelemetryConfigurationBuilderTests
    {
        private const string TestIKey = "8e321e78-834f-4397-96fd-297fa844d140";
        private const string RegionalIngestionEndpoint = "https://westus2-2.in.applicationinsights.azure.com/";

        [Fact]
        public void ShouldApplyRegionalIngestionEndpointFromFullConnectionString()
        {
            Assert.True(ConnectionString.TryParse(
                $"InstrumentationKey={TestIKey};IngestionEndpoint={RegionalIngestionEndpoint}",
                out ConnectionString connectionString));

            using TelemetryConfiguration telemetryConfiguration = BuildWith(connectionString, tokenCredential: null);

            Assert.Contains("westus2-2.in.applicationinsights.azure.com", telemetryConfiguration.TelemetryChannel.EndpointAddress);
        }

        [Fact]
        public void ShouldApplyTokenCredentialWhenProvided()
        {
            Assert.True(ConnectionString.TryParse(
                $"InstrumentationKey={TestIKey};IngestionEndpoint={RegionalIngestionEndpoint}",
                out ConnectionString connectionString));

            using TelemetryConfiguration telemetryConfiguration = BuildWith(connectionString, new StubTokenCredential());

            Assert.NotNull(GetCredentialEnvelope(telemetryConfiguration));
        }

        [Fact]
        public void ShouldNotApplyTokenCredentialWhenNull()
        {
            Assert.True(ConnectionString.TryParse(
                $"InstrumentationKey={TestIKey};IngestionEndpoint={RegionalIngestionEndpoint}",
                out ConnectionString connectionString));

            using TelemetryConfiguration telemetryConfiguration = BuildWith(connectionString, tokenCredential: null);

            Assert.Null(GetCredentialEnvelope(telemetryConfiguration));
        }

        private static TelemetryConfiguration BuildWith(ConnectionString connectionString, TokenCredential? tokenCredential)
            => new TelemetryConfigurationBuilder(
                connectionString,
                tokenCredential,
                telemetryInitializers: [],
                NullLogger<TelemetryConfigurationBuilder>.Instance).Build();

        private static object? GetCredentialEnvelope(TelemetryConfiguration telemetryConfiguration)
            => typeof(TelemetryConfiguration)
                .GetTypeInfo()
                .GetDeclaredProperty("CredentialEnvelope")?
                .GetValue(telemetryConfiguration);

        private sealed class StubTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
        }
    }
}
