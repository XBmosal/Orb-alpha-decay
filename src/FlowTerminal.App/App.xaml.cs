using System.IO;
using System.Windows;
using FlowTerminal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FlowTerminal.App;

/// <summary>
/// Application entry point. Builds the generic host (DI + configuration + Serilog)
/// and shows the main shell. The shell is strictly read-only: there is no
/// order-entry surface anywhere in the application.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths().EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.Logs, "flowterminal-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(paths);
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Log.Information("{Product} starting in {Mode} mode (read-only).",
            AppInfo.ProductName, AppInfo.DescribeMode(RunMode.Mock));

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("{Product} shutting down.", AppInfo.ProductName);
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
