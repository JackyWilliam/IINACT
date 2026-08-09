using Mono.Cecil.Cil;

namespace FetchDependencies;

public static class LogFormatIdentity
{
    private const string FormatVersionMethod =
        "System.String FFXIV_ACT_Plugin.Logfile.LogFormat::FormatVersion()";

    public static string ExpectedTemplate(Version pluginVersion)
        => $"This is IINACT {pluginVersion} (API {ApiVersion.IinactApiVersion}) based on FFXIV_ACT_Plugin {{0}}";

    public static string ReadTemplate(string logfileAssemblyPath)
    {
        using var logfile = new TargetAssembly(logfileAssemblyPath);
        var method = logfile.GetMethod(FormatVersionMethod);
        return method.Body.Instructions
                     .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
                     .Select(instruction => instruction.Operand as string)
                     .First(template => template?.StartsWith("This is IINACT ", StringComparison.Ordinal) == true)!;
    }

    public static bool Matches(string logfileAssemblyPath, Version pluginVersion)
    {
        try
        {
            return string.Equals(
                ReadTemplate(logfileAssemblyPath),
                ExpectedTemplate(pluginVersion),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
