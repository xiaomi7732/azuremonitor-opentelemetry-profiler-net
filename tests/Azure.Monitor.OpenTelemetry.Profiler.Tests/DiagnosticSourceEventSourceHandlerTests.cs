// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class DiagnosticSourceEventSourceHandlerTests
{
    [Theory]
    // ASP.NET Core HTTP-in.
    [InlineData("[AS]Microsoft.AspNetCore/")]
    // Service Bus single-dispatch (processor) sources — Consumer kind -> request.
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusProcessor/")]
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusSessionProcessor/")]
    // Azure Functions isolated worker source.
    [InlineData("[AS]Microsoft.Azure.Functions.Worker/")]
    public void FilterAndPayloadSpecs_SubscribesToExpectedActivitySource(string expectedSpec)
    {
        string[] specs = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(expectedSpec, specs);
    }

    [Theory]
    // Service Bus receiver sources are ActivityKind.Client (dependencies), not requests — not subscribed.
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusReceiver/")]
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusSessionReceiver/")]
    public void FilterAndPayloadSpecs_DoesNotSubscribeToReceiverSources(string unexpectedSpec)
    {
        string[] specs = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain(unexpectedSpec, specs);
    }

    [Fact]
    public void FilterAndPayloadSpecs_OnlyContainsExpectedSources()
    {
        string[] specs = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Guard against accidental [AS]* firehose or stray DiagnosticListener specs.
        Assert.All(specs, spec => Assert.StartsWith("[AS]", spec));
        Assert.DoesNotContain("[AS]*", specs);
        Assert.Equal(4, specs.Length);
    }

    [Theory]
    // Functions worker, schema 1.37.0+ ("function <name>", ActivityKind.Internal -> dependency): remap.
    [InlineData("Microsoft.Azure.Functions.Worker", "function MySBFuncTest", true)]
    [InlineData("Microsoft.Azure.Functions.Worker", "function SomeOtherFunction", true)]
    // Functions worker, schema <= 1.17.0 ("Invoke", ActivityKind.Server -> request): do NOT remap.
    [InlineData("Microsoft.Azure.Functions.Worker", "Invoke", false)]
    // "function" without the trailing space is not the worker invocation naming.
    [InlineData("Microsoft.Azure.Functions.Worker", "function", false)]
    // Non-worker sources are never remapped, even with a "function "-like name.
    [InlineData("Microsoft.AspNetCore", "function MySBFuncTest", false)]
    [InlineData("Azure.Messaging.ServiceBus.ServiceBusProcessor", "ServiceBusProcessor.ProcessMessage", false)]
    [InlineData("Azure.Messaging.ServiceBus.ServiceBusReceiver", "ServiceBusReceiver.Receive", false)]
    [InlineData(null, "function MySBFuncTest", false)]
    public void ShouldRemapToParentRequest_OnlyForInternalWorkerSchema(string? sourceName, string requestName, bool expected)
    {
        Assert.Equal(expected, DiagnosticSourceEventSourceHandler.ShouldRemapToParentRequest(sourceName, requestName));
    }
}
