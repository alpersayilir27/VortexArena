using System.Windows;
using VortexArena.Launcher.Infrastructure;

namespace VortexArena.Launcher.Views;

/// <summary>What the operator chose when closing with things still running.</summary>
public enum ExitChoice
{
    Cancel,

    /// <summary>Close the launcher, leave server/admin running.</summary>
    LeaveRunning,

    CloseAll,
}

public partial class ExitDialog : Window
{
    public ExitDialog(string bodyText, bool hasBackgroundWork)
    {
        // Set BEFORE InitializeComponent: these are plain properties, so the bindings read them
        // once at load and never hear about a later assignment.
        BodyText = bodyText;
        WorkWarningVisibility = hasBackgroundWork ? Visibility.Visible : Visibility.Collapsed;
        InitializeComponent();
    }

    public string BodyText { get; }

    public Visibility WorkWarningVisibility { get; }

    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyDarkTitleBar(this);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close(ExitChoice.Cancel);

    private void OnLeaveRunning(object sender, RoutedEventArgs e) => Close(ExitChoice.LeaveRunning);

    private void OnCloseAll(object sender, RoutedEventArgs e) => Close(ExitChoice.CloseAll);

    private void Close(ExitChoice choice)
    {
        Choice = choice;
        DialogResult = choice != ExitChoice.Cancel;
    }
}
