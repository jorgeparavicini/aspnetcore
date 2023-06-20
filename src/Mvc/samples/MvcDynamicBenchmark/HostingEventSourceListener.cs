// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Tracing;

namespace MvcStaticBenchmark.Benchmarks;

public class HostingEventSourceListener(ManualResetEvent resetEvent) : EventListener
{
    private readonly ManualResetEvent _resetEvent = resetEvent;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft.AspNetCore.Hosting")
        {
            EnableEvents(eventSource, EventLevel.LogAlways);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == "ServerReady")
        {
            _resetEvent.Set();
        }
    }
}
