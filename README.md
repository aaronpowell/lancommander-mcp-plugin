# LANCommander MCP plugin

A server plugin that exposes LANCommander catalog management over the Model Context
Protocol (MCP). It runs **inside LANCommander**, uses its provider contracts and
authentication, and opens no additional listener.

## Requirements

- LANCommander server **2.2.0-alpha.3 or later** with the server plugin framework.
- .NET 10 SDK to build.
- An MCP client supporting Streamable HTTP and an administrator's LANCommander bearer token.

The project references published `LANCommander.SDK` and
`LANCommander.Server.Plugins` **NuGet packages**, not the LANCommander source tree.
The initial implementation was checked against upstream `LANCommander/LANCommander`
`main` at `a77242f02891d86cab29d4d2621f95b2dfedf80d`.

## Build and install

```powershell
dotnet test tests\LANCommander.Mcp.Tests
dotnet pack src\LANCommander.Mcp -c Release -o artifacts
.\eng\Test-Package.ps1 -PackagePath artifacts\LANCommander.Mcp.0.1.0.nupkg
```

Upload `artifacts\LANCommander.Mcp.0.1.0.nupkg` through the server's plugin
package installer, then restart LANCommander. The package declares
`LANCommander.Plugin` and `LANCommander.Plugin.Server` and bundles its private
dependencies and dependency manifest under `lib/net10.0`. Host-owned LANCommander
assemblies are deliberately excluded.

For a manual installation, build in Release and copy the **whole output folder**
to `Plugins\LANCommander.Mcp` under the server's application directory, then restart.
Do not copy just the main DLL. Use either manual installation or the package
installer, not both.

Tagged releases (`v*`) build, test and attach the installable NuGet package to a
GitHub release. The workflow does not publish to NuGet.org.

## Connect

The endpoint is:

```text
https://your-lancommander-server/api/Plugins/Mcp
```

For clients using the VS Code MCP configuration format:

```json
{
  "inputs": [
    {
      "id": "lc-token",
      "type": "promptString",
      "description": "LANCommander administrator bearer token",
      "password": true
    }
  ],
  "servers": {
    "lancommander": {
      "type": "http",
      "url": "https://your-lancommander-server/api/Plugins/Mcp",
      "headers": {
        "Authorization": "Bearer ${input:lc-token}"
      }
    }
  }
}
```

Obtain a bearer token through LANCommander's existing authentication flow with
an administrator account. This plugin does not issue tokens, implement OAuth
discovery, or change the server's authentication settings. Browser cookies work
only where the host accepts them; the supported remote-client setup is the bearer
header above. Use HTTPS beyond a trusted local development connection, and never
commit tokens to configuration files.

The server enforces the plugin's **administrator-only** policy on every MCP
request, including initialization and discovery. Ordinary authenticated users
cannot use the endpoint. The transport is stateless: each request gets its own
scope, so database-backed provider instances are not retained across sessions.
There is no stdio transport, legacy SSE endpoint, anonymous access or broad CORS
policy.
Browser requests with an `Origin` header must match the request's scheme, host
and port; malformed, opaque and cross-origin values receive HTTP 403. Non-browser
clients may omit `Origin`. Behind a reverse proxy, configure LANCommander's trusted
forwarded headers and allowed hosts correctly so that it sees the public origin.

## Tools

| Area | Tools |
| --- | --- |
| Games and add-ons | `list_games`, `get_game`, `create_game`, `update_game`, `delete_game` |
| Game relationships | `get_game_links`, `set_game_links` |
| Game versions | `list_game_versions`, `get_game_version`, `create_game_version`, `update_game_version`, `delete_game_version` |
| Taxonomy | `list_taxonomy`, `get_taxonomy`, `create_taxonomy`, `rename_taxonomy`, `delete_taxonomy` |
| Installable tools | `list_tools`, `get_tool`, `create_tool`, `update_tool`, `delete_tool` |
| Redistributables | `list_redistributables`, `get_redistributable`, `create_redistributable`, `update_redistributable`, `delete_redistributable` |
| Game server configuration | `list_servers`, `get_server`, `create_server`, `update_server`, `delete_server` |
| External metadata | `list_metadata_providers`, `search_metadata`, `get_metadata` |

List tools accept `offset` and `limit` (1-100, default 25; metadata search defaults
to 10). Catalog lists return `items`, `total`, `offset` and `limit`; optional
`search` is a case-insensitive substring match on title/name. Providers currently
load their full list before the plugin filters/pages it.

Create calls ignore supplied record ids and let LANCommander assign them.
**Update calls replace all writable fields**, matching the provider contracts:
read the record immediately before updating, preserve unrelated fields, and send
the entire record. There is no optimistic concurrency token; simultaneous edits
can overwrite one another. Setting links replaces the entire chosen relationship
kind, and an empty array removes all links of that kind.

### Safety and limitations

This is a write-capable administrative interface. Keep MCP client approval enabled
for mutations, especially deletions and game server configuration. Read-only and
destructive tool annotations are hints for clients, **not an authorization or
confirmation mechanism**. The plugin does not independently collect user approval.

- Deleting games, versions, tools or redistributables can permanently delete
  their archive/media files as specified in the tool descriptions.
- Creating a game version carries forward the latest version's child records
  and **publishes an existing latest draft**.
- Game server configuration includes executable and script paths and autostart:
  writes can affect subsequent process execution. Process start/stop, consoles,
  RCON passwords and HTTP file paths are not exposed.
- No script editing/execution, product keys, media upload, package import,
  archive download, custom field/external-id management or version-specific
  redistributable option management is exposed.
- Catalog descriptions and external metadata are untrusted content, not
  instructions for an agent.
- An MCP SDK failure or provider failure is reported as an error, not a
  successful empty result. Missing single records are explicit tool errors.

## Development

The integration tests use the published contracts, ASP.NET Core TestServer and
mock providers. They load the built plugin through LANCommander's isolated
`PluginLoadContext`, then exercise the real HTTP MCP pipeline, browser origin
guard and the same route-group authorization arrangement as LANCommander.
No live catalog is modified.

For testing inside a local LANCommander clone, first fetch upstream and check
out its latest `main` in an isolated worktree, build the server, and install the
plugin as above. Do not replace the NuGet references with project references.
The published contracts are the compatibility boundary.

## License

MIT.
