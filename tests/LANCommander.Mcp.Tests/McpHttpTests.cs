using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using LANCommander.SDK.Plugins;
using LANCommander.Mcp.Pages;
using LANCommander.Server.Plugins;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LANCommander.Mcp.Tests;

public sealed class McpHttpTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IGameProvider> _games = new(MockBehavior.Strict);
    private readonly Mock<IGameVersionProvider> _versions = new(MockBehavior.Strict);
    private readonly Mock<ITaxonomyProvider> _taxonomy = new(MockBehavior.Strict);
    private readonly Mock<IToolProvider> _tools = new(MockBehavior.Strict);
    private readonly Mock<IRedistributableProvider> _redistributables = new(MockBehavior.Strict);
    private readonly Mock<IServerProvider> _servers = new(MockBehavior.Strict);
    private readonly Mock<IMetadataLookup> _metadata = new(MockBehavior.Strict);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped(_ => _games.Object);
        builder.Services.AddScoped(_ => _versions.Object);
        builder.Services.AddScoped(_ => _taxonomy.Object);
        builder.Services.AddScoped(_ => _tools.Object);
        builder.Services.AddScoped(_ => _redistributables.Object);
        builder.Services.AddScoped(_ => _servers.Object);
        builder.Services.AddScoped(_ => _metadata.Object);
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "plugin", "LANCommander.Mcp.dll");
        var loadContext = new PluginLoadContext(pluginPath);
        var assembly = loadContext.LoadFromAssemblyPath(pluginPath);
        var pluginAttribute = Assert.Single(assembly.GetCustomAttributes(typeof(LANCommanderPluginAttribute), false)
            .Cast<LANCommanderPluginAttribute>());
        var plugin = Assert.IsAssignableFrom<IPlugin>(Activator.CreateInstance(pluginAttribute.EntryPoint));
        plugin.ConfigureServices(builder.Services);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        // Match LANCommander's host-enforced route-group authorization.
        foreach (var extension in _app.Services.GetServices<IServerEndpointExtension>())
        {
            var group = _app.MapGroup($"/api/Plugins/{extension.RoutePrefix}")
                .RequireAuthorization(policy => policy.RequireAuthenticatedUser()
                    .RequireAssertion(context => extension.Access.IsSatisfiedBy(context.User)));
            extension.MapEndpoints(group);
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<JsonElement> Request(string method, object? parameters = null, bool initialize = true)
    {
        if (initialize && method != "initialize")
        {
            var initialization = await Request("initialize", new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "integration-test", version = "1.0" }
            }, false);
            _client.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
            _client.DefaultRequestHeaders.Add("MCP-Protocol-Version",
                initialization.GetProperty("result").GetProperty("protocolVersion").GetString());
        }
        using var response = await _client.PostAsJsonAsync("/api/Plugins/Mcp",
            new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = body.Split('\n').First(line => line.StartsWith("data: "))[6..];
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private Task<JsonElement> Call(string name, object arguments) =>
        Request("tools/call", new { name, arguments });

    private static JsonElement ToolContent(JsonElement response)
    {
        var result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.ToString());
        using var document = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        return document.RootElement.Clone();
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task EveryRequestRequiresAdministrator(string? token, HttpStatusCode expected)
    {
        _client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        foreach (var method in new[] { "initialize", "tools/list", "tools/call" })
        {
            using var response = await _client.PostAsJsonAsync("/api/Plugins/Mcp",
                new { jsonrpc = "2.0", id = 1, method });
            Assert.Equal(expected, response.StatusCode);
        }
        using var get = await _client.GetAsync("/api/Plugins/Mcp");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Fact]
    public async Task DiscoveryExposesExpectedToolsAndSafetyAnnotations()
    {
        var response = await Request("tools/list");
        var tools = response.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(35, tools.Length);
        Assert.Equal(tools.Length, tools.Select(x => x.GetProperty("name").GetString()).Distinct().Count());
        var read = tools.Single(x => x.GetProperty("name").GetString() == "list_games");
        Assert.True(read.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        var delete = tools.Single(x => x.GetProperty("name").GetString() == "delete_game");
        Assert.True(delete.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
        Assert.DoesNotContain(tools, x => x.GetProperty("name").GetString()!.Contains("script"));
        var create = tools.Single(x => x.GetProperty("name").GetString() == "create_game");
        Assert.Contains("game", create.GetProperty("inputSchema").GetProperty("properties").ToString());
    }

    [Fact]
    public async Task GameCrudUsesHostProviderAndPreservesFields()
    {
        var id = Guid.NewGuid();
        var game = new GameInfo { Id = id, Title = "Quake", Notes = "Keep these", Published = false };
        _games.Setup(x => x.AddAsync(It.Is<GameInfo>(g => g.Id == Guid.Empty && g.Title == "Quake"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(game);
        _games.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(game);
        _games.Setup(x => x.UpdateAsync(It.Is<GameInfo>(g => g.Id == id && g.Title == "Quake II"
            && g.Notes == "Keep these" && !g.Published), It.IsAny<CancellationToken>()))
            .ReturnsAsync(game with { Title = "Quake II" });
        _games.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(id, ToolContent(await Call("create_game", new { game })).GetProperty("id").GetGuid());
        Assert.Equal("Keep these", ToolContent(await Call("get_game", new { id })).GetProperty("notes").GetString());
        Assert.Equal("Quake II", ToolContent(await Call("update_game", new { game = game with { Title = "Quake II" } }))
            .GetProperty("title").GetString());
        var deleted = await Call("delete_game", new { id });
        Assert.False(deleted.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean());
        _games.VerifyAll();
    }

    [Fact]
    public async Task GameSearchIsCaseInsensitiveAndPaged()
    {
        _games.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new() { Title = "DOOM" }, new() { Title = "Doom II" }, new() { Title = "Quake" }
        ]);
        var page = ToolContent(await Call("list_games", new { search = "doom", offset = 1, limit = 1 }));
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("offset").GetInt32());
        Assert.Equal("Doom II", page.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Single(page.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPaginationDoesNotCallProvider(int offset, int limit)
    {
        var response = await Call("list_games", new { offset, limit });
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        _games.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingRecordIsAnExplicitToolError()
    {
        _games.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((GameInfo?)null);
        var response = await Call("get_game", new { id = Guid.NewGuid() });
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task EmptyDeleteIdDoesNotCallProvider()
    {
        var response = await Call("delete_game", new { id = Guid.Empty });
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        _games.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProviderFailureIsNotReportedAsSuccess()
    {
        _games.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Database unavailable"));
        var response = await Call("list_games", new { });
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Theory]
    [InlineData("https://untrusted.example")]
    [InlineData("null")]
    [InlineData("http://localhost:9000")]
    [InlineData("http://localhost/invalid-path")]
    [InlineData("http://user@localhost")]
    [InlineData("http://localhost, https://untrusted.example")]
    public async Task CrossOriginBrowserRequestIsRejected(string origin)
    {
        _client.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await _client.PostAsJsonAsync("/api/Plugins/Mcp",
            new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SameOriginBrowserRequestIsAllowed()
    {
        _client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var response = await Request("tools/list");
        Assert.Equal(35, response.GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task SetupModuleRequiresAdministrator(string? token, HttpStatusCode expected)
    {
        _client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        using var response = await _client.GetAsync("/api/Plugins/Mcp/setup.mjs");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("api/Auth/Login", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void SetupPageAndNavigationAreAdministratorOnly()
    {
        var navigation = new McpNavigation();
        Assert.Equal("/Plugins/Mcp", navigation.Href);
        Assert.Equal(ServerNavigationSection.Integrations, navigation.Section);
        Assert.Equal(PluginAccessPolicy.Administrator, navigation.Access);
        Assert.Equal(typeof(McpPlugin).Assembly, new McpPages().Assembly);
        var route = Assert.Single(typeof(McpSetup).GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false)
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>());
        Assert.Equal(navigation.Href, route.Template);
        var access = Assert.Single(typeof(McpSetup).GetCustomAttributes(typeof(PluginAccessAttribute), false)
            .Cast<PluginAccessAttribute>());
        Assert.Equal(PluginAccessPolicy.Administrator, access.Policy);
    }

    [Fact]
    public async Task TaxonomyCrudUsesCorrectKind()
    {
        var id = Guid.NewGuid();
        _taxonomy.Setup(x => x.AddAsync(TaxonomyKind.Genre, "FPS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxonomyInfo { Id = id, Kind = TaxonomyKind.Genre, Name = "FPS" });
        _taxonomy.Setup(x => x.RenameAsync(TaxonomyKind.Genre, id, "Shooter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxonomyInfo { Id = id, Kind = TaxonomyKind.Genre, Name = "Shooter" });
        _taxonomy.Setup(x => x.DeleteAsync(TaxonomyKind.Genre, id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal("FPS", ToolContent(await Call("create_taxonomy", new { kind = "Genre", name = "FPS" }))
            .GetProperty("name").GetString());
        Assert.Equal("Shooter", ToolContent(await Call("rename_taxonomy", new { kind = "Genre", id, name = "Shooter" }))
            .GetProperty("name").GetString());
        await Call("delete_taxonomy", new { kind = "Genre", id });
        _taxonomy.VerifyAll();
    }

    [Fact]
    public async Task GameVersionCrudUsesHostProvider()
    {
        var gameId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var version = new GameVersionInfo { Id = id, GameId = gameId, Version = "1.0", Changelog = "Keep" };
        _versions.Setup(x => x.CreateAsync(gameId, "1.0", "Keep", It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _versions.Setup(x => x.GetVersionAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _versions.Setup(x => x.UpdateAsync(It.Is<GameVersionInfo>(v => v.Id == id && v.Changelog == "Keep" && v.Version == "2.0"),
            It.IsAny<CancellationToken>())).ReturnsAsync(version with { Version = "2.0" });
        _versions.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(id, ToolContent(await Call("create_game_version", new { gameId, version = "1.0", changelog = "Keep" }))
            .GetProperty("id").GetGuid());
        Assert.Equal("1.0", ToolContent(await Call("get_game_version", new { id })).GetProperty("version").GetString());
        Assert.Equal("2.0", ToolContent(await Call("update_game_version", new { version = version with { Version = "2.0" } }))
            .GetProperty("version").GetString());
        await Call("delete_game_version", new { id });
        _versions.VerifyAll();
    }

    [Fact]
    public async Task CatalogToolCrudUsesHostProvider()
    {
        var id = Guid.NewGuid();
        var tool = new ToolInfo { Id = id, Name = "Mod manager", Notes = "Keep", AlwaysInstall = true };
        _tools.Setup(x => x.AddAsync(It.Is<ToolInfo>(v => v.Id == Guid.Empty && v.Name == tool.Name), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tool);
        _tools.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(tool);
        _tools.Setup(x => x.UpdateAsync(It.Is<ToolInfo>(v => v.Id == id && v.Notes == "Keep" && v.AlwaysInstall), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tool with { Name = "Updated" });
        _tools.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(id, ToolContent(await Call("create_tool", new { tool })).GetProperty("id").GetGuid());
        Assert.Equal(tool.Name, ToolContent(await Call("get_tool", new { id })).GetProperty("name").GetString());
        Assert.Equal("Updated", ToolContent(await Call("update_tool", new { tool = tool with { Name = "Updated" } }))
            .GetProperty("name").GetString());
        await Call("delete_tool", new { id });
        _tools.VerifyAll();
    }

    [Fact]
    public async Task RedistributableCrudUsesHostProvider()
    {
        var id = Guid.NewGuid();
        var redistributable = new RedistributableInfo { Id = id, Name = "Runtime", OptionSchema = "{\"keep\":true}" };
        _redistributables.Setup(x => x.AddAsync(It.Is<RedistributableInfo>(v => v.Id == Guid.Empty && v.Name == "Runtime"),
            It.IsAny<CancellationToken>())).ReturnsAsync(redistributable);
        _redistributables.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(redistributable);
        _redistributables.Setup(x => x.UpdateAsync(It.Is<RedistributableInfo>(v => v.Id == id && v.OptionSchema == redistributable.OptionSchema),
            It.IsAny<CancellationToken>())).ReturnsAsync(redistributable with { Name = "Updated" });
        _redistributables.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(id, ToolContent(await Call("create_redistributable", new { redistributable })).GetProperty("id").GetGuid());
        Assert.Equal("Runtime", ToolContent(await Call("get_redistributable", new { id })).GetProperty("name").GetString());
        Assert.Equal("Updated", ToolContent(await Call("update_redistributable", new { redistributable = redistributable with { Name = "Updated" } }))
            .GetProperty("name").GetString());
        await Call("delete_redistributable", new { id });
        _redistributables.VerifyAll();
    }

    [Fact]
    public async Task ServerCrudUsesHostProvider()
    {
        var id = Guid.NewGuid();
        var server = new ServerInfo { Id = id, Name = "Quake server", Path = "quake", Port = 26000, Autostart = false };
        _servers.Setup(x => x.AddAsync(It.Is<ServerInfo>(v => v.Id == Guid.Empty && v.Name == server.Name), It.IsAny<CancellationToken>()))
            .ReturnsAsync(server);
        _servers.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(server);
        _servers.Setup(x => x.UpdateAsync(It.Is<ServerInfo>(v => v.Id == id && v.Path == "quake" && v.Port == 26000 && !v.Autostart),
            It.IsAny<CancellationToken>())).ReturnsAsync(server with { Name = "Updated" });
        _servers.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(id, ToolContent(await Call("create_server", new { server })).GetProperty("id").GetGuid());
        Assert.Equal("quake", ToolContent(await Call("get_server", new { id })).GetProperty("path").GetString());
        Assert.Equal("Updated", ToolContent(await Call("update_server", new { server = server with { Name = "Updated" } }))
            .GetProperty("name").GetString());
        await Call("delete_server", new { id });
        _servers.VerifyAll();
    }

    [Fact]
    public async Task LinksAreReplacedUsingCorrectKind()
    {
        var gameId = Guid.NewGuid();
        var genreId = Guid.NewGuid();
        _games.Setup(x => x.GetLinksAsync(gameId, GameLinkKind.Genres, It.IsAny<CancellationToken>())).ReturnsAsync([genreId]);
        _games.Setup(x => x.SetLinksAsync(gameId, GameLinkKind.Genres, It.Is<IEnumerable<Guid>>(ids => !ids.Any()),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(genreId, ToolContent(await Call("get_game_links", new { gameId, kind = "Genres" }))[0].GetGuid());
        await Call("set_game_links", new { gameId, kind = "Genres", ids = Array.Empty<Guid>() });
        _games.VerifyAll();
    }

    [Fact]
    public async Task MetadataSearchUsesProviderPagingOrder()
    {
        _metadata.Setup(x => x.SearchGamesAsync("IGDB", "Quake", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage([], false, 5, 10));
        var result = ToolContent(await Call("search_metadata", new { providerName = "IGDB", search = "Quake", offset = 10, limit = 5 }));
        Assert.Equal(5, result.GetProperty("limit").GetInt32());
        Assert.Equal(10, result.GetProperty("offset").GetInt32());
        _metadata.VerifyAll();
    }

    [Fact]
    public void PluginIsServerOnlyAndAdministratorOnly()
    {
        var attribute = Assert.Single(typeof(McpPlugin).Assembly.GetCustomAttributes(typeof(LANCommanderPluginAttribute), false)
            .Cast<LANCommanderPluginAttribute>());
        Assert.Equal(PluginHost.Server, attribute.Hosts);
        Assert.Equal("2.2.0-alpha.3", attribute.MinHostVersion);
        Assert.False(new McpEndpoints().Access.IsSatisfiedBy(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (token is not ("Bearer admin" or "Bearer user"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.Name, "test") };
        if (token == "Bearer admin")
            claims.Add(new(ClaimTypes.Role, "Administrator"));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
