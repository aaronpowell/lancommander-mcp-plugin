using System.ComponentModel;
using LANCommander.SDK.Models.Manifest;
using LANCommander.Server.Plugins;
using ModelContextProtocol.Server;

namespace LANCommander.Mcp;

[McpServerToolType]
public sealed class MetadataTools(IMetadataLookup metadata)
{
    [McpServerTool(Name = "list_metadata_providers", ReadOnly = true, Idempotent = true)]
    [Description("List configured metadata provider names. Use one of these names for metadata search and lookup.")]
    public IReadOnlyList<string> ListProviders() => metadata.GetProviderNames();

    [McpServerTool(Name = "search_metadata", ReadOnly = true, Idempotent = true)]
    [Description("Search a configured external metadata provider for games. Does not import or modify catalog records.")]
    public Task<MetadataSearchPage> Search(string providerName, string search, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return metadata.SearchGamesAsync(ToolInput.Name(providerName), ToolInput.Name(search), limit, offset, cancellationToken);
    }

    [McpServerTool(Name = "get_metadata", ReadOnly = true, Idempotent = true)]
    [Description("Fetch full external game metadata using the provider's game id, not a catalog GUID. Does not import the game.")]
    public async Task<Game> Get(string providerName, string gameId, CancellationToken cancellationToken) =>
        ToolInput.Found(await metadata.GetGameAsync(ToolInput.Name(providerName), ToolInput.Name(gameId), cancellationToken));
}
