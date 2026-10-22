namespace NexaRun.Shared;

/// <summary>
/// Minimal 5-field cron matcher (minute hour day-of-month month day-of-week), local time.
/// Supports <c>*</c>, single numbers, lists (<c>1,2,5</c>), ranges (<c>1-5</c>), and steps (<c>*/5</c>, <c>1-10/2</c>).
/// Day-of-week: 0–6 or 7 = Sunday (PM2/cron style).
/// When both day-of-month and day-of-week are constrained (neither is <c>*</c>), either may match (Vixie OR).
/// </summary>
public static class CronExpression
{
    public static bool IsMatch(string? expression, DateTime localTime)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parts = expression.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        try
        {
            if (!FieldMatches(parts[0], localTime.Minute, 0, 59))
                return false;
            if (!FieldMatches(parts[1], localTime.Hour, 0, 23))
                return false;
            if (!FieldMatches(parts[3], localTime.Month, 1, 12))
                return false;

            var domStar = IsUnconstrained(parts[2]);
            var dowStar = IsUnconstrained(parts[4]);
            var domOk = FieldMatches(parts[2], localTime.Day, 1, 31);
            var dowOk = DowMatches(parts[4], (int)localTime.DayOfWeek);

            // Vixie: when both DOM and DOW are restricted, match if either matches.
            if (!domStar && !dowStar)
                return domOk || dowOk;

            return domOk && dowOk;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryValidate(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var parts = expression.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            error = "Cron must have 5 fields: minute hour day month weekday.";
            return false;
        }

        try
        {
            ExpandField(parts[0], 0, 59);
            ExpandField(parts[1], 0, 23);
            ExpandField(parts[2], 1, 31);
            ExpandField(parts[3], 1, 12);
            ExpandField(parts[4], 0, 7);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsUnconstrained(string field) =>
        string.IsNullOrWhiteSpace(field) || field.Trim() == "*";

    private static bool DowMatches(string field, int dow)
    {
        // .NET DayOfWeek: Sunday=0 … Saturday=6. Cron often allows 7 = Sunday.
        if (IsUnconstrained(field)) return true;
        foreach (var value in ExpandField(field, 0, 7))
        {
            var normalized = value == 7 ? 0 : value;
            if (normalized == dow) return true;
        }

        return false;
    }

    private static bool FieldMatches(string field, int value, int min, int max)
    {
        if (IsUnconstrained(field)) return true;
        return ExpandField(field, min, max).Contains(value);
    }

    private static HashSet<int> ExpandField(string field, int min, int max)
    {
        var set = new HashSet<int>();
        foreach (var segment in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var step = 1;
            var body = segment;
            var slash = segment.IndexOf('/');
            if (slash >= 0)
            {
                body = segment[..slash];
                step = int.Parse(segment[(slash + 1)..]);
                if (step < 1)
                    throw new FormatException($"Invalid cron step in '{field}'.");
            }

            int start, end;
            if (body == "*" || body.Length == 0)
            {
                start = min;
                end = max;
            }
            else if (body.Contains('-'))
            {
                var range = body.Split('-', 2);
                start = int.Parse(range[0]);
                end = int.Parse(range[1]);
            }
            else
            {
                start = end = int.Parse(body);
            }

            if (start < min || end > max || start > end)
                throw new FormatException($"Cron field out of range: '{field}'.");

            for (var i = start; i <= end; i += step)
                set.Add(i);
        }

        return set;
    }
}
