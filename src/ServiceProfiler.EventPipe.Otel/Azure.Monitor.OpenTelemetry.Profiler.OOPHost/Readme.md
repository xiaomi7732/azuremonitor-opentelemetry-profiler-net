# Azure Monitor OpenTelemetry Profiler out of Proc Host

## Build image

Call `BuildOOPImage.ps1`.

```powershell
..\tools\BuildOOPImage.ps1 -Version 0.0.6
```

* `ImageName`: provide image name, all lowercase. Default to `monitor-profiler`.
* `Version`: provide image version. Required.
* Image name and version forms up the image tag. `monitor-profiler:0.0.6` for example.

## Test the image locally

1. Create a environment file, for example, `debug.env`, with the following content:

    ```env
    ServiceProfiler__ConnectionString=<Your-connection-string>
    ServiceProfiler__TargetProcessName=<Target-process-name>
    Logging__LogLevel__Azure.Monitor.OpenTelemetry.Profiler.OOPHost=Trace
    ```

    * `ServiceProfiler__ConnectionString` defines the connection string to application insights resource.
    * `ServiceProfiler__TargetProcessName` sets the process name used for searching the target process.

1. Start the container:

    ```shell
    docker container rm -f "test-oop-profiler"
    docker run --name "test-oop-profiler" -it -d --env-file ./debug.env monitor-profiler:0.0.6
    docker logs "test-oop-profiler"
    ```

    You will see a log similar to this:

    ```log
    info: Microsoft.ApplicationInsights.Profiler.Shared.Services.TraceScavenger.TraceScavengerService[0] TraceScavengerService started. Initial delay: 00:00:00, Grace period from last access: 00:10:00
    info: Microsoft.ApplicationInsights.Profiler.Shared.Services.TraceScavenger.TraceScavengerListener[0] File scavenger started.
    ...
    info: Azure.Monitor.OpenTelemetry.Profiler.OOPHost.ServiceProfilerAgentBootstrap[0] Waiting for targeting process.
    info: Azure.Monitor.OpenTelemetry.Profiler.OOPHost.TargetProcessService[0] Looking for target process by name: VanillaApp
    ```
