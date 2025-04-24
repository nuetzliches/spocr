namespace SpocR.Enums;

public enum TargetFrameworkEnum
{
    NetCoreApp22,
    Net60,
    Net80,
    Net90
}

public static class TargetFrameworkExtensions
{
    public static string ToDefaultTargetFramework(this TargetFrameworkEnum framework)
    {
        return Constants.DefaultTargetFramework.ToFrameworkString();
    }

    public static string ToFrameworkString(this TargetFrameworkEnum framework)
    {
        return framework switch
        {
            TargetFrameworkEnum.NetCoreApp22 => "netcoreapp2.2",
            TargetFrameworkEnum.Net60 => "net6.0",
            TargetFrameworkEnum.Net80 => "net8.0",
            TargetFrameworkEnum.Net90 => "net9.0",
            _ => framework.ToDefaultTargetFramework()
        };
    }

    public static TargetFrameworkEnum FromString(string frameworkString)
    {
        if (string.IsNullOrEmpty(frameworkString))
            return Constants.DefaultTargetFramework;

        return frameworkString.ToLowerInvariant() switch
        {
            "netcoreapp2.2" => TargetFrameworkEnum.NetCoreApp22,
            "net6.0" => TargetFrameworkEnum.Net60,
            "net8.0" => TargetFrameworkEnum.Net80,
            "net9.0" => TargetFrameworkEnum.Net90,
            _ => Constants.DefaultTargetFramework
        };
    }
}