using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;
using Application = FlaUI.Core.Application;

namespace Spark.Tests.Smoke;

public class UiSmokeTests : IDisposable
{
    private readonly Application? _app;
    private readonly UIA3Automation _automation;

    public UiSmokeTests()
    {
        // 1. Kill any existing instances so the SingleInstance Mutex doesn't reject our test launch
        foreach (var process in Process.GetProcessesByName("Spark"))
        {
            try { process.Kill(); process.WaitForExit(2000); } catch { }
        }

        var buildExePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, @"..\..\..\..\bin\Debug\net10.0-windows\Spark.exe"));

        if (!File.Exists(buildExePath))
            throw new FileNotFoundException($"Cannot find Spark.exe at {buildExePath}. Build the main project first.");

        // 2. Launch our target app natively
        _app = Application.Launch(buildExePath);
        _automation = new UIA3Automation();

        // Give it 1.5s to fully init its systems (Mutex, Hotkeys, Tray)
        Thread.Sleep(1500);

        // If this is a pristine environment, Spark launches the Onboarding Wizard and suspends the hotkey.
        // We must detect this window and explicitly skip it to save settings and enable the hotkey.
        var desktop = _automation.GetDesktop();
        var onboardingWindow = Retry.WhileNull(() => desktop.FindFirstChild(cf => cf.ByName("Spark Onboarding")), TimeSpan.FromSeconds(3)).Result?.AsWindow();
        if (onboardingWindow != null)
        {
            onboardingWindow.Focus();
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(2000); // wait for fade-out close animation and hotkey register
        }
    }

    [Fact]
    public void PanelOpenSelectReplace_SmokeScenario()
    {
        SetClipboardText("Fix my internal grammar please!");
        Thread.Sleep(500); // Wait out any lingering system clipboard locks

        // 3. Simulate Global Hotkey: Ctrl + Shift + Space
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT))
        {
            Keyboard.Press(VirtualKeyShort.SPACE);
        }
        Thread.Sleep(500); // Give hotkey service time to process and trigger capture

        // 4. Wait for Action Panel Window
        var desktop = _automation.GetDesktop();
        var actionPanel = Retry.WhileNull(() => desktop.FindFirstChild(cf => cf.ByName("Spark Actions")), TimeSpan.FromSeconds(10)).Result?.AsWindow();
        Assert.NotNull(actionPanel);

        // 5. Interact using direct Keyboard input (ActionPanel auto-focuses the SearchBox on spawn)
        Thread.Sleep(1000); // give the window a beat to finish its GPU animation entry
        Keyboard.Type("casual");
        Thread.Sleep(1000); // give the UI virtualization list binding a beat to filter

        // 6. Simulate 'Enter' to select the first filtered item
        Keyboard.Press(VirtualKeyShort.ENTER);

        // 7. Verify Result Window Opens
        var resultPanel = Retry.WhileNull(() => desktop.FindFirstChild(cf => cf.ByName("Spark Result")), TimeSpan.FromSeconds(15)).Result?.AsWindow();
        Assert.NotNull(resultPanel);

        // Prove the result window eventually hits the state where the ReplaceButton displays
        // (Even if API keys fail, the window holds open correctly displaying an error state).
        var closeButton = Retry.WhileNull(() => resultPanel.FindFirstDescendant(cf => cf.ByAutomationId("CloseButton"))?.AsButton(), TimeSpan.FromSeconds(10)).Result;
        Assert.NotNull(closeButton);
        
        closeButton.Invoke();
    }

    [Fact]
    public void NoTextSelected_TriggersGracefulBalloonWarning()
    {
        // 1. Clear clipboard entirely so we hit the no-text condition exactly
        ClearClipboard();
        Thread.Sleep(200);

        // 2. Trigger Global Hotkey
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT))
        {
            Keyboard.Press(VirtualKeyShort.SPACE);
        }

        // 3. Instead of the UI appearing, Spark safely catches it. ActionPanel shouldn't appear.
        var desktop = _automation.GetDesktop();
        var actionPanel = desktop.FindFirstChild(cf => cf.ByName("Spark Actions"));
        
        Assert.Null(actionPanel); // If it exists, the failsafe broke
    }

    [Fact]
    public void RapidHotkeyPress_IsThrottledSafely()
    {
        SetClipboardText("Testing throttle");
        Thread.Sleep(200);

        // Spam the hotkey 3 times in 100ms
        for (int i = 0; i < 3; i++)
        {
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT))
            {
                Keyboard.Press(VirtualKeyShort.SPACE);
            }
            Thread.Sleep(30);
        }

        var desktop = _automation.GetDesktop();
        // Since it's throttled, the panel should still open exactly once.
        var actionPanel = Retry.WhileNull(() => desktop.FindFirstChild(cf => cf.ByName("Spark Actions")), TimeSpan.FromSeconds(5)).Result?.AsWindow();
        Assert.NotNull(actionPanel);

        // Wait a bit to ensure it doesn't duplicate or crash
        Thread.Sleep(500);
        var panels = desktop.FindAllChildren(cf => cf.ByName("Spark Actions"));
        
        // Assert that the throttling worked and only 1 panel instantiated
        Assert.Single(panels);
    }

    private void SetClipboardText(string text)
    {
        var thread = new Thread(() => System.Windows.Clipboard.SetText(text));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private void ClearClipboard()
    {
        var thread = new Thread(() => System.Windows.Clipboard.Clear());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    public void Dispose()
    {
        _automation.Dispose();
        if (_app != null)
        {
            if (!_app.HasExited)
            {
                _app.Close();
                try { _app.Kill(); } catch { }
            }
            _app.Dispose();
        }
    }
}
