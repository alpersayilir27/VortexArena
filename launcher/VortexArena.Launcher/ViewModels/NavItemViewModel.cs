using VortexArena.Launcher.Infrastructure;

namespace VortexArena.Launcher.ViewModels;

/// <summary>Small state badge drawn next to a sidebar entry.</summary>
public enum NavIndicator
{
    None,

    /// <summary>Green play triangle — the thing is up.</summary>
    Running,

    /// <summary>Pulsing amber dot — starting or shutting down.</summary>
    Pending,

    /// <summary>Blinking red dot — a recording file is open.</summary>
    RecordingActive,

    /// <summary>Hollow red ring — recording armed, waiting for the match.</summary>
    RecordingArmed,

    /// <summary>An authorized headset is attached.</summary>
    DeviceReady,
}

public sealed class NavItemViewModel : ObservableObject
{
    private bool _isSelected;
    private NavIndicator _indicator;

    public NavItemViewModel(string title, string iconKey, PageViewModel page)
    {
        Title = title;
        IconKey = iconKey;
        Page = page;
    }

    public string Title { get; }

    /// <summary>Key of a <c>Geometry</c> in <c>Theme/Icons.xaml</c>.</summary>
    public string IconKey { get; }

    public PageViewModel Page { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public NavIndicator Indicator
    {
        get => _indicator;
        set => Set(ref _indicator, value);
    }
}
