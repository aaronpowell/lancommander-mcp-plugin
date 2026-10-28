using System.Reflection;
using LANCommander.Server.Plugins;

namespace LANCommander.Mcp;

public sealed class McpPages : IServerRouteAssemblyExtension
{
    public Assembly Assembly => typeof(McpPlugin).Assembly;
}

public sealed class McpNavigation : IServerNavigationExtension
{
    public string Id => $"{McpPlugin.PluginId}.setup";
    public string Label => "MCP";
    public string Href => "/Plugins/Mcp";
    public string Section => ServerNavigationSection.Integrations;
    public PluginAccessPolicy Access => PluginAccessPolicy.Administrator;
}
