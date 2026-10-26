using System.Globalization;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Parses durations such as 30m, 2h, 7d, 2w, 1mo, 1y, 0 or perm.
/// A bare number is minutes. Returns null for a permanent duration.
/// </summary>
internal static class DurationParser
{
    private const double MaxMinutes = 3650d * 24d * 60d;

    public static bool TryParse(string? input, out TimeSpan? duration)
    {
        duration = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim().ToLowerInvariant();

        if (value is "0" or "perm" or "permanent" or "forever" or "none")
        {
            return true;
        }

        var numberLength = 0;
        while (numberLength < value.Length && (char.IsDigit(value[numberLength]) || value[numberLength] == '.'))
        {
            numberLength++;
        }

        if (numberLength == 0
            || !double.TryParse(value[..numberLength], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return false;
        }

        var suffix = value[numberLength..];
        var minutes = suffix switch
        {
            "" or "m" or "min" or "mins" => amount,
            "s" or "sec" or "secs" => amount / 60d,
            "h" or "hr" or "hrs" => amount * 60d,
            "d" or "day" or "days" => amount * 60d * 24d,
            "w" or "week" or "weeks" => amount * 60d * 24d * 7d,
            "mo" or "month" or "months" => amount * 60d * 24d * 30d,
            "y" or "year" or "years" => amount * 60d * 24d * 365d,
            _ => double.NaN
        };

        if (double.IsNaN(minutes) || minutes < 0 || minutes > MaxMinutes)
        {
            return false;
        }

        duration = TimeSpan.FromMinutes(minutes);
        return true;
    }


    public static string Format(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "permanent";
        }

        var totalMinutes = Math.Max(0, (long)Math.Ceiling(duration.Value.TotalMinutes));

        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }

        var totalHours = totalMinutes / 60;
        if (totalHours < 24)
        {
            return $"{totalHours}h";
        }

        var totalDays = totalHours / 24;
        return totalDays < 30 ? $"{totalDays}d" : $"{totalDays / 30}mo";
    }
}
