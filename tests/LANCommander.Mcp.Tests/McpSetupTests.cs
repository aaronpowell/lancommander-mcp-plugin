using Bunit;
using System.Text.Json;
using LANCommander.Mcp.Pages;
using LANCommander.Server.UI.Controls;
using Microsoft.JSInterop;
using Xunit;

namespace LANCommander.Mcp.Tests;

public sealed class McpSetupTests : BunitContext
{
    public McpSetupTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<PageHeader>();
        ComponentFactories.AddStub<PageContent>(parameters => parameters.Get(x => x.ChildContent) ?? (_ => { }));
        ComponentFactories.AddStub<Flex>(parameters => parameters.Get(x => x.ChildContent) ?? (_ => { }));
        ComponentFactories.AddStub<Alert>(parameters => parameters.Get(x => x.ChildContent) ?? (_ => { }));
        ComponentFactories.AddStub<Checkbox>();
        ComponentFactories.AddStub<Button>();
    }

    [Fact]
    public void InitialRenderDoesNotGenerateTokenAndExplainsWarning()
    {
        var cut = Render<McpSetup>();
        Assert.Contains("replaces this account's refresh token", cut.Markup);
        Assert.Equal("password", cut.Find("input[name=password]").GetAttribute("type"));
        Assert.Empty(cut.FindAll("input[aria-label='Access token']"));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public async Task GenerationRequiresAcknowledgmentAndTokenCanBeCleared()
    {
        var module = JSInterop.SetupModule("http://localhost/api/Plugins/Mcp/setup.mjs");
        module.Setup<McpSetup.TokenResult>("login", _ => true)
            .SetResult(new("test-access-token", DateTimeOffset.UtcNow.AddHours(1)));
        var cut = Render<McpSetup>();
        await cut.Find("form").SubmitAsync();
        Assert.Empty(cut.FindAll("input[aria-label='Access token']"));
        Assert.Empty(JSInterop.Invocations);

        var checkbox = cut.FindComponent<Bunit.TestDoubles.Stub<Checkbox>>();
        await cut.InvokeAsync(() => checkbox.Instance.Parameters.Get(x => x.ValueChanged).InvokeAsync(true));
        await cut.Find("form").SubmitAsync();
        Assert.Equal("test-access-token", cut.Find("input[aria-label='Access token']").GetAttribute("value"));
        Assert.Equal("password", cut.Find("input[aria-label='Access token']").GetAttribute("type"));
        var buttons = cut.FindComponents<Bunit.TestDoubles.Stub<Button>>();
        module.SetupVoid("copy", _ => true).SetVoidResult();
        await cut.InvokeAsync(() => buttons[2].Instance.Parameters.Get(x => x.OnClick).InvokeAsync());
        var copy = Assert.Single(module.Invocations["copy"]);
        using var configuration = JsonDocument.Parse(Assert.IsType<string>(copy.Arguments[0]));
        var server = configuration.RootElement.GetProperty("servers").GetProperty("lancommander");
        Assert.Equal("http://localhost/api/Plugins/Mcp", server.GetProperty("url").GetString());
        Assert.Equal("Bearer test-access-token", server.GetProperty("headers").GetProperty("Authorization").GetString());
        Assert.Contains("Copied to clipboard.", cut.Markup);
        var clear = buttons.Last();
        await cut.InvokeAsync(() => clear.Instance.Parameters.Get(x => x.OnClick).InvokeAsync());
        Assert.Empty(cut.FindAll("input[aria-label='Access token']"));
    }

    [Fact]
    public async Task LoginFailureIsVisibleWithoutEchoingCredentials()
    {
        var module = JSInterop.SetupModule("http://localhost/api/Plugins/Mcp/setup.mjs");
        module.Setup<McpSetup.TokenResult>("login", _ => true)
            .SetException(new JSException("Do not display secret details"));
        var cut = Render<McpSetup>();
        var checkbox = cut.FindComponent<Bunit.TestDoubles.Stub<Checkbox>>();
        await cut.InvokeAsync(() => checkbox.Instance.Parameters.Get(x => x.ValueChanged).InvokeAsync(true));
        await cut.Find("form").SubmitAsync();
        Assert.Contains("Could not generate a token", cut.Markup);
        Assert.DoesNotContain("Do not display secret details", cut.Markup);
        Assert.Empty(cut.FindAll("input[aria-label='Access token']"));
    }
}
