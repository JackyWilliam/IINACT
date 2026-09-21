namespace RainbowMage.OverlayPlugin.EventSources;

// Embedded browsers need a picker owned by their own UI thread. Standalone IINACT
// can keep using its drawn ImGui picker when no host implementation is registered.
public interface ICactbotDirectoryPicker
{
    string ChooseDirectory(string currentDirectory);
}
