// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Xunit;

namespace ServiceProfiler.EventPipe.Upload.Tests
{
    /// <summary>
    /// Validates the uploader-side <see cref="UploadContextValidator"/> that runs against the bound
    /// <see cref="UploadContext"/>. Because the command-line parser's <c>[Option(Required = true)]</c>
    /// attributes were removed in favor of configuration binding, required-field enforcement now lives
    /// entirely in this validator.
    /// </summary>
    public class UploadContextValidatorTests
    {
        private static UploadContext CreateValidContext() => new()
        {
            AIInstrumentationKey = Guid.NewGuid(),
            HostUrl = new Uri("https://endpoint/"),
            SessionId = DateTimeOffset.UtcNow,
            StampId = "stamp",
            TraceFilePath = @"c:\trace.nettrace",
            MetadataFilePath = @"c:\meta.json",
            UploadMode = UploadMode.OnSuccess,
            SerializedSampleFilePath = @"c:\sample",
            TraceFileFormat = "Nettrace",
        };

        [Fact]
        public void Validate_WhenComplete_ReturnsNoError()
        {
            UploadContextValidator validator = new(fileExists: _ => true);

            string error = validator.Validate(CreateValidContext());

            Assert.True(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Validate_WhenTraceFilePathMissing_ReturnsError()
        {
            UploadContextValidator validator = new(fileExists: _ => true);
            UploadContext context = CreateValidContext();
            context.TraceFilePath = null!;

            string error = validator.Validate(context);

            Assert.Contains($"{nameof(UploadContext.TraceFilePath)} is required.", error);
        }

        [Fact]
        public void Validate_WhenTraceFilePathEmpty_ReturnsError()
        {
            UploadContextValidator validator = new(fileExists: _ => true);
            UploadContext context = CreateValidContext();
            context.TraceFilePath = string.Empty;

            string error = validator.Validate(context);

            Assert.Contains($"{nameof(UploadContext.TraceFilePath)} is required.", error);
        }
    }
}
