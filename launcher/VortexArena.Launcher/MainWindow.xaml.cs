using System.ComponentModel;
using System.Windows;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.ViewModels;
using VortexArena.Launcher.Views;

namespace VortexArena.Launcher;

/// <summary>Shell window: sidebar + page host. All behaviour lives in the view models.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private bool _closeConfirmed;
    private bool _asking;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.ApplyDarkTitleBar(this);
    }

    /// <summary>
    /// Closing with things running asks first.
    /// <para>⚠️ The server does NOT depend on the launcher: "leave running" is a real choice, and a
    /// re-opened launcher recognizes it again from the status endpoint.</para>
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed) return;

        var serverRunning = _model.ServerIsRunning;
        var adminRunning = _model.AdminIsRunning;
        if (!serverRunning && !adminRunning && !_model.HasBackgroundWork) return;

        e.Cancel = true;
        if (_asking) return;
        _asking = true;
        // ⚠️ Deferred: Close() called while OnClosing is still on the stack throws.
        Dispatcher.BeginInvoke(new Action(() => AskThenClose(serverRunning, adminRunning)));
    }

    private async void AskThenClose(bool serverRunning, bool adminRunning)
    {
        try
        {
            var running = (serverRunning, adminRunning) switch
            {
                (true, true) => "Sunucu ve yönetim uygulaması çalışıyor.",
                (true, false) => "Sunucu çalışıyor.",
                (false, true) => "Yönetim uygulaması çalışıyor.",
                _ => "Arka planda süren bir iş var.",
            };

            var dialog = new ExitDialog($"{running} Ne yapmak istersiniz?", _model.HasBackgroundWork)
            {
                Owner = this,
            };

            dialog.ShowDialog();
            if (dialog.Choice == ExitChoice.Cancel) return;

            if (dialog.Choice == ExitChoice.CloseAll)
            {
                IsEnabled = false;
                try
                {
                    await _model.ShutdownEverythingAsync();
                }
                catch (Exception)
                {
                    // Best effort: the operator chose to leave either way.
                }
            }

            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _asking = false;
        }
    }
}
