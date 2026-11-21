using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Lights;

/// <summary>One batch of outputs, and when to switch it relative to the first batch.</summary>
public sealed record LightStep(TimeSpan At, string? Name, IReadOnlyList<int> Channels);

/// <summary>
/// Turns the configured groups into timed steps for one switch.
/// </summary>
/// <remarks>
/// Turning on walks the groups first to last; turning off walks them last to first, and the gap
/// between two groups is the same in both directions, so "Wides, then Centers 0.1s later" turns off
/// as "Centers, then Wides 0.1s later". Only the outputs the action asked for are switched: groups
/// with none of them are skipped along with their gap, and requested outputs in no group join the
/// first step, so nothing asked for is ever left out.
/// </remarks>
public static class LightSequence
{
    /// <summary>Upper bound on one gap, so a typo cannot hold the lights for minutes.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(10);

    /// <exception cref="FormatException">A group's outputs are not a list of output numbers.</exception>
    public static IReadOnlyList<LightStep> Plan(
        IReadOnlyList<LightGroup> groups,
        IReadOnlyList<int> requested,
        bool on)
    {
        var claimed = new HashSet<int>();
        var used = new List<(LightGroup Group, List<int> Channels)>();

        foreach (var group in groups)
        {
            // Each output belongs to the first group that lists it.
            var channels = ParseGroup(group)
                .Where(c => requested.Contains(c) && claimed.Add(c))
                .ToList();
            if (channels.Count > 0) used.Add((group, channels));
        }

        var leftover = requested.Where(c => !claimed.Contains(c)).ToList();
        if (used.Count == 0) return [new LightStep(TimeSpan.Zero, null, requested)];

        var steps = new List<LightStep>(used.Count);
        var at = TimeSpan.Zero;

        if (on)
        {
            for (var i = 0; i < used.Count; i++)
            {
                if (i > 0) at += Gap(used[i].Group);
                steps.Add(new LightStep(at, used[i].Group.Name, used[i].Channels));
            }
        }
        else
        {
            for (var i = used.Count - 1; i >= 0; i--)
            {
                // The gap before a group when turning on is the gap after it when turning off.
                if (i < used.Count - 1) at += Gap(used[i + 1].Group);
                steps.Add(new LightStep(at, used[i].Group.Name, used[i].Channels));
            }
        }

        if (leftover.Count > 0)
        {
            steps[0] = steps[0] with { Channels = [.. steps[0].Channels, .. leftover] };
        }

        return steps;
    }

    /// <summary>A group's outputs. Blank means none, never "all".</summary>
    public static IReadOnlyList<int> ParseGroup(LightGroup group) =>
        string.IsNullOrWhiteSpace(group.Channels)
            ? []
            : LightsController.ParseChannels(group.Channels, configured: string.Empty);

    private static TimeSpan Gap(LightGroup group)
    {
        var seconds = group.DelayBeforeSeconds;
        if (double.IsNaN(seconds) || seconds <= 0) return TimeSpan.Zero;

        var gap = TimeSpan.FromSeconds(seconds);
        return gap > MaxGap ? MaxGap : gap;
    }
}
