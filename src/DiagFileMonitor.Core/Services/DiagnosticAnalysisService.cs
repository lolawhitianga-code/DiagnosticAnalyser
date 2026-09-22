using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Runs the Spida log reading method over stored bundles, on demand from the dashboard.
/// Reads the three log files back off disk, so a bundle whose extracted files have been
/// cleaned up reports that plainly rather than producing an empty analysis.
/// </summary>
public class DiagnosticAnalysisService
{
    private readonly DiagFileRepository _repository;
    private readonly SpidaLogAnalyser _analyser;

    public DiagnosticAnalysisService(DiagFileRepository repository, SpidaLogAnalyserOptions? options = null)
    {
        _repository = repository;
        _analyser = new SpidaLogAnalyser(options);
    }

    /// <summary>
    /// Virtual so a test can stand in a failing analysis. Burst alerting depends on this
    /// succeeding, and the behaviour when it does not is worth pinning down.
    /// </summary>
    public virtual async Task<string> AnalyseAsync(IEnumerable<int> diagnosticFileIds, CancellationToken token = default) =>
        (await AnalyseWithGuidesAsync(diagnosticFileIds, token)).Report;

    /// <summary>
    /// The report, plus any reference photos that go with what it found. The photos are kept
    /// separate from the text because the text is copied to the clipboard and pasted into emails,
    /// and the photos are for the technician looking at the screen.
    /// </summary>
    public virtual async Task<AnalysisOutcome> AnalyseWithGuidesAsync(
        IEnumerable<int> diagnosticFileIds, CancellationToken token = default)
    {
        var bundles = await _repository.GetByIdsAsync(diagnosticFileIds);

        if (bundles.Count == 0) return new AnalysisOutcome("Nothing to analyse.", Array.Empty<ReferenceGuide>());

        return await Task.Run(() =>
        {
            var results = bundles.Select(AnalyseOne).ToList();

            var guides = results
                .SelectMany(r => r.Guides)
                .DistinctBy(g => g.Id)
                .ToList();

            if (results.Count == 1) return new AnalysisOutcome(results[0].Report, guides);

            var header = $"Analysed {results.Count} diagnostic files.\n\n";
            return new AnalysisOutcome(
                header + string.Join("\n\n" + new string('=', 78) + "\n\n", results.Select(r => r.Report)),
                guides);
        }, token);
    }

    private AnalysisOutcome AnalyseOne(DiagnosticFile bundle)
    {
        var summary = DiagnosticFileSummary.FromEntity(bundle);

        if (bundle.ExtractedPath is null || !Directory.Exists(bundle.ExtractedPath))
        {
            return new AnalysisOutcome(
                $"DIAGNOSTIC ANALYSIS - {bundle.OriginalFileName}\n"
                + new string('-', 78) + "\n"
                + "The unpacked files for this bundle are no longer on disk, so the logs cannot be\n"
                + "read. Clear the database and re-process the original .szip to analyse it.\n",
                Array.Empty<ReferenceGuide>());
        }

        var logs = BundleLogs.Read(bundle);

        var analysis = _analyser.Analyse(logs.MachineLog, logs.ErrLog, logs.ChangeLog, bundle.ArrivedAtUtc);

        // Machine.xml is the better source for the model; fall back to what the PLC reported.
        var knowledge = KnowledgeAnnotator.Annotate(
            analysis, logs.MachineLog, bundle.MachineType, bundle.SerialNumber, logs.MachineConfigPath,
            bundle.ExtractedPath);

        // What the operator wrote in SupportInfo.txt decides where the report points first.
        var complaint = ComplaintRouter.Route(
            bundle.SupportIssue, logs.ChangeLog, logs.MachineLog, bundle.MachineType,
            bundle.ArrivedAtUtc.ToLocalTime(), MachineConfigIo.Read(logs.MachineConfigPath));

        var report = SpidaReportFormatter.Format(summary, analysis, knowledge, complaint);

        // A bundle carrying two files of the same name had a choice made for it. Say which.
        var text = logs.SelectionNotes.Count == 0
            ? report
            : report + "\n" + string.Join("\n", logs.SelectionNotes.Select(n => "Note: " + n)) + "\n";

        return new AnalysisOutcome(text, complaint.Guides);
    }

}

/// <summary>A report, and the reference photos that go with what it found.</summary>
public record AnalysisOutcome(string Report, IReadOnlyList<ReferenceGuide> Guides);
