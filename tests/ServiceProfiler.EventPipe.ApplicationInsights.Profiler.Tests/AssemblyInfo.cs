using Xunit;

// Disable parallel test execution to prevent concurrent ServiceProvider creation.
// Multiple ServiceProviders with hosted services and Application Insights telemetry
// channels can cause the test host to hang during disposal under resource-constrained
// CI containers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
