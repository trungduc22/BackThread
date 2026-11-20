using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.DesignSystem.Themes;

namespace BackThread.App;

public sealed partial class App : Application
{
    private readonly IAppHost host;
    private readonly string? initialPage;
    private MainWindow? window;

    public App(IAppHost host, string? initialPage = null)
    {
        this.host = host;
        this.initialPage = initialPage;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        DesignSystemThemeResources.EnsureLoaded();
        await host.StartAsync();
        window = new MainWindow(host, initialPage);
        window.Activate();
    }
}
