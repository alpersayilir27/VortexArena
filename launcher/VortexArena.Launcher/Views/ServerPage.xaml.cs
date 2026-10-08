using System.Windows.Controls;

namespace VortexArena.Launcher.Views;

/// <summary>
/// Server page. Only job in code-behind: follow the log tail.
/// <para>⚠️ Auto-scroll stops as soon as the operator scrolls up — otherwise reading an error is
/// impossible while the server keeps writing.</para>
/// </summary>
public partial class ServerPage : UserControl
{
    private bool _followTail = true;

    public ServerPage()
    {
        InitializeComponent();
        LogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLogScrolled));
    }

    private void OnLogScrolled(object sender, ScrollChangedEventArgs e)
    {
        // A pure scroll (no new content) is the operator's intent; growth is the tailer's.
        if (e.ExtentHeightChange == 0)
        {
            _followTail = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 4;
            return;
        }

        if (_followTail) LogBox.ScrollToEnd();
    }
}
