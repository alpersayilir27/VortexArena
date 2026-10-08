using System.Globalization;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>One recording in the list.</summary>
public sealed class ReplayRowViewModel : ObservableObject
{
    private bool _isWriting;

    public ReplayRowViewModel(ReplayEntry entry, RecordingPageViewModel page)
    {
        Entry = entry;
        WatchCommand = new RelayCommand(() => page.Watch(this));
        ShowInFolderCommand = new RelayCommand(() => page.ShowInFolder(this));
    }

    public ReplayEntry Entry { get; }

    public ICommand WatchCommand { get; }

    public ICommand ShowInFolderCommand { get; }

    public string FileName => Entry.FileName;

    public string StartedText => Entry.Started.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

    public string SceneText => Display.OrDash(Entry.SceneName);

    public string ModeText => Display.OrDash(Entry.ModeId);

    public string SizeText => Display.Megabytes(Entry.Size);

    public string PlayersText => Entry.PlayerCount > 0
        ? Entry.PlayerCount.ToString(CultureInfo.CurrentCulture) + " oyuncu"
        : "—";

    public bool HasError => Entry.Error != null;

    public string ErrorText => Entry.Error ?? "";

    /// <summary>The file the server is writing right now — set by the page from the status.</summary>
    public bool IsWriting
    {
        get => _isWriting;
        set
        {
            if (!Set(ref _isWriting, value)) return;
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(ShowPartialBadge));
            OnPropertyChanged(nameof(CanWatch));
        }
    }

    /// <summary>A non-finalized file that nobody is writing lost its tail (crash/kill).</summary>
    public bool ShowPartialBadge => !Entry.Finalized && !IsWriting && !HasError;

    public bool CanWatch => !HasError && !IsWriting;

    public string DurationText
    {
        get
        {
            if (HasError) return "—";
            if (IsWriting) return "kaydediliyor";
            return Entry.Finalized ? Display.Clock(Entry.Duration) : "yarım";
        }
    }
}
