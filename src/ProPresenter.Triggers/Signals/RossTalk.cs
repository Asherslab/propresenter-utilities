namespace ProPresenter.Triggers.Signals;

/// <summary>
/// Parsing for the RossTalk control protocol, as emitted by ProPresenter's RossTalk device.
/// </summary>
/// <remarks>
/// <para>
/// RossTalk is a plain-text, line-oriented protocol over TCP (conventionally port 7788). Each line is
/// a case-sensitive verb followed by optional arguments, terminated by <c>\n</c> or <c>\r\n</c>.
/// ProPresenter can send <c>GPI</c> (Trigger GPI), <c>CC</c> (Custom Control), <c>FTB</c>
/// (fade to black) and the key transition verbs.
/// </para>
/// <para>
/// This app only cares about identity, not semantics: each distinct command becomes a signal key that
/// a trigger rule can be attached to. Verbs are upper-cased and whitespace collapsed so that the key a
/// rule stores is stable regardless of how the sender formats the line.
/// </para>
/// </remarks>
public static class RossTalk
{
    /// <summary>The conventional RossTalk port, and ProPresenter's default.</summary>
    public const int DefaultPort = 7788;

    /// <summary>
    /// Converts a received line into a signal key, or null when the line carries nothing usable
    /// (blank lines and keep-alives).
    /// </summary>
    public static string? ParseSignalKey(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        // Strip stray control characters some senders append.
        var trimmed = line.Trim().Trim('\0');
        if (trimmed.Length == 0) return null;

        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var verb = parts[0].ToUpperInvariant();
        return parts.Length == 1 ? verb : $"{verb} {string.Join(' ', parts.Skip(1))}";
    }

    /// <summary>
    /// A friendly description of a signal key, for the UI. Recognises the verbs ProPresenter sends and
    /// falls back to the raw key for anything else.
    /// </summary>
    public static string Describe(string signalKey)
    {
        var parts = signalKey.Split(' ', 2);
        var verb = parts[0].ToUpperInvariant();
        var argument = parts.Length > 1 ? parts[1] : string.Empty;

        return verb switch
        {
            "GPI" => $"RossTalk GPI trigger {argument}".TrimEnd(),
            "CC" => $"RossTalk custom control {argument}".TrimEnd(),
            "FTB" => "RossTalk fade to black",
            "KEYCUT" => $"RossTalk key cut {argument}".TrimEnd(),
            "KEYTRANS" => $"RossTalk key transition {argument}".TrimEnd(),
            _ when signalKey.StartsWith("webhook:", StringComparison.OrdinalIgnoreCase) =>
                $"Webhook “{signalKey["webhook:".Length..]}”",
            _ => signalKey,
        };
    }
}
