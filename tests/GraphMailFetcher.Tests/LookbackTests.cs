namespace GraphMailFetcher.Tests;

public class LookbackTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData("1d", "2026-11-12")]
    [InlineData("2d", "2026-11-11")]
    [InlineData("7d", "2026-11-06")]
    [InlineData("14D", "2026-10-30")]
    [InlineData("1w", "2026-11-06")]
    [InlineData("1mo", "2026-10-13")]
    [InlineData(" 3mo ", "2026-08-13")]
    [InlineData("1y", "2025-11-13")]
    public void StartsOnTheExpectedDay(string spec, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), Lookback.GetStartDate(spec, Today));

    [Theory]
    [InlineData("")]
    [InlineData("0d")]
    [InlineData("7")]
    [InlineData("d")]
    [InlineData("-2d")]
    [InlineData("2h")]
    [InlineData("1.5d")]
    [InlineData("99999y")]
    public void RejectsInvalidPeriods(string spec) =>
        Assert.Throws<UsageException>(() => Lookback.GetStartDate(spec, Today));

    [Fact]
    public void StartIsLocalMidnightConvertedToUtc()
    {
        var brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        // 00:30 local on 12 Nov is still 11 Nov in UTC; "today" must follow the local calendar.
        var now = new DateTimeOffset(2026, 11, 11, 23, 30, 0, TimeSpan.Zero);

        var start = Lookback.GetStartUtc("2d", now, brussels);

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 23, 0, 0, TimeSpan.Zero), start);
    }
}
