using Spark.Services;
using Xunit;

namespace Spark.Tests.Unit;

public class TextCaptureServiceTests
{
    [Fact]
    public void GetCaptureStrategyPlanForDiagnostics_TerminalPrefersCtrlInsert()
    {
        var plan = TextCaptureService.GetCaptureStrategyPlanForDiagnostics("Terminal");

        Assert.Equal("Ctrl+Insert", plan[0]);
        Assert.Contains("Ctrl+C", plan);
    }

    [Fact]
    public void GetCaptureStrategyPlanForDiagnostics_BrowserRetriesCtrlCBeforeCtrlInsert()
    {
        var plan = TextCaptureService.GetCaptureStrategyPlanForDiagnostics("Browser");

        Assert.Equal("Ctrl+C", plan[0]);
        Assert.Equal("Ctrl+C (delayed retry)", plan[1]);
        Assert.Contains("Ctrl+Insert", plan);
    }

    [Fact]
    public void GetCaptureStrategyPlanForDiagnostics_RemoteDesktopUsesTerminalStylePlan()
    {
        var plan = TextCaptureService.GetCaptureStrategyPlanForDiagnostics("RemoteDesktop");

        Assert.Equal("Ctrl+Insert", plan[0]);
        Assert.Contains("WM_COPY", plan);
    }
}
