// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Perfolizer.Horology;

namespace MvcStaticBenchmark.Benchmarks;

[MemoryDiagnoser]
[RPlotExporter]
public class DynamicStartupBenchmark
{
    private readonly object _lock = new();
    private ManualResetEvent? _resetEvent;
    private WebApplication? _app;

    [Benchmark]
    public void RunServer()
    {
        lock (_lock)
        {
            _resetEvent = new ManualResetEvent(false);

            using var listener = new HostingEventSourceListener(_resetEvent);
            var builder = WebApplication.CreateBuilder();

            // Add services to the container.
            builder.Services.AddMvcCore().AddApiExplorer();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            _app = builder.Build();

            // Configure the HTTP request pipeline.
            _app.UseSwagger();
            _app.UseSwaggerUI();

            _app.UseRouting();
            _app.MapControllers();

            var runTask = Task.Run(() => _app.Run());
            _resetEvent.WaitOne();
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        lock (_lock)
        {
            // Stop the host
            if (_app is not null)
            {
                _app.StopAsync().Wait();
                _app.DisposeAsync().AsTask().Wait();
                _app = null;
            }

            // Dispose the reset event
            if (_resetEvent is not null)
            {
                _resetEvent.Dispose();
                _resetEvent = null;
            }
        }
    }
}
