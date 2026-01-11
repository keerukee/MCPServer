using System.Reflection;
using System.Text.Json;
using MCPServer.Attributes;

namespace MCPServer.Handlers;

/// <summary>
/// Handles prompt-related MCP requests by discovering [McpPrompt] methods.
/// </summary>
public class McpPromptHandler
{
    // Cache of discovered prompt methods
    private readonly Dictionary<string, MethodInfo> _promptMethods;

    public McpPromptHandler(Assembly assembly)
    {
        _promptMethods = DiscoverPrompts(assembly);
    }

    /// <summary>
    /// Discovers all methods decorated with [McpPrompt] in the specified assembly.
    /// </summary>
    private static Dictionary<string, MethodInfo> DiscoverPrompts(Assembly assembly)
    {
        var prompts = new Dictionary<string, MethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<McpPromptAttribute>();
                if (attr != null)
                {
                    prompts[attr.Name] = method;
                }
            }
        }

        return prompts;
    }

    /// <summary>
    /// Generates prompt definitions for the prompts/list response.
    /// </summary>
    public object[] GetPromptDefinitions()
    {
        var definitions = new List<object>();

        foreach (var (name, method) in _promptMethods)
        {
            var attr = method.GetCustomAttribute<McpPromptAttribute>()!;

            // Build arguments list from method parameters
            var arguments = new List<object>();
            foreach (var param in method.GetParameters())
            {
                var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
                arguments.Add(new
                {
                    name = param.Name,
                    description = paramAttr?.Description ?? "",
                    required = paramAttr?.Required ?? true
                });
            }

            definitions.Add(new
            {
                name = attr.Name,
                description = attr.Description,
                arguments = arguments
            });
        }

        return definitions.ToArray();
    }

    /// <summary>
    /// Handles a prompts/get request by invoking the appropriate method.
    /// </summary>
    public object HandlePromptGet(JsonElement paramsElement)
    {
        var promptName = paramsElement.GetProperty("name").GetString()!;

        if (!_promptMethods.TryGetValue(promptName, out var method))
        {
            return new { description = "", messages = Array.Empty<object>() };
        }

        try
        {
            var attr = method.GetCustomAttribute<McpPromptAttribute>()!;

            // Extract arguments from the request
            var args = paramsElement.TryGetProperty("arguments", out var argsElement)
                ? argsElement
                : default;

            // Build parameter values for the method invocation
            var parameters = method.GetParameters();
            var paramValues = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (args.ValueKind != JsonValueKind.Undefined &&
                    args.TryGetProperty(param.Name!, out var value))
                {
                    paramValues[i] = value.GetString();
                }
                else if (param.HasDefaultValue)
                {
                    paramValues[i] = param.DefaultValue;
                }
            }

            // Invoke the prompt method - expects it to return the prompt text
            var result = method.Invoke(null, paramValues);

            return new
            {
                description = attr.Description,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new { type = "text", text = result?.ToString() ?? "" }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new
            {
                description = "Error",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new { type = "text", text = $"Error: {message}" }
                    }
                }
            };
        }
    }
}
