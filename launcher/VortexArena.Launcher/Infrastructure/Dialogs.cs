using System.Windows;

namespace VortexArena.Launcher.Infrastructure;

/// <summary>The one place the launcher opens a modal box, so view models stay free of layout.</summary>
public static class Dialogs
{
    private const string DefaultTitle = "VortexArena Launcher";

    public static bool Confirm(string message, string title = DefaultTitle)
        => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Error(string message, string title = DefaultTitle)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(string message, string title = DefaultTitle)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>⚠️ Owner is passed only when a real window is up; an unshown owner throws.</summary>
    private static MessageBoxResult Show(
        string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var owner = Application.Current?.MainWindow;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, message, title, buttons, icon)
            : MessageBox.Show(message, title, buttons, icon);
    }
}
