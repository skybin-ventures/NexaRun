using NexaRun.Shared;

namespace NexaRun.Tests;

public class CronExpressionTests
{
    [Fact]
    public void IsMatch_every_minute()
    {
        var t = new DateTime(2026, 10, 22, 14, 30, 0);
        Assert.True(CronExpression.IsMatch("* * * * *", t));
    }

    [Fact]
    public void IsMatch_specific_hour_and_minute()
    {
        var t = new DateTime(2026, 10, 22, 3, 0, 0);
        Assert.True(CronExpression.IsMatch("0 3 * * *", t));
        Assert.False(CronExpression.IsMatch("0 3 * * *", t.AddMinutes(1)));
    }

    [Fact]
    public void TryValidate_rejects_bad_field_count()
    {
        Assert.False(CronExpression.TryValidate("0 3 * *", out var error));
        Assert.Contains("5 fields", error);
    }

    [Fact]
    public void IsMatch_step_minutes()
    {
        var t = new DateTime(2026, 10, 22, 14, 10, 0);
        Assert.True(CronExpression.IsMatch("*/5 * * * *", t));
        Assert.False(CronExpression.IsMatch("*/5 * * * *", t.AddMinutes(1)));
    }

    [Fact]
    public void IsMatch_dom_or_dow_when_both_constrained()
    {
        // 0 0 1 * 1 — midnight on the 1st OR on Monday (Vixie OR)
        var mondayNotFirst = new DateTime(2026, 10, 19, 0, 0, 0); // Monday
        Assert.Equal(DayOfWeek.Monday, mondayNotFirst.DayOfWeek);
        Assert.NotEqual(1, mondayNotFirst.Day);
        Assert.True(CronExpression.IsMatch("0 0 1 * 1", mondayNotFirst));

        var firstNotMonday = new DateTime(2026, 10, 1, 0, 0, 0); // Thursday
        Assert.Equal(1, firstNotMonday.Day);
        Assert.NotEqual(DayOfWeek.Monday, firstNotMonday.DayOfWeek);
        Assert.True(CronExpression.IsMatch("0 0 1 * 1", firstNotMonday));

        var neither = new DateTime(2026, 10, 2, 0, 0, 0); // Friday, not 1st
        Assert.False(CronExpression.IsMatch("0 0 1 * 1", neither));
    }

    [Fact]
    public void IsMatch_dom_and_dow_when_one_is_star()
    {
        var t = new DateTime(2026, 10, 1, 0, 0, 0);
        Assert.True(CronExpression.IsMatch("0 0 1 * *", t));
        Assert.False(CronExpression.IsMatch("0 0 2 * *", t));
    }
}
