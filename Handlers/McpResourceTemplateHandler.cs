using System.Reflection;
using MCPServer.Attributes;

namespace MCPServer.Handlers;

/// <summary>
/// Handles resource template MCP requests by discovering [McpResourceTemplate] methods.
/// </summary>
public class McpResourceTemplateHandler
{
    // Cache of discovered resource template methods
    private readonly Dictionary<string, MethodInfo> _templateMethods;

    public McpResourceTemplateHandler(Assembly assembly)
    {
        _templateMethods = DiscoverTemplates(assembly);
    }

    /// <summary>
    /// Discovers all methods decorated with [McpResourceTemplate] in the specified assembly.
    /// </summary>
    private static Dictionary<string, MethodInfo> DiscoverTemplates(Assembly assembly)
    {
        var templates = new Dictionary<string, MethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<McpResourceTemplateAttribute>();
                if (attr != null)
                {
                    templates[attr.UriTemplate] = method;
                }
            }
        }

        return templates;
    }

    /// <summary>
    /// Generates template definitions for the resources/templates/list response.
    /// </summary>
    public object[] GetTemplateDefinitions()
    {
        var definitions = new List<object>();

        foreach (var (uriTemplate, method) in _templateMethods)
        {
            var attr = method.GetCustomAttribute<McpResourceTemplateAttribute>()!;
            definitions.Add(new
            {
                uriTemplate = attr.UriTemplate,
                name = attr.Name,
                description = attr.Description,
                mimeType = attr.MimeType
            });
        }

        return definitions.ToArray();
    }
}
