using System.Windows.Forms;

namespace Advanced_Combat_Tracker;

public sealed class ActPluginData
{
    public ActPluginData(FileInfo pluginFile, IActPluginV1 pluginObj, TabPage pluginSpace, Label pluginStatus)
    {
        this.pluginFile = pluginFile;
        this.pluginObj = pluginObj;
        tpPluginSpace = pluginSpace;
        lblPluginStatus = pluginStatus;
        lblPluginTitle = new Label { Text = pluginFile.Name };
        cbEnabled = new CheckBox { Checked = true };
    }

    public FileInfo pluginFile;
    public IActPluginV1 pluginObj;
    public TabPage tpPluginSpace;
    public Label lblPluginStatus;
    public Label lblPluginTitle;
    public CheckBox cbEnabled;
}
