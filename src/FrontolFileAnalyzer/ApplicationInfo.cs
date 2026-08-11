using System.Reflection;

namespace FrontolFileAnalyzer;

public static class ApplicationInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    public static string VersionLabel => $"Версия {Version}";
}
