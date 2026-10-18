using System.Reflection;

namespace TrilhaEstudos.App;

public static class AppVersion
{
    public static string Semantic => typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static Version Number => Version.Parse(Semantic);
    public static string Display => Number.Build == 0 ? $"{Number.Major}.{Number.Minor}" : Number.ToString(3);
    public static string RepositoryUrl => typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepositoryUrl")?.Value ?? "";
}
