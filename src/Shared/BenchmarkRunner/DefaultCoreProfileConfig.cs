// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Validators;

namespace BenchmarkDotNet.Attributes;

internal sealed class DefaultCoreProfileConfig : ManualConfig
{
    public DefaultCoreProfileConfig()
    {
        AddLogger(ConsoleLogger.Default);
        AddExporter(MarkdownExporter.GitHub);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.OperationsPerSecond);
        AddColumnProvider(DefaultColumnProviders.Instance);

        AddValidator(JitOptimizationsValidator.FailOnError);

        AddJob(Job.InProcess
            .WithToolchain(InProcessNoEmitToolchain.Instance)
            .WithStrategy(RunStrategy.Throughput)
            .WithLaunchCount(4)
            .WithWarmupCount(50)
            .WithIterationCount(200));
    }
}
