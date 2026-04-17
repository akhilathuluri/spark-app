using System.Runtime.InteropServices;
using Spark.Services.Updates;
using Xunit;

namespace Spark.Tests.Unit;

public class UpdateArchitectureServiceTests
{
    [Fact]
    public void ToRuntimeId_ReturnsWinX64_ForX64()
    {
        var rid = UpdateArchitectureService.ToRuntimeId(Architecture.X64);
        Assert.Equal("win-x64", rid);
    }

    [Fact]
    public void ToRuntimeId_ReturnsWinArm64_ForArm64()
    {
        var rid = UpdateArchitectureService.ToRuntimeId(Architecture.Arm64);
        Assert.Equal("win-arm64", rid);
    }

    [Fact]
    public void ToRuntimeId_DefaultsToWinX64_ForOtherArchitectures()
    {
        var ridX86 = UpdateArchitectureService.ToRuntimeId(Architecture.X86);
        var ridArm = UpdateArchitectureService.ToRuntimeId(Architecture.Arm);

        Assert.Equal("win-x64", ridX86);
        Assert.Equal("win-x64", ridArm);
    }
}
