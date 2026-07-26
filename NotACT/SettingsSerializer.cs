using System.Globalization;
using System.Reflection;
using System.Xml;

namespace Advanced_Combat_Tracker;

/// <summary>
/// Compatibility implementation of ACT's simple plugin settings serializer.
/// </summary>
public class SettingsSerializer : IDisposable
{
    private readonly object owner;
    private readonly Dictionary<string, Control> controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> values = new(StringComparer.Ordinal);

    public SettingsSerializer(object owner)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public void AddControlSetting(string name, Control control) => controls[name] = control;

    public void RemoveControlSetting(string name) => controls.Remove(name);

    public void AddBooleanSetting(string name) => values[name] = typeof(bool);

    public void AddIntSetting(string name) => values[name] = typeof(int);

    public void AddLongSetting(string name) => values[name] = typeof(long);

    public void AddStringSetting(string name) => values[name] = typeof(string);

    public void AddDirectoryInfoSetting(string name) => values[name] = typeof(DirectoryInfo);

    public void ExportToXml(XmlTextWriter writer)
    {
        foreach (var pair in values)
        {
            writer.WriteElementString(pair.Key, ConvertToString(GetMemberValue(pair.Key)));
        }

        foreach (var pair in controls)
        {
            writer.WriteElementString(pair.Key, ReadControl(pair.Value));
        }
    }

    public int ImportFromXml(XmlTextReader reader)
    {
        var depth = reader.Depth;
        var imported = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
            {
                continue;
            }

            var name = reader.LocalName;
            var text = reader.ReadElementContentAsString();
            if (values.TryGetValue(name, out var type))
            {
                SetMemberValue(name, ConvertFromString(text, type));
                imported++;
            }
            else if (controls.TryGetValue(name, out var control))
            {
                WriteControl(control, text);
                imported++;
            }
        }

        return imported;
    }

    public void FinializeComboBoxes()
    {
    }

    public void Dispose()
    {
        controls.Clear();
        values.Clear();
    }

    private object? GetMemberValue(string name)
    {
        var property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is not null)
        {
            return property.GetValue(owner);
        }

        return owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(owner);
    }

    private void SetMemberValue(string name, object? value)
    {
        var property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true)
        {
            property.SetValue(owner, value);
            return;
        }

        owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(owner, value);
    }

    private static string ConvertToString(object? value)
        => value switch
        {
            null => string.Empty,
            DirectoryInfo directory => directory.FullName,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static object? ConvertFromString(string value, Type type)
    {
        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(DirectoryInfo))
        {
            return new DirectoryInfo(value);
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, value, ignoreCase: true);
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private static string ReadControl(Control control)
        => control switch
        {
            CheckBox checkBox => checkBox.Checked.ToString(CultureInfo.InvariantCulture),
            RadioButton radioButton => radioButton.Checked.ToString(CultureInfo.InvariantCulture),
            NumericUpDown numeric => numeric.Value.ToString(CultureInfo.InvariantCulture),
            TrackBar trackBar => trackBar.Value.ToString(CultureInfo.InvariantCulture),
            ComboBox comboBox => comboBox.Text,
            TextBox textBox => textBox.Text,
            _ => control.Text,
        };

    private static void WriteControl(Control control, string value)
    {
        switch (control)
        {
            case CheckBox checkBox when bool.TryParse(value, out var parsed):
                checkBox.Checked = parsed;
                break;
            case RadioButton radioButton when bool.TryParse(value, out var parsed):
                radioButton.Checked = parsed;
                break;
            case NumericUpDown numeric when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                numeric.Value = Math.Clamp(parsed, numeric.Minimum, numeric.Maximum);
                break;
            case TrackBar trackBar when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                trackBar.Value = Math.Clamp(parsed, trackBar.Minimum, trackBar.Maximum);
                break;
            case ComboBox comboBox:
                comboBox.Text = value;
                break;
            case TextBox textBox:
                textBox.Text = value;
                break;
            default:
                control.Text = value;
                break;
        }
    }
}
