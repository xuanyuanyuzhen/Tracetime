namespace TraceTime.Core;

public static class ApplicationQuery
{
    public static bool Matches(string name, string? query) => string.IsNullOrWhiteSpace(query)
        || name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}
