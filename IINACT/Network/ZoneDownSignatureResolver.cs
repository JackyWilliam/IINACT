namespace IINACT.Network;

internal static class ZoneDownSignatureResolver
{
    internal static nint Resolve(string version, Func<string, int, nint[]> scan)
    {
        // IINACT 2.10.3.10 and Unscrambler's 7.57 capture hook publish different
        // compiler variants of the same receive call sites. Keep both exact patterns;
        // older clients must retain their old signature instead of matching a broad wildcard.
        string[] signatures = version == "2026.10.06.0000.0000"
            ? ["E8 ?? ?? ?? ?? 4C 8B 4E 10 8B 46 1C 45",
               "E8 ?? ?? ?? ?? 4C 8B 47 10 44 8B 4F 1C 45"]
            : ["E8 ?? ?? ?? ?? 4C 8B 4F 10 8B 47 1C 45"];

        foreach (var signature in signatures)
        {
            try
            {
                // Both upstream hooks select the third receive call. Never accept a
                // partial match: the first two calls belong to other network channels.
                return scan(signature, 3)[2];
            }
            catch (KeyNotFoundException)
            {
                // Try only the other published variant; unrelated scanner errors propagate.
            }
        }

        throw new KeyNotFoundException($"No supported zone receive signature for game {version}.");
    }
}
