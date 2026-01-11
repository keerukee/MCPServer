using System.Reflection;
using System.Text.Json;
using MCPServer.Handlers;

namespace MCPServer;

/// <summary>
/// Configuration options for the MCP server.
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// The name of the server (shown to clients).
    /// </summary>
    public string ServerName { get; set; } = "MCPServer";

    /// <summary>
    /// The version of the server.
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>
    /// The MCP protocol version to advertise.
    /// </summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";
}

/// <summary>
/// A lightweight MCP (Model Context Protocol) server that automatically discovers
/// tools, resources, and prompts from decorated methods.
/// </summary>
public class McpServerHost
{
    private readonly McpServerOptions _options;
    private readonly McpToolHandler _toolHandler;
    private readonly McpResourceHandler _resourceHandler;
    private readonly McpResourceTemplateHandler _resourceTemplateHandler;
    private readonly McpPromptHandler _promptHandler;

    /// <summary>
    /// Creates a new MCP server that discovers handlers from the calling assembly.
    /// </summary>
    public McpServerHost() : this(Assembly.GetCallingAssembly(), new McpServerOptions())
    {
    }

    /// <summary>
    /// Creates a new MCP server with custom options that discovers handlers from the calling assembly.
    /// </summary>
    public McpServerHost(McpServerOptions options) : this(Assembly.GetCallingAssembly(), options)
    {
    }

    /// <summary>
    /// Creates a new MCP server that discovers handlers from the specified assembly.
    /// </summary>
    public McpServerHost(Assembly assembly) : this(assembly, new McpServerOptions())
    {
    }

    /// <summary>
    /// Creates a new MCP server with custom options that discovers handlers from the specified assembly.
    /// </summary>
    public McpServerHost(Assembly assembly, McpServerOptions options)
    {
        _options = options;
        _toolHandler = new McpToolHandler(assembly);
        _resourceHandler = new McpResourceHandler(assembly);
        _resourceTemplateHandler = new McpResourceTemplateHandler(assembly);
        _promptHandler = new McpPromptHandler(assembly);
    }

    /// <summary>
    /// Starts the MCP server and begins listening for requests on stdin/stdout.
    /// This method runs indefinitely until the process is terminated.
    /// </summary>
    public void Run()
    {
        while (true)
        {
            // Read a single line from standard input (stdin)
            string? line = Console.ReadLine();

            // Skip empty or null lines to avoid parsing errors
            if (string.IsNullOrEmpty(line)) continue;

            // Parse the incoming line as a JSON document
            var request = JsonDocument.Parse(line);

            // Extract the method field from the JSON-RPC request
            var method = request.RootElement.GetProperty("method").GetString();

            // Check if this is a notification (no "id" field)
            bool isNotification = !request.RootElement.TryGetProperty("id", out var idElement);

            // Route the request to the appropriate handler
            object? responseResult = HandleRequest(method, request.RootElement);

            // Only send a response if this is not a notification
            if (!isNotification)
            {
                var id = idElement.GetInt32();
                var response = new { jsonrpc = "2.0", id = id, result = responseResult };
                Console.WriteLine(JsonSerializer.Serialize(response));
            }
        }
    }

    /// <summary>
    /// Handles a single MCP request and returns the result.
    /// </summary>
    private object? HandleRequest(string? method, JsonElement requestElement)
    {
        return method switch
        {
            // Initialize - returns server capabilities and protocol version
            "initialize" => new
            {
                protocolVersion = _options.ProtocolVersion,
                capabilities = new
                {
                    tools = new { },
                    resources = new { },
                    prompts = new { }
                },
                serverInfo = new { name = _options.ServerName, version = _options.ServerVersion }
            },

            // Notifications - no response required
            "notifications/initialized" => null,
            "initialized" => null,
            "cancelled" => null,

            // Ping - health check
            "ping" => new { },

            // Tools
            "tools/list" => new { tools = _toolHandler.GetToolDefinitions() },
            "tools/call" => _toolHandler.HandleToolCall(requestElement.GetProperty("params")),

            // Resources
            "resources/list" => new { resources = _resourceHandler.GetResourceDefinitions() },
            "resources/read" => _resourceHandler.HandleResourceRead(requestElement.GetProperty("params")),
            "resources/templates/list" => new { resourceTemplates = _resourceTemplateHandler.GetTemplateDefinitions() },

            // Prompts
            "prompts/list" => new { prompts = _promptHandler.GetPromptDefinitions() },
            "prompts/get" => _promptHandler.HandlePromptGet(requestElement.GetProperty("params")),

            // Completion
            "completion/complete" => new
            {
                completion = new
                {
                    values = Array.Empty<string>(),
                    hasMore = false
                }
            },

            // Unknown method
            _ => new { error = new { code = -32601, message = "Method not found" } }
        };
    }
}
