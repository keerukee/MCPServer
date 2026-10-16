using System.Reflection;
using System.Text;
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
    /// The default MCP protocol version to advertise for legacy initialize handshakes.
    /// </summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// All MCP protocol versions supported by the server (advertised in server/discover and used in version negotiation).
    /// </summary>
    public string[] SupportedProtocolVersions { get; set; } =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        "2025-11-25",
        "2026-07-28"
    ];
}

/// <summary>
/// A lightweight MCP (Model Context Protocol) server that automatically discovers
/// tools, resources, and prompts from decorated methods.
/// </summary>
public class McpServerHost
{
    private const string ModernProtocolVersion = "2026-07-28";
    private const string LatestInitializeVersion = "2025-11-25";

    private readonly McpServerOptions _options;
    private readonly McpToolHandler _toolHandler;
    private readonly McpResourceHandler _resourceHandler;
    private readonly McpResourceTemplateHandler _resourceTemplateHandler;
    private readonly McpPromptHandler _promptHandler;
    private string _negotiatedProtocolVersion;

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
        _negotiatedProtocolVersion = options.ProtocolVersion;
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
        // Ensure UTF-8 encoding on stdin (stripping any BOM) and UTF-8 without BOM on stdout
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Log registered handlers to stderr (stdout is reserved for MCP protocol)
        Console.Error.WriteLine($"[{_options.ServerName}] MCP Server v{_options.ServerVersion} started");
        Console.Error.WriteLine($"[{_options.ServerName}] Registered: {_toolHandler.Count} tools, {_resourceHandler.Count} resources, {_resourceTemplateHandler.Count} resource templates, {_promptHandler.Count} prompts");

        while (true)
        {
            // Read a single line from standard input (stdin)
            string? line = Console.ReadLine();

            // If stdin is closed (client disconnected), exit gracefully
            if (line == null)
            {
                Console.Error.WriteLine($"[{_options.ServerName}] Client disconnected, shutting down");
                break;
            }

            // Strip any UTF-8 BOM and skip empty or whitespace lines
            line = line.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument request;
            try
            {
                request = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                var parseError = new
                {
                    jsonrpc = "2.0",
                    id = (object?)null,
                    error = new { code = -32700, message = $"Parse error: {ex.Message}" }
                };
                Console.WriteLine(JsonSerializer.Serialize(parseError));
                continue;
            }

            using (request)
            {
                var root = request.RootElement;

                // Check if this is a notification (no "id" field)
                bool isNotification = !root.TryGetProperty("id", out var idElement);

                // Extract the method field from the JSON-RPC request
                if (!root.TryGetProperty("method", out var methodElement) ||
                    methodElement.ValueKind != JsonValueKind.String)
                {
                    if (!isNotification)
                    {
                        WriteErrorResponse(idElement, new JsonRpcError(-32600, "Invalid Request: missing method"));
                    }
                    continue;
                }

                var method = methodElement.GetString();

                object? responseResult;
                try
                {
                    responseResult = HandleRequest(method, root);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{_options.ServerName}] Error handling '{method}': {ex.Message}");
                    responseResult = new JsonRpcError(-32603, $"Internal error: {ex.Message}");
                }

                // Only send a response if this is not a notification
                if (!isNotification)
                {
                    if (responseResult is JsonRpcError rpcError)
                    {
                        WriteErrorResponse(idElement, rpcError);
                    }
                    else
                    {
                        var response = new
                        {
                            jsonrpc = "2.0",
                            id = ExtractRequestId(idElement),
                            result = responseResult
                        };
                        Console.WriteLine(JsonSerializer.Serialize(response));
                    }
                }
            }
        }
    }

    private sealed record JsonRpcError(int Code, string Message);

    private static object? ExtractRequestId(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.Number when idElement.TryGetInt32(out var i) => i,
            JsonValueKind.Number when idElement.TryGetInt64(out var l) => l,
            JsonValueKind.Number => idElement.GetDouble(),
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Null => null,
            _ => idElement.GetRawText()
        };
    }

    private static void WriteErrorResponse(JsonElement idElement, JsonRpcError rpcError)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = ExtractRequestId(idElement),
            error = new { code = rpcError.Code, message = rpcError.Message }
        };
        Console.WriteLine(JsonSerializer.Serialize(response));
    }

    /// <summary>
    /// Determines whether the request/session uses MCP 2026-07-28+ semantics.
    /// </summary>
    private bool IsModernProtocol(JsonElement requestElement)
    {
        if (requestElement.TryGetProperty("params", out var paramsElement) &&
            paramsElement.ValueKind == JsonValueKind.Object &&
            paramsElement.TryGetProperty("_meta", out var metaElement) &&
            metaElement.ValueKind == JsonValueKind.Object &&
            metaElement.TryGetProperty("io.modelcontextprotocol/protocolVersion", out var versionElement) &&
            versionElement.ValueKind == JsonValueKind.String)
        {
            var reqVersion = versionElement.GetString();
            if (!string.IsNullOrEmpty(reqVersion))
            {
                return string.CompareOrdinal(reqVersion, ModernProtocolVersion) >= 0;
            }
        }

        return string.CompareOrdinal(_negotiatedProtocolVersion, ModernProtocolVersion) >= 0;
    }

    private static object GetCapabilities() => new
    {
        tools = new { },
        resources = new { },
        prompts = new { }
    };

    private object HandleDiscover()
    {
        _negotiatedProtocolVersion = ModernProtocolVersion;

        return new
        {
            resultType = "complete",
            supportedVersions = _options.SupportedProtocolVersions,
            capabilities = GetCapabilities(),
            ttlMs = 0,
            cacheScope = "private",
            _meta = new Dictionary<string, object>
            {
                ["io.modelcontextprotocol/serverInfo"] = new
                {
                    name = _options.ServerName,
                    version = _options.ServerVersion
                }
            }
        };
    }

    private object HandleInitialize(JsonElement requestElement)
    {
        string negotiated = _options.ProtocolVersion;

        if (requestElement.TryGetProperty("params", out var paramsElement) &&
            paramsElement.ValueKind == JsonValueKind.Object &&
            paramsElement.TryGetProperty("protocolVersion", out var versionElement) &&
            versionElement.ValueKind == JsonValueKind.String)
        {
            var requested = versionElement.GetString();
            if (!string.IsNullOrEmpty(requested))
            {
                if (string.CompareOrdinal(requested, ModernProtocolVersion) >= 0)
                {
                    // Legacy initialize handshake cannot echo 2026-07-28+; cap at 2025-11-25
                    negotiated = LatestInitializeVersion;
                }
                else if (_options.SupportedProtocolVersions.Contains(requested))
                {
                    negotiated = requested;
                }
            }
        }

        _negotiatedProtocolVersion = negotiated;

        return new
        {
            protocolVersion = negotiated,
            capabilities = GetCapabilities(),
            serverInfo = new { name = _options.ServerName, version = _options.ServerVersion }
        };
    }

    private static bool TryGetParams(JsonElement requestElement, out JsonElement paramsElement)
    {
        if (requestElement.TryGetProperty("params", out paramsElement) &&
            paramsElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        paramsElement = default;
        return false;
    }

    /// <summary>
    /// Handles a single MCP request and returns the result.
    /// </summary>
    private object? HandleRequest(string? method, JsonElement requestElement)
    {
        bool isModern = IsModernProtocol(requestElement);

        return method switch
        {
            // Modern stateless discovery (MCP 2026-07-28+)
            "server/discover" => HandleDiscover(),

            // Initialize - returns server capabilities and negotiated protocol version
            "initialize" => HandleInitialize(requestElement),

            // Notifications - no response required
            "notifications/initialized" => null,
            "initialized" => null,
            "notifications/cancelled" => null,
            "cancelled" => null,
            "notifications/progress" => null,
            "notifications/roots/list_changed" => null,

            // Ping & Logging
            "ping" => new { },
            "logging/setLevel" => new { },

            // Tools
            "tools/list" => isModern
                ? new
                {
                    resultType = "complete",
                    tools = _toolHandler.GetToolDefinitions(),
                    ttlMs = 0,
                    cacheScope = "private"
                }
                : new { tools = _toolHandler.GetToolDefinitions() },

            "tools/call" => TryGetParams(requestElement, out var toolParams)
                ? _toolHandler.HandleToolCall(toolParams, isModern)
                : new JsonRpcError(-32602, "Invalid params: missing 'params' object"),

            // Resources
            "resources/list" => isModern
                ? new
                {
                    resultType = "complete",
                    resources = _resourceHandler.GetResourceDefinitions(),
                    ttlMs = 0,
                    cacheScope = "private"
                }
                : new { resources = _resourceHandler.GetResourceDefinitions() },

            "resources/read" => TryGetParams(requestElement, out var resourceParams)
                ? _resourceHandler.HandleResourceRead(resourceParams, isModern)
                : new JsonRpcError(-32602, "Invalid params: missing 'params' object"),

            "resources/templates/list" => isModern
                ? new
                {
                    resultType = "complete",
                    resourceTemplates = _resourceTemplateHandler.GetTemplateDefinitions(),
                    ttlMs = 0,
                    cacheScope = "private"
                }
                : new { resourceTemplates = _resourceTemplateHandler.GetTemplateDefinitions() },

            "resources/subscribe" => new { },
            "resources/unsubscribe" => new { },

            // Prompts
            "prompts/list" => isModern
                ? new
                {
                    resultType = "complete",
                    prompts = _promptHandler.GetPromptDefinitions(),
                    ttlMs = 0,
                    cacheScope = "private"
                }
                : new { prompts = _promptHandler.GetPromptDefinitions() },

            "prompts/get" => TryGetParams(requestElement, out var promptParams)
                ? _promptHandler.HandlePromptGet(promptParams, isModern)
                : new JsonRpcError(-32602, "Invalid params: missing 'params' object"),

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
            _ => new JsonRpcError(-32601, "Method not found")
        };
    }
}
