// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Monitor.Diagnostics.Models;
using Azure.Monitor.Diagnostics.Profiler;
using Azure.Storage.Blobs.Models;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.ApplicationInsights.Profiler.Uploader;
using Microsoft.ApplicationInsights.Profiler.Uploader.Stubs;
using Microsoft.ApplicationInsights.Profiler.Uploader.TraceValidators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Agent;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Upload.Tests
{
    public class TraceUploaderTests
    {
        [Fact]
        public async Task ShouldZipTheTraceFileAsync()
        {
            bool isZipCalled = false;
            _zipUtilityMock.Setup(u => u.ZipFile(_traceFilePath, It.IsAny<string>(), It.IsAny<IEnumerable<string>>())).Callback(() =>
            {
                isZipCalled = true;
            });

            IServiceProvider serviceProvider = GetTestServiceProvider();
            TraceUploader uploader = CreateTraceUploader(serviceProvider);

            Assert.False(isZipCalled);
            await uploader.UploadAsync(CancellationToken.None);
            Assert.True(isZipCalled);
        }

        [Fact]
        public async Task ShouldUploadBlobAsync()
        {
            bool isBlobUploadCalled = false;
            const string zippedFilePath = "test_zipped.file";
            _zipUtilityMock.Setup(u => u.ZipFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>())).Returns(zippedFilePath);

            IServiceProvider serviceProvider = GetTestServiceProvider(null, (path, cancellationToken) =>
            {
                isBlobUploadCalled = true;
                return Task.FromResult<Response<BlobContentInfo>>(null!);
            });

            TraceUploader uploader = CreateTraceUploader(serviceProvider);

            Assert.False(isBlobUploadCalled);
            await uploader.UploadAsync(CancellationToken.None);
            Assert.True(isBlobUploadCalled);
        }

        [Fact]
        public async Task ShouldBlobSetMetadataAsync()
        {
            bool isSetMetadataAsyncCalled = false;
            IServiceProvider serviceProvider = GetTestServiceProvider(null, null, (metadata, conditions, cancellationToken) =>
            {
                Assert.Equal(Guid.Empty.ToString(), metadata[BlobMetadataConstants.DataCubeMetaName]);
                Assert.Equal(Environment.MachineName, metadata[BlobMetadataConstants.MachineNameMetaName], ignoreCase: true);
                Assert.Equal("C#", metadata[BlobMetadataConstants.ProgrammingLanguageMetaName]);
                Assert.Equal(nameof(TraceFileFormat.Netperf), metadata[BlobMetadataConstants.TraceFileFormatMetaName]);
                Assert.Equal("MockRoleName", metadata[BlobMetadataConstants.RoleName]);
                Assert.Equal("MockTrigger", metadata[BlobMetadataConstants.TriggerType]);
                isSetMetadataAsyncCalled = true;
                return Task.FromResult<Response<BlobInfo>>(null!);
            });

            TraceUploader uploader = CreateTraceUploader(serviceProvider);

            Assert.False(isSetMetadataAsyncCalled);
            await uploader.UploadAsync(CancellationToken.None);
            Assert.True(isSetMetadataAsyncCalled);
        }

        [Fact]
        public async Task ShouldCommitProfilerArtifactAsync()
        {
            bool isCommitProfilerArtifactAsyncCalled = false;
            IServiceProvider serviceProvider = GetTestServiceProvider(() =>
            {
                isCommitProfilerArtifactAsyncCalled = true;
            });
            TraceUploader uploader = CreateTraceUploader(serviceProvider);

            Assert.False(isCommitProfilerArtifactAsyncCalled);
            await uploader.UploadAsync(CancellationToken.None);
            Assert.True(isCommitProfilerArtifactAsyncCalled);
        }

        [Fact]
        public void ShouldReturnNoValidActivityWhenValidationFails()
        {
            List<SampleActivity> input = new List<SampleActivity>(){
                new SampleActivity(),
            };

            Mock<ITraceValidator> exceptionValidator = new Mock<ITraceValidator>();
            exceptionValidator.Setup(v => v.Validate(It.IsAny<IEnumerable<SampleActivity>>())).Throws(new ValidateFailedException(toStopUploading: true));

            using (ServiceProvider provider = GetTestServiceProvider(traceValidatorFactory: () => exceptionValidator.Object))
            {
                TraceUploader target = ActivatorUtilities.CreateInstance<TraceUploader>(provider);
                IEnumerable<SampleActivity> output = target.GetValidSamples("abc.etl.zip", input);

                Assert.Equal(Enumerable.Empty<SampleActivity>(), output);
            }
        }

        private TraceUploader CreateTraceUploader(IServiceProvider serviceProvider)
            => new TestTraceUploader(
                serviceProvider.GetRequiredService<IZipUtility>(),
                serviceProvider.GetRequiredService<IBlobClientFactory>(),
                serviceProvider.GetRequiredService<IAppInsightsLogger>(),
                serviceProvider.GetRequiredService<IOSPlatformProvider>(),
                serviceProvider.GetRequiredService<ITraceValidatorFactory>(),
                serviceProvider.GetRequiredService<ISampleActivitySerializer>(),
                serviceProvider.GetRequiredService<UploadContext>(),
                serviceProvider.GetRequiredService<IUploadContextValidator>(),
                serviceProvider.GetRequiredService<IAppProfileClientFactory>(),
                serviceProvider.GetRequiredService<ICustomEventsSender>(),
                serviceProvider.GetRequiredService<ILogger<TraceUploader>>(),
                _profilerClientMock.Object);

        private ServiceProvider GetTestServiceProvider(
            Action? onCommitProfilerArtifact = null,
            Func<string, CancellationToken, Task<Response<BlobContentInfo>>>? onBlobClientUploadAsync = null,
            Func<IDictionary<string, string>, BlobRequestConditions, CancellationToken, Task<Response<BlobInfo>>>? onBlobClientSetMetadataAsync = null,
            Func<UploadContext>? uploadContextFactory = null,
            Func<IUploadContextValidator>? uploadContextValidatorFactory = null,
            Func<ITraceValidator>? traceValidatorFactory = null)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddTransient<IZipUtility>(_ => _zipUtilityMock.Object);

            _profilerClientMock.Setup(s => s.GetProfilerArtifactUploadTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Uri(_testBlobUrl));
            _profilerClientMock.Setup(s => s.CommitProfilerArtifactAsync(It.IsAny<Guid>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
                .Callback(() => onCommitProfilerArtifact?.Invoke())
                .ReturnsAsync(CreateAcceptedArtifact());
            services.AddTransient<IProfilerClient>(_ => _profilerClientMock.Object);
            services.AddTransient<IAppInsightsLogger>(_ => _telemetryLoggerMock.Object);

            var blobClientFactoryMock = new Mock<IBlobClientFactory>();
            blobClientFactoryMock.Setup(f => f.CreateBlobClient(It.IsAny<Uri>())).Returns(new MockBlobClient(
                new Uri(_testBlobUrl),
                onBlobClientUploadAsync,
                onBlobClientSetMetadataAsync ?? ((metadata, conditions, cancellationToken) => Task.FromResult(CreateBlobInfoResponse()))));
            services.AddTransient<IBlobClientFactory>(_ => blobClientFactoryMock.Object);
            services.AddSingleton<IOSPlatformProvider, OSPlatformProvider>();

            Mock<ITraceValidatorFactory> traceValidatorFactoryMock = new Mock<ITraceValidatorFactory>();
            traceValidatorFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(traceValidatorFactory?.Invoke() ?? new AlwaysPassValidator());
            services.AddTransient<ITraceValidatorFactory>(_ => traceValidatorFactoryMock.Object);

            services.AddTransient<IPayloadSerializer, HighPerfJsonSerializationProvider>();

            Mock<ISampleActivitySerializer> sampleActivitySerializerMock = new Mock<ISampleActivitySerializer>();
            sampleActivitySerializerMock.Setup(s => s.DeserializeFromFileAsync(It.IsAny<string>())).ReturnsAsync(new[] { new SampleActivity() });
            sampleActivitySerializerMock.Setup(s => s.SerializeToFileAsync(It.IsAny<IEnumerable<SampleActivity>>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            services.AddTransient<ISampleActivitySerializer>(_ => sampleActivitySerializerMock.Object);

            uploadContextFactory ??= CreateTestUploadContext;
            services.AddTransient<UploadContext>(_ => uploadContextFactory());

            services.AddTransient<IUploadContextValidator>(_ =>
            {
                uploadContextValidatorFactory ??= (() => CreateUploadContextValidator());
                return uploadContextValidatorFactory();
            });

            services.AddTransient<IAppProfileClient>(_ =>
            {
                Mock<IAppProfileClient> appProfileMock = new Mock<IAppProfileClient>();
                appProfileMock.Setup(m => m.GetAppProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new AppProfileResponse() { AppId = _testAppId });
                return appProfileMock.Object;
            });

            services.AddTransient<IAppProfileClientFactory>(p =>
            {
                Mock<IAppProfileClientFactory> factoryMock = new Mock<IAppProfileClientFactory>();
                factoryMock.Setup(f => f.Create(It.IsAny<UploadContextExtension>())).Returns(p.GetRequiredService<IAppProfileClient>());
                return factoryMock.Object;
            });

            Mock<ICustomEventsSender> customEventsSenderMock = new();
            services.AddTransient<ICustomEventsSender>(_ => customEventsSenderMock.Object);

            return services.BuildServiceProvider();
        }

        private static AcceptedArtifact CreateAcceptedArtifact() => new()
        {
            AcceptedTime = DateTime.UtcNow,
            BlobUri = new Uri(_testBlobUrl),
            CorrelationId = "test-correlation-id",
            StampId = "test-stamp-id",
            ArtifactLocationId = "test-artifact-location",
            DownloadUri = new Uri(_testBlobUrl),
        };

        private static Response<BlobInfo> CreateBlobInfoResponse()
            => Response.FromValue(
                BlobsModelFactory.BlobInfo(new ETag("\"test-etag\""), DateTimeOffset.UtcNow),
                Mock.Of<Response>());

        private readonly Mock<IZipUtility> _zipUtilityMock = new Mock<IZipUtility>();
        private readonly Mock<IProfilerClient> _profilerClientMock = new Mock<IProfilerClient>();
        private readonly Mock<IAppInsightsLogger> _telemetryLoggerMock = new Mock<IAppInsightsLogger>();
        private const string _testBlobUrl = "https://this_is_blob_url";

        private const string _traceFilePath = "balabala";
        private static readonly Guid _testAppId = Guid.NewGuid();

        private UploadContext CreateTestUploadContext()
        {
            return new UploadContext
            {
                TraceFilePath = _traceFilePath,
                UploadMode = UploadMode.Always,
                TriggerType = "MockTrigger",
                RoleName = "MockRoleName"
            };
        }

        private IUploadContextValidator CreateUploadContextValidator(Action<Mock<IUploadContextValidator>>? mockConfigure = null)
        {
            Mock<IUploadContextValidator> uploadContextValidatorMock = new Mock<IUploadContextValidator>();
            mockConfigure ??= mock =>
            {
                mock.Setup(m => m.Validate(It.IsAny<UploadContext>())).Returns(string.Empty);
            };
            mockConfigure(uploadContextValidatorMock);

            return uploadContextValidatorMock.Object;
        }

        private sealed class TestTraceUploader : TraceUploader
        {
            private readonly IProfilerClient _profilerClient;

            public TestTraceUploader(
                IZipUtility zipUtility,
                IBlobClientFactory blobClientFactory,
                IAppInsightsLogger telemetryLogger,
                IOSPlatformProvider oSPlatformProvider,
                ITraceValidatorFactory traceValidatorFactory,
                ISampleActivitySerializer sampleActivitySerializer,
                UploadContext uploadContext,
                IUploadContextValidator uploadContextValidator,
                IAppProfileClientFactory appProfileClientFactory,
                ICustomEventsSender customEventsSender,
                ILogger<TraceUploader> logger,
                IProfilerClient profilerClient)
                : base(
                    zipUtility,
                    blobClientFactory,
                    telemetryLogger,
                    oSPlatformProvider,
                    traceValidatorFactory,
                    sampleActivitySerializer,
                    uploadContext,
                    uploadContextValidator,
                    appProfileClientFactory,
                    customEventsSender,
                    logger)
            {
                _profilerClient = profilerClient;
            }

            protected override IProfilerClient CreateProfilerClient(UploadContextExtension extendedContext)
                => _profilerClient;
        }
    }
}
