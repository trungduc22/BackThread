using BackThread.App.Pages;
using BackThread.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Windows.SingleInstance;
using NativeShellKit.Windows.Startup;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using Windows.Graphics;

namespace BackThread.App;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost host;
    private readonly INavigationService navigation;
    private readonly IThemeService theme;
    private readonly INotificationService notifications;
    private readonly IShellWindowService windowService;
    private readonly DaemonClient daemon;
    private readonly WindowsStartupService startup;
    private readonly IDiagnosticsService diagnostics;
    private readonly SingleInstanceCoordinator coordinator;

    public MainWindow(IAppHost host, string? initialPage = null)
    {
        this.host = host;
        InitializeComponent();

        navigation = host.Services.GetRequiredService<INavigationService>();
        theme = host.Services.GetRequiredService<IThemeService>();
        notifications = host.Services.GetRequiredService<INotificationService>();
        windowService = host.Services.GetRequiredService<IShellWindowService>();
        daemon = host.Services.GetRequiredService<DaemonClient>();
        startup = host.Services.GetRequiredService<WindowsStartupService>();
        diagnostics = host.Services.GetRequiredService<IDiagnosticsService>();
        coordinator = host.Services.GetRequiredService<SingleInstanceCoordinator>();

        if (windowService is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (theme is WinUiThemeService winUiTheme)
            winUiTheme.TryApplyBackdrop(this);

        if (windowService.WindowHandle != nint.Zero)
        {
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowService.WindowHandle));
            appWindow.Resize(new SizeInt32(1140, 760));
        }

        notifications.InAppNotificationRaised += (_, record) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var banner = new NotificationBannerControl();
                banner.Show(record);
                banner.Dismissed += (_, _) => ShellView.SetBannerContent(null);
                ShellView.SetBannerContent(banner);
            });
        };

        coordinator.ActivationCommandReceived += OnActivationCommand;
        Closed += (_, _) => coordinator.ActivationCommandReceived -= OnActivationCommand;

        RegisterRoutes();
        ShellView.Initialize(host.Identity.DisplayName, navigation, theme);

        if (!string.IsNullOrWhiteSpace(initialPage))
            navigation.Navigate(initialPage);
    }

    private void OnActivationCommand(string command)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            windowService.ShowAndActivate();
            if (command.StartsWith("--page:", StringComparison.OrdinalIgnoreCase))
            {
                string route = command["--page:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(route))
                    navigation.Navigate(route);
            }
        });
    }

    private void RegisterRoutes()
    {
        navigation.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", () => new OverviewPage(daemon), Order: 0));
        navigation.RegisterRoute(new NavigationRouteDescriptor("devices", "Devices & Pairing", "\uE8EA", () => new DevicesPairingPage(daemon), Order: 1));
        navigation.RegisterRoute(new NavigationRouteDescriptor("media", "Media & Audio", "\uE8D6", () => new MediaAudioPage(daemon), Order: 2));
        navigation.RegisterRoute(new NavigationRouteDescriptor("offload", "Offload & Eco", "\uE950", () => new OffloadEcoPage(daemon), Order: 3));
        navigation.RegisterRoute(new NavigationRouteDescriptor("deck", "Deck & Rules", "\uE9F5", () => new ControlDeckRulesPage(daemon), Order: 4));
        navigation.RegisterRoute(new NavigationRouteDescriptor("notifications", "Notifications & Dev", "\uEA8F", () => new NotificationsIntegrationsPage(daemon, notifications), Order: 5));
        navigation.RegisterRoute(new NavigationRouteDescriptor("settings", "Diagnostics & Settings", "\uE713", () => new DiagnosticsSettingsPage(daemon, startup, theme, diagnostics, Close), Order: 6, IsFooterItem: true));
    }
}
