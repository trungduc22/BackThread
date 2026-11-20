using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Communication;
using NativeShellKit.Diagnostics;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Notifications;
using NativeShellKit.Security;
using NativeShellKit.Windows;
using NativeShellKit.Windows.SingleInstance;
using NativeShellKit.Windows.Startup;
using NativeShellKit.WinUI;
using WinRT;

namespace BackThread.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? initialPage = ParseArg(args, "--page");
        var identity = DaemonContract.CreateIdentity();
        identity.EnsureDirectoriesExist();

        var coordinator = new SingleInstanceCoordinator(
            identity,
            DaemonContract.AppMutexName,
            DaemonContract.AppActivationPipe);

        try
        {
            if (!coordinator.TryAcquirePrimaryInstance())
            {
                string cmd = string.IsNullOrWhiteSpace(initialPage) ? "--activate" : "--page:" + initialPage;
                coordinator.SignalExistingPrimaryInstanceAsync(cmd).GetAwaiter().GetResult();
                return 0;
            }

            var host = NativeShellHostBuilder.Create(identity, args)
                .AddWinUiShell()
                .AddUnifiedNotifications()
                .ConfigureServices(services =>
                {
                    services.AddWindowsServices(new Win32RegistryStartupBackend());
                    services.AddSingleton(coordinator);
                    services.AddCommunicationServices();
                    services.AddSecurityAndDiagnostics();
                    services.AddSingleton(sp => new DaemonClient(
                        sp.GetRequiredService<AppIdentity>(),
                        sp.GetRequiredService<IpcAuthManager>()));
                })
                .Build();

            try
            {
                ComWrappersSupport.InitializeComWrappers();
                Application.Start(_ =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(host, initialPage);
                });
                host.StopAsync().GetAwaiter().GetResult();
                return 0;
            }
            finally
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string? ParseArg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
