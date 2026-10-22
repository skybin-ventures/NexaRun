namespace NexaRun.Shared;

public static class EnvironmentPairParser
{
    /// <summary>Parse KEY=VALUE pairs into a dictionary. Invalid entries are skipped.</summary>
    public static Dictionary<string, string>? Parse(IEnumerable<string> pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq].Trim();
            var value = pair[(eq + 1)..];
            if (key.Length == 0) continue;
            dict[key] = value;
        }

        return dict.Count == 0 ? null : dict;
    }
}
