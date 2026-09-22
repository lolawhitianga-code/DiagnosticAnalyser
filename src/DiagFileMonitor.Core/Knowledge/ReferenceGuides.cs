namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One photo a technician can compare the machine against.</summary>
/// <param name="ResourcePath">Where the picture ships inside the app, e.g. Assets/References/x.jpg.</param>
/// <param name="Caption">What the picture shows, in a technician's words.</param>
public record ReferencePhoto(string ResourcePath, string Caption);

/// <summary>A set of photos that goes with one kind of finding.</summary>
public record ReferenceGuide(string Id, string Title, string Summary, IReadOnlyList<ReferencePhoto> Photos);

/// <summary>
/// Photos from solved cases, shown beside the report when a finding points at the same thing.
/// <para>
/// A written instruction to "check the sensor sits 1-2 mm from the block" is easy to misread on a
/// machine you have not seen before. A photo of a wrong one and a right one is not. These come
/// from real jobs: each set says which case it came from, so it can be traced back.
/// </para>
/// </summary>
public static class ReferenceGuides
{
    public static readonly ReferenceGuide HomeSensorGap = new(
        "home-sensor-gap",
        "Servo home sensor - too far vs correct",
        "From M21856 (Mainland), September 2026: fixed side out by 30 mm, fixed by moving the "
        + "home sensor to 1-2 mm from the aluminium block. A Raked Wall Extruder V3 trolley is shown, "
        + "but the home sensor looks and works almost the same on every servo, CLX and Omron.",
        new[]
        {
            new ReferencePhoto(
                "Assets/References/home-sensor-too-far.jpg",
                "WRONG - sensor sitting too far above the aluminium block. The point where it turns "
                + "off as the servo backs away is not crisp, so home lands in the wrong place."),
            new ReferencePhoto(
                "Assets/References/home-sensor-correct.jpg",
                "RIGHT - sensor 1-2 mm from the aluminium block. The servo drives onto the block, "
                + "backs slowly off, and the instant the sensor turns off is home.")
        });

    public static readonly IReadOnlyList<ReferenceGuide> All = new[] { HomeSensorGap };

    public static ReferenceGuide? Find(string id) =>
        All.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
