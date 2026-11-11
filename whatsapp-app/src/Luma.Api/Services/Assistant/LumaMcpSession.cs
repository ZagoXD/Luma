using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Luma.Api.Services.Assistant;

// Each session binds one authenticated conversation. No identity or credentials are model arguments.
public sealed class LumaMcpSession : IAsyncDisposable
{
    private readonly McpServer _server;
    private readonly McpClient _client;
    private readonly Task _run;
    private readonly CancellationTokenSource _lifetime;

    private LumaMcpSession(McpServer server, McpClient client, Task run, CancellationTokenSource lifetime)
        => (_server, _client, _run, _lifetime) = (server, client, run, lifetime);

    public static async Task<LumaMcpSession> CreateAsync(
        Func<string, JsonElement, CancellationToken, Task<ToolOutcome>> execute, CancellationToken cancellationToken = default)
    {
        var upstream = new Pipe();
        var downstream = new Pipe();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = McpServer.Create(new StreamServerTransport(upstream.Reader.AsStream(), downstream.Writer.AsStream()),
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "luma-authoritative-tools", Version = "1.0.0" },
                ToolCollection = [.. LumaToolCatalog.Tools.Select(tool => new BoundTool(tool, execute))]
            });
        var run = server.RunAsync(lifetime.Token);
        try
        {
            var client = await McpClient.CreateAsync(new StreamClientTransport(upstream.Writer.AsStream(), downstream.Reader.AsStream()),
                cancellationToken: lifetime.Token);
            return new LumaMcpSession(server, client, run, lifetime);
        }
        catch
        {
            await lifetime.CancelAsync();
            await server.DisposeAsync();
            lifetime.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<Tool>> ListAsync(CancellationToken ct)
        => (await _client.ListToolsAsync(cancellationToken: ct)).Select(tool => tool.ProtocolTool).ToArray();

    public async Task<ToolOutcome> CallAsync(string name, JsonElement arguments, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(name,
            arguments.EnumerateObject().ToDictionary(field => field.Name, field => (object?)field.Value),
            cancellationToken: ct);
        var text = string.Join("", result.Content.OfType<TextContentBlock>().Select(content => content.Text));
        return JsonSerializer.Deserialize<ToolOutcome>(text) ?? new(false, "A operação não retornou resultado.");
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        try { await _run; } catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }

    private sealed class BoundTool(Tool tool, Func<string, JsonElement, CancellationToken, Task<ToolOutcome>> execute) : McpServerTool
    {
        public override Tool ProtocolTool => tool;
        public override IReadOnlyList<object> Metadata => [];
        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            var arguments = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
            var error = LumaToolCatalog.Validate(tool.Name, arguments);
            var outcome = error is null ? await execute(tool.Name, arguments, cancellationToken) : new ToolOutcome(false, error);
            return new CallToolResult
            {
                IsError = !outcome.Success,
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(outcome) }]
            };
        }
    }
}
