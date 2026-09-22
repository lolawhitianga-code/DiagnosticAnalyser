using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>One reference photo as the window shows it.</summary>
/// <param name="Source">A pack URI the Image control can load straight from the exe.</param>
public record ReferencePhotoItem(string Source, string Caption);

public partial class AnalysisViewModel : ObservableObject
{
    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _reportText = string.Empty;

    /// <summary>The photo currently shown large over the report, or null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnlargedPhoto))]
    private ReferencePhotoItem? _enlargedPhoto;

    /// <summary>
    /// Reference photos from solved cases that match what this report found - a wrong home sensor
    /// and a right one, say. Kept out of the report text because that text gets copied into emails,
    /// and a photo of the thing to look for is worth more to the person at the screen than a
    /// paragraph describing it.
    /// </summary>
    public ObservableCollection<ReferencePhotoItem> ReferencePhotos { get; } = new();

    public string ReferenceTitle { get; }

    public string ReferenceSummary { get; }

    public bool HasReferences => ReferencePhotos.Count > 0;

    public bool HasEnlargedPhoto => EnlargedPhoto is not null;

    public AnalysisViewModel(string heading, string reportText)
        : this(heading, reportText, Array.Empty<ReferenceGuide>())
    {
    }

    public AnalysisViewModel(string heading, string reportText, IReadOnlyList<ReferenceGuide> guides)
    {
        Heading = heading;
        ReportText = reportText;

        foreach (var photo in guides.SelectMany(g => g.Photos))
        {
            ReferencePhotos.Add(new ReferencePhotoItem(PackUri(photo.ResourcePath), photo.Caption));
        }

        ReferenceTitle = string.Join(" / ", guides.Select(g => g.Title));
        ReferenceSummary = string.Join(" ", guides.Select(g => g.Summary));
    }

    /// <summary>The photos ship inside the exe, linked from docs/cases so there is one copy of each.</summary>
    private static string PackUri(string resourcePath) =>
        "pack://application:,,,/" + resourcePath.Replace('\\', '/').TrimStart('/');

    [RelayCommand]
    private void OpenPhoto(ReferencePhotoItem? photo) => EnlargedPhoto = photo;

    [RelayCommand]
    private void ClosePhoto() => EnlargedPhoto = null;

    [RelayCommand]
    private void Copy()
    {
        try
        {
            System.Windows.Clipboard.SetText(ReportText);
            Heading = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Heading = $"Could not copy: {ex.Message}";
            SimpleLogger.Error("Could not copy the analysis to the clipboard", ex);
        }
    }
}
