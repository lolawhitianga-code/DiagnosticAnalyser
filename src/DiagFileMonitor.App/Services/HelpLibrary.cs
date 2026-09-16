using System.IO;
using System.Windows.Resources;
using DiagFileMonitor.Core.Help;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.Services;

/// <summary>
/// The help text the ? button shows.
/// <para>
/// HELP.md ships inside the exe, so a fresh build has working help with nothing to copy. A HELP.md
/// on disk still wins - drop one beside the exe to correct a sentence or add a note about a site's
/// own machines without waiting for a build. Same arrangement as the logo.
/// </para>
/// </summary>
public static class HelpLibrary
{
    private const string FileName = "HELP.md";

    private static HelpFile? _loaded;

    /// <summary>Where the help actually came from, so the About text can say.</summary>
    public static string Source { get; private set; } = "not loaded";

    public static HelpFile Current => _loaded ??= Load();

    /// <summary>Drops the cached copy so an edited file on disk is picked up without a restart.</summary>
    public static void Reload()
    {
        _loaded = null;
        _ = Current;
    }

    public static HelpTopic? Find(string? id) => Current.Find(id);

    private static HelpFile Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;

            var fromDisk = HelpFile.ParseFile(path);
            if (fromDisk.Count > 0)
            {
                Source = path;
                return fromDisk;
            }

            SimpleLogger.Info($"'{path}' had no help topics in it, so the built-in help is used instead.");
        }

        return LoadEmbedded();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
        yield return Path.Combine(AppContext.BaseDirectory, "docs", FileName);

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiagFileMonitor", FileName);
    }

    /// <summary>The copy built into the exe, used when no file on disk overrides it.</summary>
    private static HelpFile LoadEmbedded()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/HELP.md", UriKind.Absolute);
            StreamResourceInfo? resource = System.Windows.Application.GetResourceStream(uri);

            if (resource is null)
            {
                Source = "none";
                return HelpFile.Empty;
            }

            using var reader = new StreamReader(resource.Stream);
            Source = "built in";
            return HelpFile.Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Could not load the built-in help", ex);
            Source = "none";
            return HelpFile.Empty;
        }
    }
}
