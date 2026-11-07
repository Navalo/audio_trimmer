using System.Globalization;
using AudioTrimmer.Abstractions;

namespace AudioTrimmer.Input;

/// <summary>Accepts "90", "1:30", "00:01:30", "00:00:10.5". Plain numbers are seconds (not days!).</summary>
public sealed class TimeParser : ITimeParser
{
    public bool TryParse(string? input, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parts = input.Trim().Split(':');
        if (parts.Length > 3) return false;

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
                return false;
            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }
}
