using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DiagFileMonitor.App.Help;

/// <summary>
/// A transparent sheet laid over the window while help mode is on, so every click lands somewhere
/// that can be caught.
/// <para>
/// This exists because of disabled controls. WPF does not raise a mouse event for one at all - it
/// is skipped during input hit testing - so a handler on the window never hears about a click on a
/// greyed out button, and whatever the popup was showing stays on screen. A greyed out button is
/// exactly the one somebody wants explained, so the click has to be caught somewhere else.
/// </para>
/// <para>
/// The sheet is enabled and hit-test visible, so the pointer always lands on something and the
/// window's own handler runs. What was really under the pointer is then found by hit testing the
/// window directly, with the sheet itself skipped - and that hit test does see disabled controls.
/// </para>
/// </summary>
internal class HelpOverlay : Adorner
{
    public HelpOverlay(UIElement adorned) : base(adorned)
    {
        IsHitTestVisible = true;
        Focusable = false;
    }

    /// <summary>
    /// A transparent fill still takes a hit; no fill at all does not. That one line is the whole
    /// mechanism.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(
            System.Windows.Media.Brushes.Transparent, null, new Rect(AdornedElement.RenderSize));

    /// <summary>Whatever is under the pointer in the window below, disabled or not.</summary>
    public DependencyObject? ElementUnder(System.Windows.Point pointInWindow, Visual window)
    {
        DependencyObject? hit = null;

        VisualTreeHelper.HitTest(
            window,
            target => IsPartOfTheOverlay(target)
                // Skip the sheet and everything drawn in it, or every click finds the sheet.
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            result =>
            {
                hit = result.VisualHit;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(pointInWindow));

        return hit;
    }

    private bool IsPartOfTheOverlay(DependencyObject target) =>
        ReferenceEquals(target, this) || target is AdornerLayer;
}
