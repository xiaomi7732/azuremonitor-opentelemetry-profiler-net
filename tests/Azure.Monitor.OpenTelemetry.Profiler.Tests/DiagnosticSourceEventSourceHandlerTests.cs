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
    // Service Bus single-dispatch (processor) sources.
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusProcessor/")]
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusSessionProcessor/")]
    // Service Bus batch (receiver) sources.
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusReceiver/")]
    [InlineData("[AS]Azure.Messaging.ServiceBus.ServiceBusSessionReceiver/")]
    // Azure Functions isolated worker source.
    [InlineData("[AS]Microsoft.Azure.Functions.Worker/")]
    public void FilterAndPayloadSpecs_SubscribesToExpectedActivitySource(string expectedSpec)
    {
        string[] specs = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(expectedSpec, specs);
    }

    [Fact]
    public void FilterAndPayloadSpecs_OnlyContainsExpectedSources()
    {
        string[] specs = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Guard against accidental [AS]* firehose or stray DiagnosticListener specs.
        Assert.All(specs, spec => Assert.StartsWith("[AS]", spec));
        Assert.DoesNotContain("[AS]*", specs);
        Assert.Equal(6, specs.Length);
    }
}
