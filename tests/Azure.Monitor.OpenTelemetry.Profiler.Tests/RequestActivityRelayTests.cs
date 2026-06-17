// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class RequestActivityRelayTests
{
    [Theory]
    // ASP.NET Core HTTP-in.
    [InlineData("Microsoft.AspNetCore.Hosting.HttpRequestIn")]
    // Service Bus single-dispatch (processor) consumption.
    [InlineData("ServiceBusProcessor.ProcessMessage")]
    [InlineData("ServiceBusSessionProcessor.ProcessSessionMessage")]
    // Service Bus batch (receiver) consumption, e.g. Azure Functions batch triggers.
    [InlineData("ServiceBusReceiver.Receive")]
    // Azure Functions isolated worker per-invocation activity.
    // Older worker schema (<= 1.17.0) uses the literal "Invoke".
    [InlineData("Invoke")]
    // Worker schema 1.37.0+ uses "function <FunctionName>".
    [InlineData("function MySBFuncTest")]
    [InlineData("function SomeOtherFunction")]
    public void IsInterestingRequest_KnownRequestNames_ReturnsTrue(string requestName)
    {
        Assert.True(RequestActivityRelay.IsInterestingRequest(requestName));
    }

    [Theory]
    // HTTP-out is explicitly excluded.
    [InlineData("System.Net.Http.HttpRequestOut")]
    // Non-consumption Service Bus receiver operations are excluded.
    [InlineData("ServiceBusReceiver.Complete")]
    [InlineData("ServiceBusReceiver.Abandon")]
    [InlineData("ServiceBusReceiver.Peek")]
    [InlineData("ServiceBusReceiver.RenewMessageLock")]
    // Service Bus send is excluded.
    [InlineData("ServiceBusSender.Send")]
    // Session lock/state operations are excluded.
    [InlineData("ServiceBusSessionReceiver.RenewSessionLock")]
    // Case sensitivity: matching is ordinal.
    [InlineData("invoke")]
    [InlineData("servicebusprocessor.processmessage")]
    // "function " prefix boundary: no trailing space / different word must not match.
    [InlineData("function")]
    [InlineData("functional.Thing")]
    [InlineData("Function MyFunc")]
    // Unrelated / empty.
    [InlineData("")]
    [InlineData("SomeOther.Activity")]
    public void IsInterestingRequest_UnknownRequestNames_ReturnsFalse(string requestName)
    {
        Assert.False(RequestActivityRelay.IsInterestingRequest(requestName));
    }

    [Fact]
    public void ExtractKeyIds_ValidW3CId_ReturnsSpanIdAndTraceId()
    {
        // W3C trace context: 00-<trace-id>-<span-id>-<flags>
        string id = "00-4dee62c12eaa9efca3d1f0565f3efda6-b3c470a7ee10c13b-01";

        (string requestId, string operationId) = RequestActivityRelay.ExtractKeyIds(id);

        Assert.Equal("b3c470a7ee10c13b", requestId);   // span-id
        Assert.Equal("4dee62c12eaa9efca3d1f0565f3efda6", operationId); // trace-id
    }

    [Theory]
    [InlineData("")]
    [InlineData("00-traceid-spanid")]               // only 3 sections
    [InlineData("00-traceid-spanid-01-extra")]      // 5 sections
    [InlineData("00--spanid-01")]                   // empty trace-id
    [InlineData("00-traceid--01")]                  // empty span-id
    public void ExtractKeyIds_InvalidId_Throws(string id)
    {
        Assert.Throws<InvalidDataException>(() => RequestActivityRelay.ExtractKeyIds(id));
    }

    [Fact]
    public void TryExtractKeyIds_ValidW3CId_ReturnsTrueWithSpanAndTraceId()
    {
        bool ok = RequestActivityRelay.TryExtractKeyIds(
            "00-10a36f22b23e6acb788d5412acd510c7-aca3108a6da34543-01",
            out string requestId,
            out string operationId);

        Assert.True(ok);
        Assert.Equal("aca3108a6da34543", requestId);                  // span-id (parent → request id)
        Assert.Equal("10a36f22b23e6acb788d5412acd510c7", operationId); // trace-id
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00-traceid-spanid")]          // only 3 sections
    [InlineData("00--spanid-01")]              // empty trace-id
    [InlineData("00-traceid--01")]             // empty span-id
    public void TryExtractKeyIds_InvalidId_ReturnsFalse(string? id)
    {
        bool ok = RequestActivityRelay.TryExtractKeyIds(id, out string requestId, out string operationId);

        Assert.False(ok);
        Assert.Equal(string.Empty, requestId);
        Assert.Equal(string.Empty, operationId);
    }
}
