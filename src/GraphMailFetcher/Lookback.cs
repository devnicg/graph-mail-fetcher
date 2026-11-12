using System.Globalization;
using System.Text.RegularExpressions;

namespace GraphMailFetcher;

/// <summary>Parses look-back periods such as 2d, 2w, 1mo or 1y. Today always counts as the first day.</summary>
internal static partial class Lookback
{
    [GeneratedRegex(@"^\s*(\d+)\s*(d|w|mo|y)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>Returns the first local calendar day that is included in the period.</summary>
    public static DateOnly GetStartDate(string spec, DateOnly today)
    {
        var match = Pattern().Match(spec);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
            throw new UsageException($"Invalid look-back period '{spec}'. Use <n>d, <n>w, <n>mo or <n>y with n >= 1, e.g. 7d, 2w, 1mo, 1y.");

        try
        {
            // The period ends today, so "2d" is today and yesterday and "1mo" starts the day after the same date last month.
            return match.Groups[2].Value.ToLowerInvariant() switch
            {
                "d" => today.AddDays(1 - n),
                "w" => today.AddDays(1 - 7 * n),
                "mo" => today.AddMonths(-n).AddDays(1),
                _ => today.AddYears(-n).AddDays(1),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new UsageException($"Look-back period '{spec}' is too long.");
        }
    }

    /// <summary>Returns the UTC instant at which the period starts (local midnight of the first day).</summary>
    public static DateTimeOffset GetStartUtc(string spec, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var midnight = GetStartDate(spec, today).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(midnight)) // midnight skipped by a DST transition
            midnight = midnight.AddMinutes(30);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, timeZone), TimeSpan.Zero);
    }
}
