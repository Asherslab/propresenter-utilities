using ProPresenter.Api;

namespace ProPresenter.Triggers.Tests;

public class TimeCodeTests
{
    [Theory]
    [InlineData("00:00:00", 0)]
    [InlineData("00:05:00", 300)]
    [InlineData("05:00:00", 18000)]
    [InlineData("00:21:43", 1303)]
    [InlineData("-00:00:02", -2)]
    [InlineData("00:00:01.50", 1.5)]
    [InlineData("-00:01:30.00", -90)]
    [InlineData("01:30", 90)]
    [InlineData("45", 45)]
    public void Parses_propresenter_clock_strings(string input, double expectedSeconds)
    {
        Assert.True(TimeCode.TryParse(input, out var parsed));
        Assert.Equal(expectedSeconds, parsed.TotalSeconds, precision: 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a time")]
    [InlineData("1:2:3:4")]
    public void Rejects_unparseable_input(string? input)
    {
        Assert.False(TimeCode.TryParse(input, out _));
        Assert.Equal(TimeSpan.Zero, TimeCode.Parse(input));
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(300, "00:05:00")]
    [InlineData(-2, "-00:00:02")]
    [InlineData(18000, "05:00:00")]
    public void Formats_round_trip(double seconds, string expected)
    {
        Assert.Equal(expected, TimeCode.Format(TimeSpan.FromSeconds(seconds)));
    }
}
