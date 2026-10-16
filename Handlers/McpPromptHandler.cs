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

    /// <summary>
    /// Gets the number of registered prompts.
    /// </summary>
    public int Count => _promptMethods.Count;

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
    public object HandlePromptGet(JsonElement paramsElement, bool isModern = false)
    {
        var promptName = paramsElement.TryGetProperty("name", out var nameElement) &&
                         nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : "";

        if (!_promptMethods.TryGetValue(promptName, out var method))
        {
            return isModern
                ? new { resultType = "complete", description = "", messages = Array.Empty<object>() }
                : new { description = "", messages = Array.Empty<object>() };
        }

        try
        {
            var attr = method.GetCustomAttribute<McpPromptAttribute>()!;

            // Extract arguments from the request
            var args = paramsElement.TryGetProperty("arguments", out var argsElement) &&
                       argsElement.ValueKind == JsonValueKind.Object
                ? argsElement
                : default;

            // Build parameter values for the method invocation
            var parameters = method.GetParameters();
            var paramValues = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (args.ValueKind == JsonValueKind.Object &&
                    args.TryGetProperty(param.Name!, out var value) &&
                    value.ValueKind != JsonValueKind.Null &&
                    value.ValueKind != JsonValueKind.Undefined)
                {
                    paramValues[i] = value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : value.GetRawText();
                }
                else if (param.HasDefaultValue)
                {
                    paramValues[i] = param.DefaultValue;
                }
            }

            // Invoke the prompt method - expects it to return the prompt text
            var result = method.Invoke(null, paramValues);

            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
                var taskType = task.GetType();
                result = taskType.IsGenericType
                    ? taskType.GetProperty("Result")?.GetValue(task)
                    : null;
            }

            return CreatePromptResponse(attr.Description, result?.ToString() ?? "", isModern);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return CreatePromptResponse("Error", $"Error: {message}", isModern);
        }
    }

    private static object CreatePromptResponse(string description, string text, bool isModern)
    {
        var messages = new[]
        {
            new
            {
                role = "user",
                content = new { type = "text", text = text }
            }
        };

        return isModern
            ? new { resultType = "complete", description = description, messages = messages }
            : new { description = description, messages = messages };
    }
}
