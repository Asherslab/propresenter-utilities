using System.Globalization;

namespace ProPresenter.Api;

/// <summary>
/// Parsing and formatting for ProPresenter's clock strings.
/// </summary>
/// <remarks>
/// ProPresenter reports times as <c>HH:MM:SS</c>, sometimes with a fractional part (<c>HH:MM:SS.ff</c>),
/// and prefixes a <c>-</c> when a timer is in overrun. Observed streams tick at whole-second resolution
/// once per second, so the fractional component is generally <c>.00</c>, but it is parsed when present.
/// </remarks>
public static class TimeCode
{
    /// <summary>Parses a ProPresenter clock string. Returns <see cref="TimeSpan.Zero"/> for unparseable input.</summary>
    public static TimeSpan Parse(string? value) => TryParse(value, out var result) ? result : TimeSpan.Zero;

    public static bool TryParse(string? value, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var span = value.AsSpan().Trim();
        var negative = false;
        if (span[0] is '-' or '+')
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        // Split on ':' into at most 3 parts, tolerating "MM:SS" and bare "SS" as well as "HH:MM:SS".
        Span<Range> parts = stackalloc Range[4];
        var count = span.Split(parts, ':');
        if (count is 0 or > 3) return false;

        double hours = 0, minutes = 0, seconds;

        if (count == 3)
        {
            if (!double.TryParse(span[parts[0]], CultureInfo.InvariantCulture, out hours)) return false;
            if (!double.TryParse(span[parts[1]], CultureInfo.InvariantCulture, out minutes)) return false;
            if (!double.TryParse(span[parts[2]], CultureInfo.InvariantCulture, out seconds)) return false;
        }
        else if (count == 2)
        {
            if (!double.TryParse(span[parts[0]], CultureInfo.InvariantCulture, out minutes)) return false;
            if (!double.TryParse(span[parts[1]], CultureInfo.InvariantCulture, out seconds)) return false;
        }
        else
        {
            if (!double.TryParse(span[parts[0]], CultureInfo.InvariantCulture, out seconds)) return false;
        }

        var total = TimeSpan.FromSeconds((hours * 3600) + (minutes * 60) + seconds);
        result = negative ? -total : total;
        return true;
    }

    /// <summary>Formats as <c>HH:MM:SS</c>, with a leading <c>-</c> for negative values.</summary>
    public static string Format(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        return $"{sign}{(int)abs.TotalHours:00}:{abs.Minutes:00}:{abs.Seconds:00}";
    }

    /// <summary>Formats without an hours component when the value is under an hour, for compact display.</summary>
    public static string FormatCompact(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        return abs.TotalHours >= 1
            ? $"{sign}{(int)abs.TotalHours}:{abs.Minutes:00}:{abs.Seconds:00}"
            : $"{sign}{abs.Minutes:00}:{abs.Seconds:00}";
    }
}
