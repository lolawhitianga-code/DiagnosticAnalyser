using System.Windows;
using System.Windows.Media;

namespace DiagFileMonitor.App.Help;

/// <summary>
/// Names the help topic a control explains.
/// <para>
/// A control carries its topic id: <c>help:Help.Topic="main.analyse"</c>. The lookup walks up the
/// visual tree, so a panel can carry one topic for everything inside it - the stat tiles are one
/// explanation, not six - and a label beside a box inherits the box's topic without being tagged.
/// </para>
/// </summary>
public static class Help
{
    public static readonly DependencyProperty TopicProperty = DependencyProperty.RegisterAttached(
        "Topic", typeof(string), typeof(Help),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetTopic(DependencyObject element, string? value) =>
        element.SetValue(TopicProperty, value);

    public static string? GetTopic(DependencyObject element) =>
        (string?)element.GetValue(TopicProperty);

    /// <summary>
    /// The topic for whatever was clicked: the control's own, or the nearest one above it.
    /// <para>
    /// The property inherits, so in most cases the value is already there. The walk is the
    /// fallback for the places inheritance does not reach - popup and context menu content, which
    /// sit in their own trees.
    /// </para>
    /// </summary>
    public static string? TopicFor(DependencyObject? element)
    {
        while (element is not null)
        {
            if (GetTopic(element) is { Length: > 0 } topic) return topic;

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }
}
