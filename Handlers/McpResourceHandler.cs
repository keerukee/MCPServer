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
    public object HandleResourceRead(JsonElement paramsElement)
    {
        var uri = paramsElement.GetProperty("uri").GetString()!;

        if (!_resourceMethods.TryGetValue(uri, out var method))
        {
            return new
            {
                contents = new[] { new { uri = uri, text = $"Resource '{uri}' not found", mimeType = "text/plain" } }
            };
        }

        try
        {
            var attr = method.GetCustomAttribute<McpResourceAttribute>()!;
            var result = method.Invoke(null, null);

            return new
            {
                contents = new[] { new { uri = uri, text = result?.ToString() ?? "", mimeType = attr.MimeType } }
            };
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new
            {
                contents = new[] { new { uri = uri, text = $"Error: {message}", mimeType = "text/plain" } }
            };
        }
    }
}
