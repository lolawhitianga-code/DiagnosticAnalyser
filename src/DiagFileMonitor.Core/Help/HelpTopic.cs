namespace DiagFileMonitor.Core.Help;

/// <summary>One thing the help file explains: a button, a field, a panel.</summary>
public class HelpTopic
{
    /// <summary>The id a control names, e.g. <c>main.analyse</c>. Lower case, dotted.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The heading, which is the control's own label.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The explanation, as plain paragraphs and bullets.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>The window this topic belongs to - the part of the id before the first dot.</summary>
    public string Area => Id.Split('.', 2)[0];
}
