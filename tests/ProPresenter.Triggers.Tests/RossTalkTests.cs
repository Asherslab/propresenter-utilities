using ProPresenter.Triggers.Model;
using ProPresenter.Triggers.Signals;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Parsing of the RossTalk lines ProPresenter sends, and the signal matching that binds them to rules.
/// A rule stores the parsed key, so normalisation is part of the on-disk contract.
/// </summary>
public class RossTalkTests
{
    [Theory]
    [InlineData("GPI 1", "GPI 1")]
    [InlineData("GPI 12", "GPI 12")]
    [InlineData("CC 3", "CC 3")]
    [InlineData("CC 2:5", "CC 2:5")]
    [InlineData("CC 2/3/1", "CC 2/3/1")]
    [InlineData("FTB", "FTB")]
    [InlineData("KEYCUT ME:1 KEY:2", "KEYCUT ME:1 KEY:2")]
    public void Parses_the_commands_propresenter_sends(string line, string expected)
    {
        Assert.Equal(expected, RossTalk.ParseSignalKey(line));
    }

    [Theory]
    // Terminators are stripped by the line reader, but tolerate them arriving anyway.
    [InlineData("GPI 1\r", "GPI 1")]
    [InlineData("  GPI 1  ", "GPI 1")]
    // Verbs are upper-cased so a rule matches regardless of how the sender formatted the line.
    [InlineData("gpi 1", "GPI 1")]
    [InlineData("Cc 4", "CC 4")]
    // Extra internal whitespace is collapsed to a single space.
    [InlineData("GPI    7", "GPI 7")]
    [InlineData("GPI\t7", "GPI 7")]
    public void Normalises_formatting_differences(string line, string expected)
    {
        Assert.Equal(expected, RossTalk.ParseSignalKey(line));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData("\0")]
    public void Ignores_blank_and_keepalive_lines(string? line)
    {
        Assert.Null(RossTalk.ParseSignalKey(line));
    }

    [Theory]
    [InlineData("GPI 1", "RossTalk GPI trigger 1")]
    [InlineData("CC 3", "RossTalk custom control 3")]
    [InlineData("FTB", "RossTalk fade to black")]
    [InlineData("webhook:start-service", "Webhook “start-service”")]
    public void Describes_known_signals_for_the_ui(string key, string expected)
    {
        Assert.Equal(expected, RossTalk.Describe(key));
    }

    [Fact]
    public void Describes_an_unknown_signal_as_itself()
    {
        Assert.Equal("SOMETHING 9", RossTalk.Describe("SOMETHING 9"));
    }

    // ------------------------------------------------------------- matching

    private static TriggerRule SignalRule(string key) => new()
    {
        Source = TriggerSource.Signal,
        SignalKey = key,
    };

    [Fact]
    public void A_signal_rule_matches_its_own_key()
    {
        Assert.True(SignalRule("GPI 1").MatchesSignal("GPI 1"));
    }

    [Theory]
    [InlineData("gpi 1")]
    [InlineData("GPI 1 ")]
    public void Signal_matching_ignores_case_and_surrounding_space(string incoming)
    {
        Assert.True(SignalRule("GPI 1").MatchesSignal(incoming));
    }

    [Theory]
    [InlineData("GPI 2")]
    [InlineData("CC 1")]
    [InlineData("GPI 11")]
    public void A_signal_rule_does_not_match_a_different_signal(string incoming)
    {
        // "GPI 11" matters: a prefix or substring match would fire the wrong cue.
        Assert.False(SignalRule("GPI 1").MatchesSignal(incoming));
    }

    [Fact]
    public void A_rule_with_no_signal_key_matches_nothing()
    {
        Assert.False(SignalRule(string.Empty).MatchesSignal("GPI 1"));
        Assert.False(SignalRule("   ").MatchesSignal("GPI 1"));
    }

    [Fact]
    public void A_timer_rule_never_matches_a_signal()
    {
        // Guards against a rule switched from signal to timer still firing on the old signal.
        var rule = new TriggerRule { Source = TriggerSource.Timer, SignalKey = "GPI 1" };

        Assert.False(rule.MatchesSignal("GPI 1"));
    }

    // ---------------------------------------------------------- description

    [Fact]
    public void Signal_rules_describe_themselves_by_signal_not_timer_kind()
    {
        var rule = SignalRule("GPI 4");

        Assert.Equal("on signal GPI 4", rule.ConditionDescription);
        Assert.Equal("GPI 4", rule.SourceDisplay);
        Assert.False(rule.UsesOffset);
    }

    [Fact]
    public void A_delayed_signal_rule_says_so()
    {
        var rule = SignalRule("GPI 4");
        rule.Offset = TimeSpan.FromSeconds(90);

        Assert.Equal("00:01:30 after signal GPI 4", rule.ConditionDescription);
    }

    [Fact]
    public void A_signal_rule_with_no_key_reads_sensibly()
    {
        Assert.Equal("on signal (no signal set)", SignalRule(string.Empty).ConditionDescription);
        Assert.Equal("(no signal)", SignalRule(string.Empty).SourceDisplay);
    }
}
