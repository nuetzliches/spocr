namespace SpocR.Utils;

internal static class CacheControl
{
    // Set to true when --no-cache flag is used on a command invocation.
    public static bool ForceReload { get; set; }
}
