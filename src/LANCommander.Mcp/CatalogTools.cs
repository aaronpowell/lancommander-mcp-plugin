using System.ComponentModel;
using LANCommander.Server.Plugins;
using ModelContextProtocol.Server;

namespace LANCommander.Mcp;

[McpServerToolType]
public sealed class CatalogTools(
    IGameProvider games,
    IGameVersionProvider versions,
    ITaxonomyProvider taxonomy,
    IToolProvider tools,
    IRedistributableProvider redistributables,
    IServerProvider servers)
{
    [McpServerTool(Name = "list_games", ReadOnly = true, Idempotent = true)]
    [Description("Search games and add-ons by title, including unpublished records. Returns a bounded page and total match count.")]
    public async Task<CatalogPage<GameInfo>> ListGames(string? search = null, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await games.GetAsync(cancellationToken), x => x.Title, search, offset, limit);
    }

    [McpServerTool(Name = "get_game", ReadOnly = true, Idempotent = true)]
    [Description("Read every field of a game or add-on. Read immediately before updating.")]
    public async Task<GameInfo> GetGame(Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await games.GetAsync(ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_game", Destructive = false)]
    [Description("Create a game or add-on. Leave id empty; specify title and other desired fields. Does not upload an archive.")]
    public Task<GameInfo> CreateGame(GameInfo game, CancellationToken cancellationToken)
    {
        ToolInput.Name(game.Title);
        return games.AddAsync(game with { Id = Guid.Empty }, cancellationToken);
    }

    [McpServerTool(Name = "update_game", Destructive = true)]
    [Description("Replace ALL writable game fields. First get_game, then change only intended fields and submit the full record; omitted fields reset to defaults.")]
    public Task<GameInfo> UpdateGame(GameInfo game, CancellationToken cancellationToken)
    {
        ToolInput.Id(game.Id);
        ToolInput.Name(game.Title);
        return games.UpdateAsync(game, cancellationToken);
    }

    [McpServerTool(Name = "delete_game", Destructive = true, Idempotent = true)]
    [Description("Permanently delete a game, its archives and media, INCLUDING FILES. Obtain explicit user approval before calling.")]
    public Task DeleteGame(Guid id, CancellationToken cancellationToken) =>
        games.DeleteAsync(ToolInput.Id(id), cancellationToken);

    [McpServerTool(Name = "get_game_links", ReadOnly = true, Idempotent = true)]
    [Description("Read ids linked to a game for one kind: categories, collections, developers, genres, platforms, publishers, tags, redistributables, or tools.")]
    public Task<IReadOnlyList<Guid>> GetGameLinks(Guid gameId, GameLinkKind kind, CancellationToken cancellationToken) =>
        games.GetLinksAsync(ToolInput.Id(gameId), kind, cancellationToken);

    [McpServerTool(Name = "set_game_links", Destructive = true, Idempotent = true)]
    [Description("Replace ALL game links of the chosen kind. Read existing links first; an empty ids array unlinks everything of that kind.")]
    public Task SetGameLinks(Guid gameId, GameLinkKind kind, Guid[] ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
            ToolInput.Id(id);
        return games.SetLinksAsync(ToolInput.Id(gameId), kind, ids, cancellationToken);
    }

    [McpServerTool(Name = "list_game_versions", ReadOnly = true, Idempotent = true)]
    [Description("Read a game's versions, oldest first, in bounded pages.")]
    public async Task<CatalogPage<GameVersionInfo>> ListGameVersions(Guid gameId, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await versions.GetAsync(ToolInput.Id(gameId), cancellationToken), x => x.Version, null, offset, limit);
    }

    [McpServerTool(Name = "get_game_version", ReadOnly = true, Idempotent = true)]
    [Description("Read every field of a game version immediately before updating it.")]
    public async Task<GameVersionInfo> GetGameVersion(Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await versions.GetVersionAsync(ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_game_version", Destructive = true)]
    [Description("Create a version after the latest, carrying over its actions, scripts, save paths, options and redistributables. Also PUBLISHES an existing latest draft.")]
    public Task<GameVersionInfo> CreateGameVersion(Guid gameId, string version, string? changelog = null, CancellationToken cancellationToken = default) =>
        versions.CreateAsync(ToolInput.Id(gameId), ToolInput.Name(version), changelog, cancellationToken);

    [McpServerTool(Name = "update_game_version", Destructive = true)]
    [Description("Replace ALL writable version fields: number, changelog, sort order, published state and option schema. Get the version first and preserve unrelated fields.")]
    public Task<GameVersionInfo> UpdateGameVersion(GameVersionInfo version, CancellationToken cancellationToken)
    {
        ToolInput.Id(version.Id);
        ToolInput.Name(version.Version);
        return versions.UpdateAsync(version, cancellationToken);
    }

    [McpServerTool(Name = "delete_game_version", Destructive = true, Idempotent = true)]
    [Description("Permanently delete a version, its archive FILE, actions, scripts and save paths. Obtain explicit user approval before calling.")]
    public Task DeleteGameVersion(Guid id, CancellationToken cancellationToken) =>
        versions.DeleteAsync(ToolInput.Id(id), cancellationToken);

    [McpServerTool(Name = "list_taxonomy", ReadOnly = true, Idempotent = true)]
    [Description("Search taxonomy by name for a kind: Category, Collection, Company, Engine, Genre, Platform or Tag.")]
    public async Task<CatalogPage<TaxonomyInfo>> ListTaxonomy(TaxonomyKind kind, string? search = null, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await taxonomy.GetAsync(kind, cancellationToken), x => x.Name, search, offset, limit);
    }

    [McpServerTool(Name = "get_taxonomy", ReadOnly = true, Idempotent = true)]
    [Description("Read a taxonomy record by kind and id.")]
    public async Task<TaxonomyInfo> GetTaxonomy(TaxonomyKind kind, Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await taxonomy.GetAsync(kind, ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_taxonomy", Destructive = false)]
    [Description("Create a named taxonomy record of a kind. Duplicate names are allowed; search first to avoid duplicates.")]
    public Task<TaxonomyInfo> CreateTaxonomy(TaxonomyKind kind, string name, CancellationToken cancellationToken) =>
        taxonomy.AddAsync(kind, ToolInput.Name(name), cancellationToken);

    [McpServerTool(Name = "rename_taxonomy", Destructive = true, Idempotent = true)]
    [Description("Rename a taxonomy record without changing its id or game links.")]
    public Task<TaxonomyInfo> RenameTaxonomy(TaxonomyKind kind, Guid id, string name, CancellationToken cancellationToken) =>
        taxonomy.RenameAsync(kind, ToolInput.Id(id), ToolInput.Name(name), cancellationToken);

    [McpServerTool(Name = "delete_taxonomy", Destructive = true, Idempotent = true)]
    [Description("Delete a taxonomy record and unlink it from EVERY game. Obtain explicit user approval before calling.")]
    public Task DeleteTaxonomy(TaxonomyKind kind, Guid id, CancellationToken cancellationToken) =>
        taxonomy.DeleteAsync(kind, ToolInput.Id(id), cancellationToken);

    [McpServerTool(Name = "list_tools", ReadOnly = true, Idempotent = true)]
    [Description("Search installable catalog tools by name. These are LANCommander records, not MCP tools.")]
    public async Task<CatalogPage<ToolInfo>> ListTools(string? search = null, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await tools.GetAsync(cancellationToken), x => x.Name, search, offset, limit);
    }

    [McpServerTool(Name = "get_tool", ReadOnly = true, Idempotent = true)]
    [Description("Read every field of an installable catalog tool immediately before updating.")]
    public async Task<ToolInfo> GetTool(Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await tools.GetAsync(ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_tool", Destructive = false)]
    [Description("Create an installable catalog tool. Leave id empty; does not upload an archive.")]
    public Task<ToolInfo> CreateTool(ToolInfo tool, CancellationToken cancellationToken)
    {
        ToolInput.Name(tool.Name);
        return tools.AddAsync(tool with { Id = Guid.Empty }, cancellationToken);
    }

    [McpServerTool(Name = "update_tool", Destructive = true)]
    [Description("Replace ALL writable catalog tool fields. Get the full record first and preserve unrelated fields.")]
    public Task<ToolInfo> UpdateTool(ToolInfo tool, CancellationToken cancellationToken)
    {
        ToolInput.Id(tool.Id);
        ToolInput.Name(tool.Name);
        return tools.UpdateAsync(tool, cancellationToken);
    }

    [McpServerTool(Name = "delete_tool", Destructive = true, Idempotent = true)]
    [Description("Permanently delete a catalog tool and its archive FILES, unlinking it from every game. Obtain explicit user approval before calling.")]
    public Task DeleteTool(Guid id, CancellationToken cancellationToken) =>
        tools.DeleteAsync(ToolInput.Id(id), cancellationToken);

    [McpServerTool(Name = "list_redistributables", ReadOnly = true, Idempotent = true)]
    [Description("Search catalog redistributables by name, returning a bounded page.")]
    public async Task<CatalogPage<RedistributableInfo>> ListRedistributables(string? search = null, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await redistributables.GetAsync(cancellationToken), x => x.Name, search, offset, limit);
    }

    [McpServerTool(Name = "get_redistributable", ReadOnly = true, Idempotent = true)]
    [Description("Read every redistributable field immediately before updating.")]
    public async Task<RedistributableInfo> GetRedistributable(Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await redistributables.GetAsync(ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_redistributable", Destructive = false)]
    [Description("Create a catalog redistributable. Leave id empty; does not upload an archive.")]
    public Task<RedistributableInfo> CreateRedistributable(RedistributableInfo redistributable, CancellationToken cancellationToken)
    {
        ToolInput.Name(redistributable.Name);
        return redistributables.AddAsync(redistributable with { Id = Guid.Empty }, cancellationToken);
    }

    [McpServerTool(Name = "update_redistributable", Destructive = true)]
    [Description("Replace ALL writable redistributable fields. Get the full record first and preserve unrelated fields.")]
    public Task<RedistributableInfo> UpdateRedistributable(RedistributableInfo redistributable, CancellationToken cancellationToken)
    {
        ToolInput.Id(redistributable.Id);
        ToolInput.Name(redistributable.Name);
        return redistributables.UpdateAsync(redistributable, cancellationToken);
    }

    [McpServerTool(Name = "delete_redistributable", Destructive = true, Idempotent = true)]
    [Description("Permanently delete a redistributable and its archive FILES, unlinking it from every game. Obtain explicit user approval before calling.")]
    public Task DeleteRedistributable(Guid id, CancellationToken cancellationToken) =>
        redistributables.DeleteAsync(ToolInput.Id(id), cancellationToken);

    [McpServerTool(Name = "list_servers", ReadOnly = true, Idempotent = true)]
    [Description("Search dedicated game server configuration by name. Does not start or stop servers or expose console passwords.")]
    public async Task<CatalogPage<ServerInfo>> ListServers(string? search = null, int offset = 0, int limit = 25, CancellationToken cancellationToken = default)
    {
        ToolInput.PageBounds(offset, limit);
        return ToolInput.Page(await servers.GetAsync(cancellationToken), x => x.Name, search, offset, limit);
    }

    [McpServerTool(Name = "get_server", ReadOnly = true, Idempotent = true)]
    [Description("Read every game server configuration field immediately before updating. Does not expose console passwords.")]
    public async Task<ServerInfo> GetServer(Guid id, CancellationToken cancellationToken) =>
        ToolInput.Found(await servers.GetAsync(ToolInput.Id(id), cancellationToken));

    [McpServerTool(Name = "create_server", Destructive = true)]
    [Description("Create game server configuration, including executable, arguments and autostart settings. Leave id empty. Obtain approval for executable/script paths and autostart.")]
    public Task<ServerInfo> CreateServer(ServerInfo server, CancellationToken cancellationToken)
    {
        ToolInput.Name(server.Name);
        return servers.AddAsync(server with { Id = Guid.Empty }, cancellationToken);
    }

    [McpServerTool(Name = "update_server", Destructive = true)]
    [Description("Replace ALL writable game server configuration fields, including executable/script paths and autostart. Get the full record first; obtain approval for operational changes.")]
    public Task<ServerInfo> UpdateServer(ServerInfo server, CancellationToken cancellationToken)
    {
        ToolInput.Id(server.Id);
        ToolInput.Name(server.Name);
        return servers.UpdateAsync(server, cancellationToken);
    }

    [McpServerTool(Name = "delete_server", Destructive = true, Idempotent = true)]
    [Description("Delete game server configuration. Obtain explicit user approval before calling.")]
    public Task DeleteServer(Guid id, CancellationToken cancellationToken) =>
        servers.DeleteAsync(ToolInput.Id(id), cancellationToken);
}
