using LANCommander.Mcp;
using LANCommander.SDK.Plugins;
using LANCommander.Server.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: LANCommanderPlugin(typeof(McpPlugin), Id = McpPlugin.PluginId,
    MinHostVersion = "2.2.0-alpha.3", Hosts = PluginHost.Server)]

namespace LANCommander.Mcp;

public sealed class McpPlugin : IPlugin
{
    public const string PluginId = "io.github.aaronpowell.lancommander.mcp";
    public string Id => PluginId;
    public string Name => "LANCommander MCP";
    public string Version => "0.1.0";
    public string Author => "aaronpowell";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IServerEndpointExtension, McpEndpoints>();
        services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<CatalogTools>()
            .WithTools<MetadataTools>();
    }

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation("LANCommander MCP available to administrators at /api/Plugins/Mcp");
        return Task.CompletedTask;
    }
}

public sealed class McpEndpoints : IServerEndpointExtension
{
    public string RoutePrefix => "Mcp";
    public PluginAccessPolicy Access => PluginAccessPolicy.Administrator;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp().Add(builder =>
        {
            var next = builder.RequestDelegate
                ?? throw new InvalidOperationException("The MCP endpoint has no request delegate.");
            builder.RequestDelegate = context => ValidateOrigin(context, next);
        });
    }

    private static Task ValidateOrigin(HttpContext context, RequestDelegate next)
    {
        var origins = context.Request.Headers.Origin;
        if (origins.Count == 0)
            return next(context);

        // Non-browser MCP clients omit Origin. Browsers must use the server's own origin.
        if (origins.Count == 1
            && Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            && origin.UserInfo.Length == 0 && origin.AbsolutePath == "/"
            && origin.Query.Length == 0 && origin.Fragment.Length == 0
            && Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host}", UriKind.Absolute, out var server)
            && origin.Scheme == server.Scheme
            && string.Equals(origin.IdnHost, server.IdnHost, StringComparison.OrdinalIgnoreCase)
            && origin.Port == server.Port)
            return next(context);

        context.RequestServices.GetRequiredService<ILogger<McpEndpoints>>()
            .LogWarning("Rejected cross-origin or malformed-origin MCP request");
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
