using System.Windows;
using System.Windows.Input;
using DiagFileMonitor.App.Services;

namespace DiagFileMonitor.App.Help;

/// <summary>
/// "Ask about this" mode. Press ?, then click anything to find out what it does.
/// <para>
/// The click is caught on the preview pass and swallowed, so it asks about the control rather than
/// pressing it. That is the point of the whole feature - somebody can find out what Clear
/// database does without finding out the hard way.
/// </para>
/// </summary>
public class HelpMode
{
    private readonly Window _window;
    private readonly HelpPopupHost _popup = new();

    private bool _on;
    private Cursor? _previousCursor;

    /// <summary>
    /// The ? itself. The mode swallows every click while it is on, which would otherwise swallow
    /// the press that turns it off - so a click on this one is handled here instead.
    /// </summary>
    private DependencyObject? _ownButton;

    public HelpMode(Window window) => _window = window;

    /// <summary>
    /// Wires a ? button to a window in one line, so every window gets the same behaviour without
    /// each one growing its own copy of it.
    /// </summary>
    public static HelpMode Attach(Window window, System.Windows.Controls.Primitives.ButtonBase button,
        Action<string>? say = null)
    {
        var mode = new HelpMode(window) { _ownButton = button };

        button.Click += (_, _) => mode.Toggle();
        mode.Changed += (_, _) => button.Opacity = mode.IsOn ? 1.0 : 0.65;
        if (say is not null) mode.Said += (_, text) => say(text);

        // The mode must not outlive the window it is listening to.
        window.Closed += (_, _) => mode.TurnOff();

        button.Opacity = 0.65;
        return mode;
    }

    public bool IsOn => _on;

    /// <summary>Raised when the mode turns on or off, so a ? button can show itself pressed.</summary>
    public event EventHandler? Changed;

    /// <summary>Says what happened, for the window's status line.</summary>
    public event EventHandler<string>? Said;

    public void Toggle()
    {
        if (_on) TurnOff(); else TurnOn();
    }

    public void TurnOn()
    {
        if (_on) return;

        _on = true;
        _previousCursor = _window.Cursor;
        _window.Cursor = Cursors.Help;

        _window.PreviewMouseLeftButtonDown += OnClick;
        _window.PreviewKeyDown += OnKey;

        Said?.Invoke(this, "Click anything to find out what it does. Escape when you are done.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TurnOff()
    {
        if (!_on) return;

        _on = false;
        _window.Cursor = _previousCursor;

        _window.PreviewMouseLeftButtonDown -= OnClick;
        _window.PreviewKeyDown -= OnKey;

        _popup.Close();

        Said?.Invoke(this, string.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether a click landed on the ? button, or on something drawn inside it.</summary>
    private bool IsOwnButton(DependencyObject? clicked)
    {
        if (_ownButton is null) return false;

        while (clicked is not null)
        {
            if (ReferenceEquals(clicked, _ownButton)) return true;

            clicked = clicked is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(clicked)
                : LogicalTreeHelper.GetParent(clicked);
        }

        return false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        TurnOff();
        e.Handled = true;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        // Swallow it whatever happens next: a click in this mode must never press the button.
        e.Handled = true;

        if (e.OriginalSource is not DependencyObject clicked)
        {
            TurnOff();
            return;
        }

        // Pressing ? again comes out of the mode. Without this the click is swallowed like any
        // other and the only way out is Escape.
        if (IsOwnButton(clicked))
        {
            TurnOff();
            return;
        }

        var id = Help.TopicFor(clicked);
        var topic = HelpLibrary.Find(id);

        if (topic is null)
        {
            // Nothing written for it. Say so plainly rather than flashing an empty box, and stay
            // on so the next click still works.
            Said?.Invoke(this, id is { Length: > 0 }
                ? $"No help written for '{id}' yet."
                : "Nothing to explain there. Try a button, a box or a tick.");

            _popup.Close();
            return;
        }

        var anchor = clicked as UIElement ?? _window;
        _popup.Show(anchor, topic);

        Said?.Invoke(this, $"{topic.Title} - press Escape when you are done.");
    }
}
