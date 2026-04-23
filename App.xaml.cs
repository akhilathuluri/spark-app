using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spark.Models;
using Spark.Services;
using Spark.Windows;

namespace Spark;

public partial class App : Application
{
    private TrayService? _trayService;
    private GlobalHotkeyService? _hotkeyService;
    private TextCaptureService? _textCapture;
    private SettingsService? _settingsService;
    private ActionsService? _actionsService;
    private AiService? _aiService;
    private InternetSearchService? _internetSearchService;
    private HistoryService? _historyService;
    private UsageMetricsService? _usageMetricsService;
    private AnalyticsService? _analyticsService;
    private DiagnosticsService? _diagnosticsService;
    private TextSanitizationService? _textSanitizationService;
    private DeveloperNotificationService? _developerNotificationService;
    private UpdateNotificationService? _updateService;
    private OnboardingStateService? _onboardingStateService;
    private System.Threading.Mutex? _mutex; // kept alive for the process lifetime

    // New diagnostic and resilience services (issues #1–5)
    private HotkeyThrottleService? _hotkeyThrottle;
    private SettingsBackupService? _settingsBackup;
    private TextCaptureDiagnosticsService? _captureDiagnostics;
    private CustomActionValidationService? _actionValidator;

    // Hotkey debounce: prevent rapid repeated presses from spawning multiple windows.
    // volatile: ensures the flag is read from main memory, not a CPU register cache,
    // guarding against stale reads if an async continuation ever resumes on a different thread.
    private volatile bool _hotkeyProcessing = false;
    private DateTime _lastNoTextCapturedNoticeUtc = DateTime.MinValue;

    // Hidden message-only window to receive hotkey messages
    private HotkeyWindow? _hotkeyWindow;

    // Pre-created windows (hidden until needed)
    private ActionPanelWindow? _actionPanel;
    private ResultWindow? _resultWindow;
    private InternetSearchResultWindow? _internetSearchResultWindow;
    private HistoryWindow? _historyWindow;
    private MoreInfoWindow? _moreInfoWindow;
    private UpdatesWindow? _updatesWindow;
    private DeveloperNotificationsWindow? _developerNotificationsWindow;
    private UsageDashboardWindow? _usageDashboardWindow;
    private OnboardingWizardWindow? _onboardingWindow;
    private SettingsWindow? _settingsWindow;
    private IReadOnlyList<DeveloperNotification> _latestDeveloperNotifications = Array.Empty<DeveloperNotification>();
    private AppUpdateInfo? _latestUpdateInfo;
    private string? _lastNotifiedDeveloperNotificationId;
    private string? _lastNotifiedUpdateVersion;
    private System.Windows.Threading.DispatcherTimer? _updateCheckTimer;
    private System.Windows.Threading.DispatcherTimer? _developerNotificationsTimer;

    private static ImageSource? _cachedAppIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Enforce single instance — stored as a field so GC never collects it while the app runs
        _mutex = new System.Threading.Mutex(true, "Spark_SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("Spark is already running. Look for the icon in the system tray.",
                "Spark", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Use WinForms message loop for NotifyIcon
        System.Windows.Forms.Application.EnableVisualStyles();

        // Services
        _settingsService = new SettingsService();
        _actionsService = new ActionsService(_settingsService);
        _ = _actionsService.GetAllActions(); // warm cache for first hotkey press
        _aiService = new AiService(_settingsService);
        _internetSearchService = new InternetSearchService(_settingsService);
        _textCapture = new TextCaptureService();
        _historyService = new HistoryService();
        _usageMetricsService = new UsageMetricsService();
        _developerNotificationService = new DeveloperNotificationService();
        _updateService = new UpdateNotificationService();
        _onboardingStateService = new OnboardingStateService();

        // New diagnostic and resilience services (issues #1–5)
        _hotkeyThrottle = new HotkeyThrottleService();
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spark", "settings.json");
        _settingsBackup = new SettingsBackupService(settingsPath);
        _settingsService.SetBackupService(_settingsBackup);  // Attach to auto-backup on save
        _captureDiagnostics = new TextCaptureDiagnosticsService();
        _actionValidator = new CustomActionValidationService();
        _textSanitizationService = new TextSanitizationService();

        // Analytics — developer-only, silently disabled if API key is not set
        _analyticsService = new AnalyticsService();
        _analyticsService.TrackAppStarted();
        _diagnosticsService = new DiagnosticsService(_settingsService, _analyticsService);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Tray
        _trayService = new TrayService();
        _trayService.OpenSettingsRequested += OpenSettings;
        _trayService.OpenHistoryRequested  += OpenHistory;
        _trayService.OpenUsageDashboardRequested += OpenUsageDashboard;
        _trayService.OpenDeveloperNotificationsRequested += OpenDeveloperNotifications;
        _trayService.OpenUpdatesRequested += OpenUpdates;
        _trayService.OpenMoreInfoRequested += OpenMoreInfo;
        _trayService.ExitRequested += () => Shutdown();
        _trayService.Initialize();

        // Helper window for hotkey registration (not shown, not in taskbar)
        _hotkeyWindow = new HotkeyWindow();
        ApplyWindowIcon(_hotkeyWindow);
        _hotkeyWindow.Show();
        _hotkeyWindow.Hide();

        // Hotkey
        _hotkeyService = new GlobalHotkeyService();
        _hotkeyService.Initialize(_hotkeyWindow);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        var settings = _settingsService.Load();
        bool registered = _hotkeyService.Register(settings.HotkeyModifiers, settings.HotkeyKey);

        if (!registered)
            _trayService.ShowBalloon("Spark — Hotkey conflict",
                $"Could not register {settings.HotkeyModifiers}+{settings.HotkeyKey}. " +
                "Another app may be using it. Open Settings to choose a different shortcut.",
                System.Windows.Forms.ToolTipIcon.Warning);

        // Apply startup-on-login preference
        StartupService.Apply(settings.StartWithWindows);

        // Pre-create windows (hidden) — reused on every hotkey press
        _resultWindow = new ResultWindow(_aiService, _textCapture, _settingsService, _historyService!, _usageMetricsService!, _analyticsService!, _textSanitizationService, _diagnosticsService, _internetSearchService);
        _internetSearchResultWindow = new InternetSearchResultWindow(_internetSearchService!, _historyService!, _usageMetricsService!, _analyticsService!, _diagnosticsService);
        _actionPanel  = new ActionPanelWindow(_actionsService, _aiService, _textCapture, _settingsService, _resultWindow, _internetSearchService, _internetSearchResultWindow);
        _historyWindow = new HistoryWindow(_historyService!);
        _moreInfoWindow = new MoreInfoWindow(DeveloperInfo.Default);
        _updatesWindow = new UpdatesWindow();
        _developerNotificationsWindow = new DeveloperNotificationsWindow();
        _usageDashboardWindow = new UsageDashboardWindow(_usageMetricsService!);

        ApplyWindowIcon(_resultWindow);
        ApplyWindowIcon(_actionPanel);
        ApplyWindowIcon(_internetSearchResultWindow);
        ApplyWindowIcon(_historyWindow);
        ApplyWindowIcon(_moreInfoWindow);
        ApplyWindowIcon(_updatesWindow);
        ApplyWindowIcon(_developerNotificationsWindow);
        ApplyWindowIcon(_usageDashboardWindow);

        _updatesWindow.RefreshRequested += OnRefreshUpdatesRequested;
        _developerNotificationsWindow.RefreshRequested += OnRefreshDeveloperNotificationsRequested;

        // Show balloon on first start or if no API key set
        if (string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey) && string.IsNullOrWhiteSpace(settings.Groq.ApiKey))
        {
            _trayService.ShowBalloon("Spark is running",
                "Open Settings to add your AI API key. Then select text and press Ctrl+Shift+Space.");
        }
        else
        {
            _trayService.ShowBalloon("Spark is running",
                $"Select text anywhere and press {settings.HotkeyModifiers}+{settings.HotkeyKey}");
        }

        if (!string.IsNullOrWhiteSpace(_settingsService.LastRecoveryMessage))
        {
            _trayService.ShowBalloon("Spark settings recovered", _settingsService.LastRecoveryMessage!);
        }

        var skipOnboarding = string.Equals(
            Environment.GetEnvironmentVariable("Spark_SKIP_ONBOARDING"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (!skipOnboarding && _onboardingStateService?.ShouldShowOnboarding(settings) == true)
        {
            _hotkeyService?.SetSuspended(true);
            _onboardingWindow = new OnboardingWizardWindow(
                _settingsService,
                _textCapture,
                _aiService,
                CompleteOnboarding);
            ApplyWindowIcon(_onboardingWindow);
            _onboardingWindow.Closed += (_, _) =>
            {
                _hotkeyService?.SetSuspended(false);
                _onboardingWindow = null;
            };
            _onboardingWindow.Show();
            _onboardingWindow.Activate();

            // Safety watchdog: if the onboarding window is killed by the OS (crash, task-manager,
            // etc.) without firing its Closed event, the hotkey would stay suspended forever.
            // This timer fires once after 5 minutes and re-enables the hotkey regardless.
            // Non-breaking: if onboarding completes normally, the Closed handler already called
            // SetSuspended(false) and _onboardingWindow is null, so the watchdog is a no-op.
            var watchdog = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            watchdog.Tick += (_, _) =>
            {
                watchdog.Stop();
                if (_onboardingWindow != null)
                {
                    DebugLogService.LogMessage("Onboarding watchdog: window not closed after 5 min — re-enabling hotkey.");
                    _hotkeyService?.SetSuspended(false);
                    // Do NOT null _onboardingWindow here; the window itself may still be open.
                }
            };
            watchdog.Start();
        }


        _ = CheckForUpdatesAsync(showUpToDateBalloon: false);
        StartUpdateCheckTimer();

        _ = CheckDeveloperNotificationsAsync(forceBalloon: false);
        StartDeveloperNotificationsTimer();
    }

    private void StartUpdateCheckTimer()
    {
        _updateCheckTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromHours(6)
        };

        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync(showUpToDateBalloon: false);
        _updateCheckTimer.Start();
    }

    private void StartDeveloperNotificationsTimer()
    {
        _developerNotificationsTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };

        _developerNotificationsTimer.Tick += async (_, _) => await CheckDeveloperNotificationsAsync(forceBalloon: false);
        _developerNotificationsTimer.Start();
    }

    private async Task CheckForUpdatesAsync(bool showUpToDateBalloon)
    {
        if (_updateService is null || _trayService is null)
            return;

        var info = await _updateService.CheckForUpdateAsync();
        _latestUpdateInfo = info;
        _updatesWindow?.SetUpdateInfo(info);

        if (info is null)
            return;

        if (info.IsUpdateAvailable)
        {
            _trayService.SetUpdatesAvailable(info.LatestVersion, info.IsRequiredUpdate);

            if (!string.Equals(_lastNotifiedUpdateVersion, info.LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                _lastNotifiedUpdateVersion = info.LatestVersion;
                _trayService.ShowBalloon(
                    "Spark update available",
                    $"Version {info.LatestVersion} is available. Right-click tray icon and open Updates.");
            }
        }
        else
        {
            _trayService.ClearUpdatesAvailable();

            if (showUpToDateBalloon)
            {
                _trayService.ShowBalloon("Spark", "You are already on the latest version.");
            }
        }
    }

    private async void OnRefreshUpdatesRequested()
    {
        if (_updatesWindow is null)
            return;

        _updatesWindow.SetCheckingState();
        await CheckForUpdatesAsync(showUpToDateBalloon: true);
    }

    private async Task CheckDeveloperNotificationsAsync(bool forceBalloon)
    {
        if (_developerNotificationService is null || _trayService is null)
            return;

        var notifications = await _developerNotificationService.GetActiveNotificationsAsync();
        _latestDeveloperNotifications = notifications;

        _developerNotificationsWindow?.SetNotifications(notifications);
        _trayService.SetDeveloperNotificationsAvailable(notifications.Count);

        if (notifications.Count == 0)
        {
            if (forceBalloon)
                _trayService.ShowBalloon("Spark", "No active developer notifications.");
            return;
        }

        var newestByTime = notifications.OrderByDescending(n => n.CreatedAtLocal).First();
        if (forceBalloon || !string.Equals(_lastNotifiedDeveloperNotificationId, newestByTime.Id, StringComparison.Ordinal))
        {
            _lastNotifiedDeveloperNotificationId = newestByTime.Id;
            _trayService.ShowBalloon("Spark developer notification", newestByTime.Title, System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private async void OnRefreshDeveloperNotificationsRequested()
    {
        if (_developerNotificationsWindow is null)
            return;

        _developerNotificationsWindow.SetCheckingState();
        await CheckDeveloperNotificationsAsync(forceBalloon: true);
    }

    private async void OnHotkeyPressed()
    {
        // Throttle: prevent rapid repeated hotkey presses (issue #2: no rate limiting)
        // Use HotkeyThrottleService for configurable debounce + diagnostics
        if (_hotkeyThrottle != null && !_hotkeyThrottle.ShouldProcess())
        {
            return;  // Suppressed by rate limiter
        }

        // Debounce: prevent rapid repeated hotkey presses from spawning multiple windows
        if (_hotkeyProcessing) return;
        _hotkeyProcessing = true;

        try
        {
            if (_textCapture == null || _actionPanel == null) return;

            var cursorPos = _textCapture.GetCursorPosition();
            var selectedText = await _textCapture.GetSelectedTextAsync();

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                // Record failure for diagnostics (issue #1: text capture fails in some apps)
                if (_captureDiagnostics != null)
                {
                    _captureDiagnostics.RecordFailure(_textCapture!.LastDiagnostics, _textCapture!.LastSelectionContext);
                }

                if (_diagnosticsService != null)
                {
                    var captureCid = _diagnosticsService.NewCorrelationId();
                    _diagnosticsService.ReportCaptureFailure(
                        "OnHotkeyPressed.Capture",
                        captureCid,
                        _textCapture!.LastDiagnostics,
                        _textCapture!.LastSelectionContext);
                }

                // Avoid silent no-op behavior; show a throttled hint to the user.
                var now = DateTime.UtcNow;
                if ((now - _lastNoTextCapturedNoticeUtc).TotalSeconds > 5)
                {
                    _lastNoTextCapturedNoticeUtc = now;
                    var details = _captureDiagnostics != null
                        ? _captureDiagnostics.GetDetailedDiagnostics()
                        : _textCapture.GetFailureDiagnosticSummary();
                    _trayService?.ShowBalloon(
                        "Spark",
                        $"Hotkey detected, but no text was captured. Select text and try again. {details}",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
                return;
            }

            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (_actionPanel.IsVisible)
                {
                    _actionPanel.Hide();
                    return;
                }
                _actionPanel.ShowWithText(selectedText, cursorPos, _textCapture.LastSelectionContext?.ProcessName);
            }));
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("OnHotkeyPressed", ex);
            var cid = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
            _diagnosticsService?.ReportHandledError("OnHotkeyPressed", ex, cid);
        }
        finally
        {
            _hotkeyProcessing = false;
        }
    }

    private void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _hotkeyService?.SetSuspended(true);

            var win = new SettingsWindow(_settingsService!, settings =>
            {
                // Invalidate cached actions so new custom actions are picked up immediately
                _actionsService?.InvalidateCache();

                // Re-register hotkey on save; warn if the new combo is already taken
                bool ok = _hotkeyService?.Register(settings.HotkeyModifiers, settings.HotkeyKey) ?? false;
                if (!ok)
                    _trayService?.ShowBalloon("Spark — Hotkey conflict",
                        $"Could not register {settings.HotkeyModifiers}+{settings.HotkeyKey}. " +
                        "Another app may be using it. Try a different shortcut.",
                        System.Windows.Forms.ToolTipIcon.Warning);
            });

            _settingsWindow = win;
            ApplyWindowIcon(win);
            win.Closed += (_, _) =>
            {
                _settingsWindow = null;
                _hotkeyService?.SetSuspended(false);
            };

            win.Show();
            win.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Ensure history is persisted before exit
        _historyService?.PersistSync();

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_updatesWindow is not null)
            _updatesWindow.RefreshRequested -= OnRefreshUpdatesRequested;

        if (_developerNotificationsWindow is not null)
            _developerNotificationsWindow.RefreshRequested -= OnRefreshDeveloperNotificationsRequested;

        _updateCheckTimer?.Stop();
        _developerNotificationsTimer?.Stop();

        CloseWindow(_actionPanel);
        CloseWindow(_resultWindow);
        CloseWindow(_internetSearchResultWindow);
        CloseWindow(_historyWindow);
        CloseWindow(_moreInfoWindow);
        CloseWindow(_updatesWindow);
        CloseWindow(_developerNotificationsWindow);
        CloseWindow(_usageDashboardWindow);
        CloseWindow(_onboardingWindow);
        CloseWindow(_hotkeyWindow);

        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _aiService?.Dispose();
        _internetSearchService?.Dispose();
        _analyticsService?.Dispose();
        _developerNotificationService?.Dispose();
        _updateService?.Dispose();

        _textCapture = null;
        _actionsService = null;
        _settingsService = null;

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void CloseWindow(Window? window)
    {
        if (window is null)
            return;

        if (window.IsVisible)
            window.Close();
    }

    private static void ApplyWindowIcon(Window? window)
    {
        if (window == null)
            return;

        var icon = TryGetAppIcon();
        if (icon == null)
            return;

        window.Icon = icon;
    }

    private static ImageSource? TryGetAppIcon()
    {
        if (_cachedAppIcon != null)
            return _cachedAppIcon;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "ico", "App.ico");
            if (!File.Exists(iconPath))
                return null;

            _cachedAppIcon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
            return _cachedAppIcon;
        }
        catch
        {
            return null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var cid = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
        _diagnosticsService?.ReportCrash("DispatcherUnhandledException", e.Exception, cid);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
        var cid = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
        _diagnosticsService?.ReportCrash("AppDomainUnhandledException", exception, cid);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var cid = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
        _diagnosticsService?.ReportCrash("TaskSchedulerUnobservedTaskException", e.Exception, cid);
    }

    private void OpenHistory()
    {
        Dispatcher.Invoke(() =>
        {
            if (_historyWindow!.IsVisible)
            {
                _historyWindow.Activate();
                return;
            }
            _historyWindow.ShowPanel();
        });
    }

    private void OpenMoreInfo()
    {
        Dispatcher.Invoke(() =>
        {
            if (_moreInfoWindow!.IsVisible)
            {
                _moreInfoWindow.Activate();
                return;
            }

            _moreInfoWindow.ShowPanel();
        });
    }

    private void OpenUpdates()
    {
        Dispatcher.Invoke(() =>
        {
            if (_updatesWindow is null)
                return;

            if (_updatesWindow.IsVisible)
            {
                _updatesWindow.Activate();
                return;
            }

            if (_latestUpdateInfo is null)
                _updatesWindow.SetCheckingState();
            else
                _updatesWindow.SetUpdateInfo(_latestUpdateInfo);

            _updatesWindow.ShowPanel();

            if (_latestUpdateInfo is null)
                _ = CheckForUpdatesAsync(showUpToDateBalloon: false);
        });
    }

    private void OpenDeveloperNotifications()
    {
        Dispatcher.Invoke(() =>
        {
            if (_developerNotificationsWindow is null)
                return;

            if (_developerNotificationsWindow.IsVisible)
            {
                _developerNotificationsWindow.Activate();
                return;
            }

            if (_latestDeveloperNotifications.Count == 0)
                _developerNotificationsWindow.SetCheckingState();
            else
                _developerNotificationsWindow.SetNotifications(_latestDeveloperNotifications);

            _developerNotificationsWindow.ShowPanel();

            if (_latestDeveloperNotifications.Count == 0)
                _ = CheckDeveloperNotificationsAsync(forceBalloon: false);
        });
    }

    private void OpenUsageDashboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_usageDashboardWindow is null)
                return;

            if (_usageDashboardWindow.IsVisible)
            {
                _usageDashboardWindow.Activate();
                return;
            }

            _usageDashboardWindow.ShowPanel();
        });
    }

    private void CompleteOnboarding()
    {
        if (_settingsService is null || _onboardingStateService is null)
            return;

        _onboardingStateService.MarkCompleted(_settingsService);
    }
}


