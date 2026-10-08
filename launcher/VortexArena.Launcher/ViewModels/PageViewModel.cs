using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>Shared shape of the four pages: title, one-line description, a status message.</summary>
public abstract class PageViewModel : ObservableObject
{
    private string _message = "";
    private bool _messageIsError;

    protected PageViewModel(LauncherContext context) => Context = context;

    protected LauncherContext Context { get; }

    public abstract string Title { get; }

    public abstract string Description { get; }

    /// <summary>Last thing that happened, in the operator's words. Empty hides the strip.</summary>
    public string Message
    {
        get => _message;
        protected set => Set(ref _message, value);
    }

    public bool MessageIsError
    {
        get => _messageIsError;
        protected set => Set(ref _messageIsError, value);
    }

    protected void Inform(string message)
    {
        MessageIsError = false;
        Message = message;
    }

    protected void Fail(string message)
    {
        MessageIsError = true;
        Message = message;
    }

    protected void ClearMessage()
    {
        MessageIsError = false;
        Message = "";
    }

    /// <summary>Called on every shell tick while the page is loaded (and once at startup).</summary>
    public virtual Task TickAsync() => Task.CompletedTask;
}
