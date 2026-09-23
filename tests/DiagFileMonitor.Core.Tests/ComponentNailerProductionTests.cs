using DiagFileMonitor.Core.Production;
using DiagFileMonitor.Core.Reports;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// A Component Nailer's output is components, not panels: each MembersSubAssembled is one stud with
/// its blocks or noggings nailed on. Lines are from M21868 (Mainland, Component Nailer V2).
/// </summary>
public class ComponentNailerProductionTests
{
    private const string Week = """
        PanelStarted, 20260914 07:14:06, 11
        MemberAssembled, 20260914 07:15:22, 1, 0, Nogging-13, 0.002, 0.355
        MemberAssembled, 20260914 07:15:51, 1, 0, Nogging-13, 0.002, 0.355
        MembersSubAssembled, 20260914 07:15:58, 2, 5, Nogging-13, 0.0022365, 355,Nogging-13, 0.0022365, 355,Common Stud-10, 0.0146853, 2331
        MemberAssembled, 20260914 07:17:04, 1, 0, Nogging-15, 0.001, 0.087
        MemberAssembled, 20260914 07:17:15, 1, 0, Nogging-15, 0.001, 0.087
        MembersSubAssembled, 20260914 07:17:22, 2, 5, Nogging-15, 0.0005481, 87,Nogging-15, 0.0005481, 87,Common Stud-10, 0.0146853, 2331
        PanelStarted, 20260914 08:08:00, 12
        PanelStarted, 20260914 08:09:00, 113
        MemberAssembled, 20260914 08:09:45, 1, 0, Ao-Comp Block, 0.001, 0.394
        MembersSubAssembled, 20260914 08:10:01, 3, 5, Ap-Stud, 0.00802503250238525, 2369.3,Ao-Comp Block, 0.00133349737434631, 393.7,Ao-Comp Block, 0.00133349737434631, 393.7,Ao-Comp Block, 0.00133349737434631, 393.7
        PanelAssembled, 20260914 08:10:01, 0, 113, 0.116, 3.632, 0.4, 0, 18
        PanelStarted, 20260914 08:20:00, 14
        MembersSubAssembled, 20260914 08:20:44, 0, 0, Common Stud-4, 0.00944055, 2331
        """;

    private static IReadOnlyList<PanelRecord> Classify(string text = Week) =>
        new PanelClassifier().Classify(ProdLogParser.Parse(text).Events).Panels;

    [Fact]
    public void The_event_is_recognised()
    {
        var parsed = ProdLogParser.Parse(Week);

        Assert.Empty(parsed.UnknownEventNames);
        Assert.Equal(4, parsed.Events.Count(e => e.Kind == ProdLogEventKind.MembersSubAssembled));
    }

    [Fact]
    public void Each_sub_assembly_is_one_completed_component()
    {
        var done = Classify().Where(p => p.Outcome == PanelOutcome.Completed).ToList();

        Assert.Equal(3, done.Count);
        Assert.All(done, p => Assert.Equal(OutputKind.Component, p.Kind));

        var first = done[0];
        Assert.Equal("11 / Common Stud-10", first.Name);
        Assert.Equal(5, first.FastenerCount);
        Assert.Equal(2, first.Junctions);
        Assert.Equal(3, first.MembersAssembled);
        Assert.Equal(3.041, first.Lineal, 3);               // 355 + 355 + 2331 mm
        Assert.Equal(0.0191583, first.Cube, 6);
        Assert.Equal(36 / 60.0, first.BuildMinutes, 3);     // first nogging placed to closed
    }

    [Fact]
    public void The_stud_is_found_first_or_last()
    {
        Assert.Contains(Classify(), p => p.Name == "113 / Ap-Stud");
    }

    [Fact]
    public void A_panel_that_made_components_is_neither_superseded_nor_counted_again()
    {
        var panels = Classify();

        // 11 and 113 made components; 12 was opened and left, which is superseded as on any machine.
        var superseded = Assert.Single(panels, p => p.Outcome == PanelOutcome.Superseded);
        Assert.Equal("12", superseded.Name);

        // 113's PanelAssembled closes the panel; its one component is already counted.
        Assert.DoesNotContain(panels, p => p.Kind == OutputKind.Panel && p.Name == "113");
    }

    [Fact]
    public void A_stud_passed_through_with_nothing_nailed_is_stepped_past_not_a_fault()
    {
        var stud = Assert.Single(Classify(), p => p.Name == "14 / Common Stud-4");

        Assert.Equal(PanelOutcome.SteppedPast, stud.Outcome);
        Assert.False(stud.IsFault);
    }

    [Fact]
    public void The_summary_and_report_say_components()
    {
        var panels = Classify();
        var summary = ProductionAnalyser.Summarise(panels, ShiftModel.SingleDayShift, "M21868", "Mainland");

        Assert.Equal(OutputKind.Component, summary.Output);
        Assert.Equal(3, summary.PanelsCompleted);

        var html = new ReportHtmlRenderer().Render(ProductionReport.Build(summary, "Component Nailer V2", panels: panels));
        Assert.Contains("components completed", html);
        Assert.DoesNotContain("panels completed", html);
    }

    [Fact]
    public void A_wall_panel_machine_still_says_panels()
    {
        var panels = new PanelClassifier().Classify(ProdLogParser.Parse("""
            PanelStarted, 20260706 07:00:00, E5
            PanelAssembled, 20260706 07:13:09, 40, E5, 0.139, 3, 8.3, 5, 20
            """).Events).Panels;

        var summary = ProductionAnalyser.Summarise(panels, ShiftModel.SingleDayShift);

        Assert.Equal(OutputKind.Panel, summary.Output);
        Assert.Equal("panels", summary.Units);
    }

    [Fact]
    public void Every_component_wording_is_still_in_the_interactive_page()
    {
        // If the template's wording changes, the swap list has to change with it.
        var panelPage = ProductionInteractiveReport.Build(new ProductionSummary(), Array.Empty<PanelRecord>());

        Assert.All(ProductionInteractiveReport.ComponentWording, w => Assert.Contains(w.Panel, panelPage));
    }

    [Fact]
    public void The_interactive_page_says_components_and_keeps_its_script_names()
    {
        var panels = Classify();
        var summary = ProductionAnalyser.Summarise(panels, ShiftModel.SingleDayShift, "M21868", "Mainland");

        var page = ProductionInteractiveReport.Build(summary, panels, "Component Nailer V2");

        Assert.Contains(">Components<small>", page);
        Assert.Contains("This is a Component Nailer", page);
        Assert.Contains("data-met=\"panels\"", page);
        Assert.DoesNotContain(">Panels<small>", page);
    }

    [Fact]
    public async Task Stored_and_read_back_it_is_still_components_with_the_counter_live()
    {
        using var env = new TestEnvironment();
        var folder = Path.Combine(env.RootPath, "M21868 SDN mainland compn nailer", "Reports");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ProdLogV22026W38.log"), Week);

        var result = await env.Production.ImportFolderAsync(Path.GetDirectoryName(folder)!, "M21868");
        Assert.Equal(1, result.FilesRead);
        Assert.DoesNotContain(result.Notes, n => n.Contains("unrecognised"));

        var panels = await env.Production.LoadPanelsAsync("M21868");
        Assert.Equal(3, panels.Count(p => p.Kind == OutputKind.Component && p.Outcome == PanelOutcome.Completed));

        // Not stored; worked out again on the way back, or every day reads as counter off.
        var summary = ProductionAnalyser.Summarise(panels, ShiftModel.SingleDayShift, "M21868");
        Assert.Equal(0, summary.DaysFastenerCounterOff);
        Assert.Equal(OutputKind.Component, summary.Output);
    }
}
