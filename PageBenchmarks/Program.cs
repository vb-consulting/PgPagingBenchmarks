global using BenchmarkDotNet.Attributes;
global using Npgsql;
global using NpgsqlTypes;
using PageBenchmarks;
using BenchmarkDotNet.Running;

Functions.Recreate();
BenchmarkRunner.Run<PageTest>();

// BenchmarkRunner
//     .Run<HttpClientTests>(
//         DefaultConfig.Instance.AddJob(
//             Job.Default.WithToolchain(new InProcessEmitToolchain(timeout: TimeSpan.FromSeconds(9), logOutput: false))
//             .WithLaunchCount(1)
//             .WithWarmupCount(5)
//             .WithIterationCount(100)
//             .WithIterationTime(TimeInterval.FromMilliseconds(80)))
//         .AddLogger(new ConsoleLogger(unicodeSupport: true, ConsoleLogger.CreateGrayScheme()))
//         .WithOptions(ConfigOptions.DisableLogFile));
