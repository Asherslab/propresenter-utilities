using ProPresenter.Triggers.Lights;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Tests;

public class LightSequenceTests
{
    private static readonly List<LightGroup> WidesThenCenters =
    [
        new() { Name = "Wides", Channels = "0,1", DelayBeforeSeconds = 0 },
        new() { Name = "Centers", Channels = "2,3", DelayBeforeSeconds = 0.1 },
    ];

    private static string Describe(IReadOnlyList<LightStep> steps) =>
        string.Join(" | ", steps.Select(s => $"{s.At.TotalMilliseconds:0}ms {s.Name ?? "-"} [{string.Join(",", s.Channels)}]"));

    [Fact]
    public void Turning_on_runs_the_groups_first_to_last()
    {
        Assert.Equal("0ms Wides [0,1] | 100ms Centers [2,3]",
            Describe(LightSequence.Plan(WidesThenCenters, [0, 1, 2, 3], on: true)));
    }

    [Fact]
    public void Turning_off_runs_them_last_to_first_with_the_same_gap()
    {
        Assert.Equal("0ms Centers [2,3] | 100ms Wides [0,1]",
            Describe(LightSequence.Plan(WidesThenCenters, [0, 1, 2, 3], on: false)));
    }

    [Fact]
    public void Each_gap_stays_between_the_same_two_groups_in_both_directions()
    {
        List<LightGroup> three =
        [
            new() { Name = "A", Channels = "0", DelayBeforeSeconds = 5 },
            new() { Name = "B", Channels = "1", DelayBeforeSeconds = 0.1 },
            new() { Name = "C", Channels = "2", DelayBeforeSeconds = 0.3 },
        ];

        Assert.Equal("0ms A [0] | 100ms B [1] | 400ms C [2]", Describe(LightSequence.Plan(three, [0, 1, 2], on: true)));
        Assert.Equal("0ms C [2] | 300ms B [1] | 400ms A [0]", Describe(LightSequence.Plan(three, [0, 1, 2], on: false)));
    }

    [Fact]
    public void No_groups_means_everything_together()
    {
        Assert.Equal("0ms - [0,1,2,3]", Describe(LightSequence.Plan([], [0, 1, 2, 3], on: true)));
    }

    [Fact]
    public void Only_the_requested_outputs_switch_and_unused_groups_add_no_gap()
    {
        Assert.Equal("0ms Centers [2,3]", Describe(LightSequence.Plan(WidesThenCenters, [2, 3], on: true)));
    }

    [Fact]
    public void Requested_outputs_in_no_group_join_the_first_step()
    {
        List<LightGroup> partial = [new() { Name = "Centers", Channels = "2,3" }];

        Assert.Equal("0ms Centers [2,3,0,1]", Describe(LightSequence.Plan(partial, [0, 1, 2, 3], on: false)));
    }

    [Fact]
    public void An_output_listed_twice_belongs_to_the_first_group()
    {
        List<LightGroup> overlapping =
        [
            new() { Name = "A", Channels = "0,1" },
            new() { Name = "B", Channels = "1,2", DelayBeforeSeconds = 0.2 },
        ];

        Assert.Equal("0ms A [0,1] | 200ms B [2]", Describe(LightSequence.Plan(overlapping, [0, 1, 2], on: true)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(60, 10000)]
    public void Gaps_are_clamped(double seconds, double expectedMs)
    {
        List<LightGroup> groups =
        [
            new() { Name = "A", Channels = "0" },
            new() { Name = "B", Channels = "1", DelayBeforeSeconds = seconds },
        ];

        Assert.Equal(expectedMs, LightSequence.Plan(groups, [0, 1], on: true)[1].At.TotalMilliseconds);
    }

    [Fact]
    public void A_group_with_bad_outputs_is_an_error_not_a_guess()
    {
        List<LightGroup> bad = [new() { Name = "Wides", Channels = "left,right" }];

        Assert.Throws<FormatException>(() => LightSequence.Plan(bad, [0, 1], on: true));
    }

    [Fact]
    public void A_blank_group_switches_nothing_rather_than_everything()
    {
        Assert.Empty(LightSequence.ParseGroup(new LightGroup { Name = "Empty", Channels = " " }));
    }
}
