using System.Drawing;
using System.Windows.Forms;

namespace Spark.Services;

/// <summary>
/// Manages the system tray icon and its context menu.
/// Uses Windows Forms NotifyIcon for native tray support.
/// </summary>
public class TrayService : IDisposable
{
    private readonly IconService _iconService = new();
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _updatesItem;

    public event Action? OpenSettingsRequested;
    public event Action? OpenHistoryRequested;
    public event Action? OpenUsageDashboardRequested;
    public event Action? OpenUpdatesRequested;
    public event Action? OpenMoreInfoRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.BackColor = Color.FromArgb(22, 22, 30);
        _contextMenu.ForeColor = Color.FromArgb(224, 224, 240);
        _contextMenu.Font = new Font("Segoe UI", 9.5f);
        _contextMenu.RenderMode = ToolStripRenderMode.System;

        var titleItem = new ToolStripMenuItem("Spark")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold)
        };
        _contextMenu.Items.Add(titleItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        var historyItem = new ToolStripMenuItem("History")
        {
            Image = _iconService.CreateMenuImage(IconKey.History)
        };
        historyItem.Click += (_, _) => OpenHistoryRequested?.Invoke();
        _contextMenu.Items.Add(historyItem);

        var settingsItem = new ToolStripMenuItem("Settings")
        {
            Image = _iconService.CreateMenuImage(IconKey.Settings)
        };
        settingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke();
        _contextMenu.Items.Add(settingsItem);

        var usageItem = new ToolStripMenuItem("Usage Dashboard")
        {
            Image = _iconService.CreateMenuImage(IconKey.UsageDashboard)
        };
        usageItem.Click += (_, _) => OpenUsageDashboardRequested?.Invoke();
        _contextMenu.Items.Add(usageItem);

        _updatesItem = new ToolStripMenuItem("Updates Available")
        {
            Image = _iconService.CreateMenuImage(IconKey.Updates),
            Visible = false
        };
        _updatesItem.Click += (_, _) => OpenUpdatesRequested?.Invoke();
        _contextMenu.Items.Add(_updatesItem);

        var moreInfoItem = new ToolStripMenuItem("More Info")
        {
            Image = _iconService.CreateMenuImage(IconKey.MoreInfo)
        };
        moreInfoItem.Click += (_, _) => OpenMoreInfoRequested?.Invoke();
        _contextMenu.Items.Add(moreInfoItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit")
        {
            Image = _iconService.CreateMenuImage(IconKey.Exit)
        };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Spark — Select text and press Ctrl+Shift+Space",
            Visible = true,
            ContextMenuStrip = _contextMenu,
            Icon = _iconService.CreateAppTrayIcon()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    public void SetUpdatesAvailable(string latestVersion, bool isRequired)
    {
        if (_updatesItem is null)
            return;

        var requiredSuffix = isRequired ? " - Required" : string.Empty;
        _updatesItem.Text = $"Updates Available ({latestVersion}){requiredSuffix}";
        _updatesItem.Visible = true;
    }

    public void ClearUpdatesAvailable()
    {
        if (_updatesItem is null)
            return;

        _updatesItem.Visible = false;
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _contextMenu?.Dispose();
    }
}
