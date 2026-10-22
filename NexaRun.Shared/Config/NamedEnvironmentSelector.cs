namespace NexaRun.Shared.Config;

/// <summary>
/// PM2-style named env blocks: base <c>env</c> plus <c>env_production</c> / <c>env_development</c>, etc.
/// </summary>
public static class NamedEnvironmentSelector
{
    /// <summary>
    /// Merge a named block onto <paramref name="baseEnv"/>.
    /// Name may be <c>production</c> or <c>env_production</c>.
    /// </summary>
    public static Dictionary<string, string>? Merge(
        Dictionary<string, string>? baseEnv,
        IReadOnlyDictionary<string, Dictionary<string, string>>? namedBlocks,
        string? envName)
    {
        if (string.IsNullOrWhiteSpace(envName) || namedBlocks == null || namedBlocks.Count == 0)
            return baseEnv;

        var block = FindBlock(namedBlocks, envName.Trim());
        if (block == null || block.Count == 0)
            return baseEnv;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (baseEnv != null)
        {
            foreach (var (k, v) in baseEnv)
                merged[k] = v;
        }

        foreach (var (k, v) in block)
            merged[k] = v;

        return merged;
    }

    public static Dictionary<string, string>? FindBlock(
        IReadOnlyDictionary<string, Dictionary<string, string>> namedBlocks,
        string envName)
    {
        if (namedBlocks.TryGetValue(envName, out var exact))
            return exact;

        var withPrefix = envName.StartsWith("env_", StringComparison.OrdinalIgnoreCase)
            ? envName
            : "env_" + envName;

        if (namedBlocks.TryGetValue(withPrefix, out var prefixed))
            return prefixed;

        foreach (var (key, value) in namedBlocks)
        {
            if (key.Equals(envName, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(withPrefix, StringComparison.OrdinalIgnoreCase))
                return value;

            // env_production matches name "production"
            if (key.StartsWith("env_", StringComparison.OrdinalIgnoreCase) &&
                key[4..].Equals(envName, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }
}
