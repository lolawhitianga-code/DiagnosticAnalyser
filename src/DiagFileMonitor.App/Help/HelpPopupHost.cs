using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using DiagFileMonitor.App.Services;
using DiagFileMonitor.Core.Help;

namespace DiagFileMonitor.App.Help;

/// <summary>
/// The little window that appears beside a control and explains it.
/// <para>
/// Built in code rather than XAML on purpose: XAML cannot be compiled on the Linux box this is
/// developed on, so anything written there ships unverified. As plain C# it is type-checked by the
/// same sweep that covers the ViewModels.
/// </para>
/// </summary>
public class HelpPopupHost
{
    private const double Width = 380;

    private readonly Popup _popup = new()
    {
        AllowsTransparency = true,
        // True, so releasing the mouse does not count as a click outside and shut it. The mode
        // closes it itself - on the next help click, Escape, or leaving the window.
        StaysOpen = true,
        Placement = PlacementMode.Bottom,
        PopupAnimation = PopupAnimation.Fade,
        HorizontalOffset = 0,
        VerticalOffset = 4
    };

    public bool IsOpen => _popup.IsOpen;

    public void Close() => _popup.IsOpen = false;

    /// <summary>Shows a topic beside the control it belongs to. Returns false where there is
    /// nothing written for it, so the caller can say so rather than flashing an empty box.</summary>
    public bool Show(UIElement anchor, HelpTopic? topic)
    {
        if (topic is null) return false;

        _popup.PlacementTarget = anchor;
        _popup.Child = Build(topic);
        _popup.IsOpen = true;

        return true;
    }

    private static FrameworkElement Build(HelpTopic topic)
    {
        var body = new StackPanel();

        foreach (var block in HelpText.Blocks(topic.Body))
        {
            body.Children.Add(Line(block));
        }

        var content = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = topic.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = ThemeBrush("BrandBlueDark", System.Windows.Media.Color.FromRgb(0x00, 0x84, 0xB6))
        });

        content.Children.Add(body);

        return new Border
        {
            Width = Width,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = ThemeBrush("PanelBorder", System.Windows.Media.Color.FromRgb(0xDC, 0xE4, 0xE8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.2,
                Color = System.Windows.Media.Colors.Black
            }
        };
    }

    /// <summary>
    /// Renders the handful of things the help file actually uses - paragraphs, bullets and bold -
    /// rather than pulling in a Markdown library for three features.
    /// </summary>
    private static System.Windows.Controls.TextBlock Line(HelpBlock block)
    {
        var bullet = block.IsBullet;

        var element = new System.Windows.Controls.TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(bullet ? 12 : 0, 0, 0, 8),
            LineHeight = 18
        };

        if (bullet) element.Inlines.Add(new Run("•  "));

        // The pieces alternate plain, bold, plain.
        var pieces = HelpText.BoldRuns(block.Text);
        for (var i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].Length == 0) continue;

            element.Inlines.Add(i % 2 == 1
                ? new Bold(new Run(pieces[i]))
                : new Run(pieces[i]));
        }

        return element;
    }

    private static System.Windows.Media.Brush ThemeBrush(string themeKey, System.Windows.Media.Color fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(themeKey) is System.Windows.Media.Brush found)
            return found;

        return new System.Windows.Media.SolidColorBrush(fallback);
    }
}
