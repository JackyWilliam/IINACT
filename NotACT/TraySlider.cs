namespace Advanced_Combat_Tracker;

/// <summary>
/// Compatibility implementation of ACT's legacy tray notification window.
/// </summary>
public sealed class TraySlider : Form
{
    public enum ButtonLayoutEnum
    {
        OneButton,
        TwoButton,
        FourButton,
    }

    private readonly Label titleLabel = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly Label textLabel = new() { AutoSize = true };

    public TraySlider()
    {
        Width = 360;
        Height = 140;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12),
        };
        layout.Controls.Add(titleLabel);
        layout.Controls.Add(textLabel);
        Controls.Add(layout);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AddNotification { get; set; } = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ForceShow { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ShowDurationMs { get; set; } = 5000;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ButtonLayoutEnum ButtonLayout { get; set; }

    public Button ButtonOK { get; } = new();

    public Button ButtonNE { get; } = new();

    public Button ButtonNW { get; } = new();

    public Button ButtonSE { get; } = new();

    public Button ButtonSW { get; } = new();

    public Label TrayText => textLabel;

    public Label TrayTitle => titleLabel;

    public void ShowTraySlider()
        => ShowTraySlider(textLabel.Text, titleLabel.Text);

    public void ShowTraySlider(string message, string title = "")
    {
        textLabel.Text = message;
        titleLabel.Text = title;
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(workingArea.Right - Width, workingArea.Bottom - Height);
        Show();
        BringToFront();

        var timer = new System.Windows.Forms.Timer { Interval = Math.Max(250, ShowDurationMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Hide();
            timer.Dispose();
        };
        timer.Start();
    }
}
