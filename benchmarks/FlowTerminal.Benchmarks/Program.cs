using BenchmarkDotNet.Running;
using FlowTerminal.Benchmarks;

// Runs all benchmarks. Example:
//   dotnet run -c Release --project benchmarks/FlowTerminal.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(PipelineThroughputBenchmark).Assembly).Run(args);
