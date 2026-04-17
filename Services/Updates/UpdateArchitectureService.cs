using System.Runtime.InteropServices;

namespace Spark.Services.Updates;

public sealed class UpdateArchitectureService
{
    public string GetPreferredRuntimeId()
    {
        var processArch = RuntimeInformation.ProcessArchitecture;
        if (processArch == Architecture.X64 || processArch == Architecture.Arm64)
            return ToRuntimeId(processArch);

        var osArch = RuntimeInformation.OSArchitecture;
        return ToRuntimeId(osArch);
    }

    public string GetProcessArchitectureLabel() => RuntimeInformation.ProcessArchitecture.ToString();

    public string GetOsArchitectureLabel() => RuntimeInformation.OSArchitecture.ToString();

    public static string ToRuntimeId(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
    }
}
