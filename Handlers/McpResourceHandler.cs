using System.Reflection;
using System.Text.Json;
using MCPServer.Attributes;

namespace MCPServer.Handlers;

/// <summary>
/// Handles resource-related MCP requests by discovering [McpResource] methods.
/// </summary>
public class McpResourceHandler
{
    // Cache of discovered resource methods
    private readonly Dictionary<string, MethodInfo> _resourceMethods;

    /// <summary>
    /// Gets the number of registered resources.
    /// </summary>
    public int Count => _resourceMethods.Count;

    public McpResourceHandler(Assembly assembly)
    {
        _resourceMethods = DiscoverResources(assembly);
    }

    /// <summary>
    /// Discovers all methods decorated with [McpResource] in the specified assembly.
    /// </summary>
    private static Dictionary<string, MethodInfo> DiscoverResources(Assembly assembly)
    {
        var resources = new Dictionary<string, MethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<McpResourceAttribute>();
                if (attr != null)
                {
                    resources[attr.Uri] = method;
                }
            }
        }

        return resources;
    }

    /// <summary>
    /// Generates resource definitions for the resources/list response.
    /// </summary>
    public object[] GetResourceDefinitions()
    {
        var definitions = new List<object>();

        foreach (var (uri, method) in _resourceMethods)
        {
            var attr = method.GetCustomAttribute<McpResourceAttribute>()!;
            definitions.Add(new
            {
                uri = attr.Uri,
                name = attr.Name,
                description = attr.Description,
                mimeType = attr.MimeType
            });
        }

        return definitions.ToArray();
    }

    /// <summary>
    /// Handles a resources/read request by invoking the appropriate method.
    /// </summary>
    public object HandleResourceRead(JsonElement paramsElement, bool isModern = false)
    {
        var uri = paramsElement.TryGetProperty("uri", out var uriElement) &&
                  uriElement.ValueKind == JsonValueKind.String
            ? uriElement.GetString()!
            : "";

        if (!_resourceMethods.TryGetValue(uri, out var method))
        {
            return CreateResourceResponse(uri, $"Resource '{uri}' not found", "text/plain", isModern);
        }

        try
        {
            var attr = method.GetCustomAttribute<McpResourceAttribute>()!;
            var result = method.Invoke(null, null);

            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
                var taskType = task.GetType();
                result = taskType.IsGenericType
                    ? taskType.GetProperty("Result")?.GetValue(task)
                    : null;
            }

            return CreateResourceResponse(uri, result?.ToString() ?? "", attr.MimeType, isModern);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return CreateResourceResponse(uri, $"Error: {message}", "text/plain", isModern);
        }
    }

    private static object CreateResourceResponse(string uri, string text, string mimeType, bool isModern) =>
        isModern
            ? new
            {
                resultType = "complete",
                contents = new[] { new { uri = uri, text = text, mimeType = mimeType } },
                ttlMs = 0,
                cacheScope = "private"
            }
            : new
            {
                contents = new[] { new { uri = uri, text = text, mimeType = mimeType } }
            };
}
