using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>The operator says something is out by a fixed amount.</summary>
public record HomeSensorFinding(
    string? Axis,
    IReadOnlyList<string> OtherCandidates,
    double? OffsetMm,
    string MatchedText,
    bool? HomesToSensor,
    string? HomeMode,
    IReadOnlyList<string> SensorHomedAxes,
    IReadOnlyList<ChangeLogEntry> RelevantChanges,
    int DaysLookedBack)
{
    public bool AxisKnown => Axis is not null;

    /// <summary>A position setting for that axis did change recently, so rule that out first.</summary>
    public bool SettingsChanged => RelevantChanges.Count > 0;

    public string GuideId => ReferenceGuides.HomeSensorGap.Id;
}

/// <summary>
/// Follows the steps support used on M21856, driven by what the operator wrote rather than the log.
/// <para>
/// The M21856 complaint was "fixe side out by 30mm". Nothing in MachineLog.txt showed it. What found
/// it was a chain of reasoning: one axis out by a fixed amount; nothing in Change.log that would move
/// it on purpose; the axis homes to a sensor, so that sensor sets its zero; send it home and look -
/// the sensor was too far from its aluminium block.
/// </para>
/// <para>
/// Per Spida, that situation looks almost identical on nearly every servo on every machine, CLX and
/// Omron. The configs bear it out: every servo on the Raked Wall Extruder V3 and the Wall Sheather
/// homes to a sensor, and so do the Tornado's follower, saw Y, Z and R and printer. The exceptions
/// are the Tornado's two belts, which define their position instead - so for those the sensor
/// advice is withheld rather than given.
/// </para>
/// <para>
/// So this works on any machine. It reads the operator's words for an amount and for which servo,
/// matches those words against the servos the machine's own config lists, reads how that servo
/// homes, and looks at Change.log for anything that would have moved it on purpose. If a relevant
/// setting did change that is said first - a changed home position or scale explains a fixed offset
/// just as well, and is quicker to check than a sensor.
/// </para>
/// </summary>
public static class HomeSensorComplaintCheck
{
    /// <summary>How far back a settings change counts as possibly responsible.</summary>
    public const int DaysToLookBack = 60;

    /// <summary>"out by 30mm", "30 mm out", "off by 30", "30mm short".</summary>
    private static readonly Regex OutBy = new(
        @"(?:\b(?:out|off|short|long|over|under)\b\W*(?:by\W*)?(?<n>\d+(?:\.\d+)?)\s*(?:mm|millimet\w*)?)"
        + @"|(?:(?<m>\d+(?:\.\d+)?)\s*(?:mm|millimet\w*)\W*(?:\b(?:out|off|short|long|over|under)\b))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Settings that would move an axis on purpose.</summary>
    private static readonly string[] PositionSettings =
        { "homeposition", "scale", "caldistance", "softmin", "softmax", "offset" };

    /// <summary>Words that say nothing about which axis.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "side", "axis", "servo", "out", "off", "by", "mm", "the", "is", "in", "on", "at", "of", "and",
        "a", "an", "it", "its", "to", "about", "approx", "short", "long", "over", "under", "position",
        "wrong", "not", "home", "homing", "millimetres", "millimeters"
    };

    /// <summary>
    /// What operators call things, against what the config calls them. The operator on M21856
    /// wrote "side" and meant the gripper trolley; a Tornado operator says "in belt" for XInAxis.
    /// </summary>
    private static readonly (string AxisWord, string[] Said)[] Synonyms =
    {
        ("trolley", new[] { "gripper", "grippers", "puller", "pullers", "trolley", "trolleys" }),
        ("eject", new[] { "ejector", "ejectors", "eject", "ejection" }),
        ("follower", new[] { "follower" }),
        ("xin", new[] { "inbelt", "infeedbelt" }),
        ("xout", new[] { "outbelt", "outfeedbelt" }),
        ("printer", new[] { "printer", "print" }),
        ("yaxis", new[] { "head", "floatinghead", "height" })
    };

    /// <summary>The Tornado's saw axes, as the log names them.</summary>
    private static readonly (string AxisWord, string[] Said)[] SawAxes =
    {
        ("y", new[] { "sawoffset", "offset" }),
        ("z", new[] { "sawheight", "height" }),
        ("r", new[] { "sawrotation", "rotation", "sawangle", "angle" })
    };

    public static HomeSensorFinding? Check(
        string? issue,
        IReadOnlyList<ChangeLogEntry> changeLog,
        DateTime? bundleTime,
        string? model,
        MachineConfig? config = null)
    {
        if (string.IsNullOrWhiteSpace(issue)) return null;

        var outBy = OutBy.Match(issue);
        if (!outBy.Success) return null;

        var number = outBy.Groups["n"].Success ? outBy.Groups["n"].Value : outBy.Groups["m"].Value;
        double? offset = double.TryParse(number, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var mm) ? mm : null;

        var said = Words(issue);
        var axes = Servos(config);

        var (axis, others) = axes.Count > 0 ? Pick(axes, said) : Fallback(said, model);
        var configured = axis is null ? null : axes.FirstOrDefault(a => a.Name == axis.Value.Name);

        var homeMode = configured?.HomeMode;
        bool? toSensor = string.IsNullOrWhiteSpace(homeMode)
            ? null
            : homeMode.Equals("Sensor", StringComparison.OrdinalIgnoreCase);

        var since = (bundleTime ?? DateTime.Now).AddDays(-DaysToLookBack);
        var relevant = changeLog
            .Where(c => c.Timestamp >= since)
            .Where(c => PositionSettings.Any(p => c.Setting.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Where(c => axis is null || AboutAxis(c, axis.Value.Name))
            .OrderByDescending(c => c.Timestamp)
            .ToList();

        return new HomeSensorFinding(
            axis is null ? null : Display(axis.Value.Name),
            others.Select(Display).ToList(),
            offset,
            outBy.Value.Trim(),
            toSensor,
            homeMode,
            axes.Where(a => a.HomeMode.Equals("Sensor", StringComparison.OrdinalIgnoreCase))
                .Select(a => Display(a.Name)).ToList(),
            relevant,
            DaysToLookBack);
    }

    /// <summary>
    /// The servos on the machine's main controller. Definitions for options on other ports belong
    /// to kit the machine may not have - the M21856 config lists servo guns on a CLX port the
    /// machine does not run - so they are left out.
    /// </summary>
    private static List<ConfiguredAxis> Servos(MachineConfig? config)
    {
        if (config is null || config.Axes.Count == 0) return new List<ConfiguredAxis>();

        var main = config.MainPort;
        return config.Axes
            .Where(a => a.Velocity > 0)
            .Where(a => main.Length == 0 || a.Port.Equals(main, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static ((string Name, HashSet<string> Words)? Axis, List<string> Others) Pick(
        List<ConfiguredAxis> axes, HashSet<string> said)
    {
        var saysFixed = said.Any(w => w.StartsWith("fix", StringComparison.Ordinal));
        var saysFloating = said.Any(w => w.StartsWith("float", StringComparison.Ordinal));

        var scored = axes
            .Select(a => (a.Name, Words: AxisWords(a.Name)))
            // The side is the strongest thing an operator says: the fixed side is never the floating one.
            .Where(a => !(saysFixed && !saysFloating && a.Words.Contains("floating")))
            .Where(a => !(saysFloating && !saysFixed && a.Words.Contains("fixed")))
            .Select(a => (a.Name, a.Words, Score: said.Count(w => !Generic.Contains(w) && Matches(w, a.Words))))
            .Where(a => a.Score > 0)
            .OrderByDescending(a => a.Score)
            .ToList();

        if (scored.Count == 0) return (null, new List<string>());

        var top = scored.Where(a => a.Score == scored[0].Score).ToList();

        // "Fixed side out by 30 mm" names a side and nothing else. A side's position is its trolley.
        if (top.Count == 1) return ((top[0].Name, top[0].Words), new List<string>());

        var chosen = top.FirstOrDefault(a => a.Words.Contains("trolley"));
        if (chosen.Name is null) return (null, top.Select(a => a.Name).ToList());

        // Only mention the others if the operator said more than a side - then it is a real guess.
        var saidOnlySide = said.Where(w => !Generic.Contains(w))
            .Where(w => axes.Any(a => Matches(w, AxisWords(a.Name))))
            .All(w => w.StartsWith("fix", StringComparison.Ordinal) || w.StartsWith("float", StringComparison.Ordinal)
                || w.StartsWith("side", StringComparison.Ordinal));
        var others = saidOnlySide
            ? new List<string>()
            : top.Where(a => a.Name != chosen.Name).Select(a => a.Name).ToList();
        return ((chosen.Name, chosen.Words), others);
    }

    /// <summary>With no config to read, a side on its own still means that side's trolley.</summary>
    private static ((string Name, HashSet<string> Words)? Axis, List<string> Others) Fallback(
        HashSet<string> said, string? model)
    {
        var saysFixed = said.Any(w => w.StartsWith("fix", StringComparison.Ordinal));
        var saysFloating = said.Any(w => w.StartsWith("float", StringComparison.Ordinal));
        if (saysFixed == saysFloating) return (null, new List<string>());

        var name = saysFixed ? "FixedSide Trolley" : "FloatingSide Trolley";
        return ((name, AxisWords(name)), new List<string>());
    }

    /// <summary>
    /// Whether a Change.log line is about this axis. Change.log uses its own names - the V3's
    /// "FixedSide Trolley" is "FixedSidePuller" there - so each word of the axis name can be met
    /// by what operators call it too. Older versions leave the category blank; those cannot be
    /// ruled out, so they are kept.
    /// </summary>
    private static bool AboutAxis(ChangeLogEntry change, string axisName)
    {
        if (string.IsNullOrWhiteSpace(change.Category)) return true;

        var where = MachineIoMap.Flatten(change.Category);
        return NameWords(axisName)
            .Where(w => w.Length >= 3 && !Generic.Contains(w))
            .All(w => Alternatives(w).Any(a => where.Contains(a, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Alternatives(string word)
    {
        yield return word;
        foreach (var (axisWord, said) in Synonyms)
            if (word.StartsWith(axisWord, StringComparison.Ordinal))
                foreach (var s in said) yield return s;
    }

    /// <summary>
    /// Generic axis words ("side", "out", "axis") are left out, or "out by 30" would match the
    /// Tornado's out belt and "side out" every side on the machine.
    /// </summary>
    private static bool Matches(string said, HashSet<string> axisWords)
    {
        var distinctive = axisWords.Where(w => !Generic.Contains(w)).ToList();
        return distinctive.Contains(said)
            || said.Length >= 3 && distinctive.Any(w => w.Length >= 3 && (w.StartsWith(said) || said.StartsWith(w)));
    }

    /// <summary>
    /// "FixedSide Trolley" -> fixed, side, trolley, and the words operators use for a trolley.
    /// "YZR ZAxis" -> the Tornado's saw height.
    /// </summary>
    private static HashSet<string> AxisWords(string name)
    {
        var words = NameWords(name);

        var saw = words.Contains("yzr");

        // The floating head's Y axis is its height; the saw's Y axis is its offset, not its height.
        foreach (var (axisWord, said) in Synonyms)
            if (!(saw && axisWord == "yaxis") && words.Any(w => w.StartsWith(axisWord, StringComparison.Ordinal)))
                words.UnionWith(said);

        if (saw)
            foreach (var (axisWord, said) in SawAxes)
                if (words.Contains(axisWord + "axis") || words.Contains(axisWord))
                    words.UnionWith(said);

        return words;
    }

    /// <summary>"FixedSide Trolley" -> fixedside, fixed, side, trolley.</summary>
    private static HashSet<string> NameWords(string name)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in Regex.Split(name, @"[\s/_\-()]+").Where(p => p.Length > 0))
        {
            words.Add(part.ToLowerInvariant());
            foreach (Match m in Regex.Matches(part, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
                words.Add(m.Value.ToLowerInvariant());
        }

        return words;
    }

    /// <summary>
    /// The operator's words, lower case, with numbers split off ("30mm" -> 30, mm) and each pair
    /// joined as well, so "in belt" can match "inbelt".
    /// </summary>
    private static HashSet<string> Words(string text)
    {
        var tokens = Regex.Matches(text.ToLowerInvariant(), @"[a-z]+|\d+").Select(m => m.Value).ToList();
        var words = new HashSet<string>(tokens, StringComparer.Ordinal);

        for (var i = 0; i + 1 < tokens.Count; i++)
            words.Add(tokens[i] + tokens[i + 1]);

        return words;
    }

    /// <summary>"FixedSide Trolley" -> "fixed side trolley"; "YZR ZAxis" -> "saw Z axis".</summary>
    private static string Display(string name)
    {
        // The Tornado's belts: nobody calls them XInAxis.
        if (name.EndsWith("XInAxis", StringComparison.Ordinal)) return "in belt (XInAxis)";
        if (name.EndsWith("XOutAxis", StringComparison.Ordinal)) return "out belt (XOutAxis)";

        var spaced = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)", " ");
        spaced = spaced.Replace("YZR ", "saw ", StringComparison.Ordinal);
        spaced = Regex.Replace(spaced, @"\b([XYZR]) ?Axis\b", "$1 axis");
        return Regex.Replace(spaced, @"\s+", " ").Trim().ToLowerInvariant()
            .Replace("saw y axis", "saw Y axis").Replace("saw z axis", "saw Z axis").Replace("saw r axis", "saw R axis");
    }
}
