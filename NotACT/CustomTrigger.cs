namespace Advanced_Combat_Tracker;

/// <summary>
/// ACT-compatible custom trigger data exposed to legacy plugins.
/// </summary>
public class CustomTrigger
{
    public bool Active { get; set; }

    public bool RestrictToCategoryZone { get; set; }

    public bool Tabbed { get; set; }

    public bool Timer { get; set; }

    public int SoundType { get; set; }

    public string Category { get; set; } = string.Empty;

    public string ShortRegexString { get; set; } = string.Empty;

    public string SoundData { get; set; } = string.Empty;

    public string TimerName { get; set; } = string.Empty;
}
