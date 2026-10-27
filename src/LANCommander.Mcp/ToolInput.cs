using ModelContextProtocol;

namespace LANCommander.Mcp;

public sealed record CatalogPage<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);

internal static class ToolInput
{
    public static Guid Id(Guid id)
    {
        if (id == Guid.Empty)
            throw new McpException("A non-empty record id is required.");
        return id;
    }

    public static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("A non-empty name or title is required.");
        return name;
    }

    public static T Found<T>(T? value) where T : class =>
        value ?? throw new McpException("The requested record does not exist.");

    public static void PageBounds(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw new McpException("Offset must be non-negative and limit must be between 1 and 100.");
    }

    public static CatalogPage<T> Page<T>(
        IReadOnlyList<T> records, Func<T, string> name, string? search, int offset, int limit)
    {
        PageBounds(offset, limit);
        var matches = records.Where(item => string.IsNullOrWhiteSpace(search)
            || name(item).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(matches.Skip(offset).Take(limit).ToArray(), matches.Length, offset, limit);
    }
}
