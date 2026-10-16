using System.Reflection;
using System.Text.Json;
using MCPServer.Attributes;

namespace MCPServer.Handlers;

/// <summary>
/// Handles tool-related MCP requests by discovering and invoking [McpTool] methods.
/// </summary>
public class McpToolHandler
{
    // Cache of discovered tool methods for performance
    private readonly Dictionary<string, MethodInfo> _toolMethods;

    /// <summary>
    /// Gets the number of registered tools.
    /// </summary>
    public int Count => _toolMethods.Count;

    public McpToolHandler(Assembly assembly)
    {
        _toolMethods = DiscoverTools(assembly);
    }

    /// <summary>
    /// Discovers all methods decorated with [McpTool] in the specified assembly.
    /// </summary>
    private static Dictionary<string, MethodInfo> DiscoverTools(Assembly assembly)
    {
        var tools = new Dictionary<string, MethodInfo>();

        // Scan all types in the assembly
        foreach (var type in assembly.GetTypes())
        {
            // Look for static methods with the McpTool attribute
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<McpToolAttribute>();
                if (attr != null)
                {
                    // Register the method with its tool name
                    tools[attr.Name] = method;
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// Generates tool definitions for the tools/list response.
    /// </summary>
    public object[] GetToolDefinitions()
    {
        var definitions = new List<object>();

        foreach (var (name, method) in _toolMethods)
        {
            var attr = method.GetCustomAttribute<McpToolAttribute>()!;

            // Build the input schema from method parameters
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var param in method.GetParameters())
            {
                var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
                var description = paramAttr?.Description ?? param.Name ?? "";
                var isRequired = paramAttr?.Required ?? (!param.HasDefaultValue && Nullable.GetUnderlyingType(param.ParameterType) == null);

                // Map C# types to JSON schema types
                properties[param.Name!] = new
                {
                    type = GetJsonSchemaType(param.ParameterType),
                    description = description
                };

                if (isRequired)
                {
                    required.Add(param.Name!);
                }
            }

            definitions.Add(new
            {
                name = attr.Name,
                description = attr.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = properties,
                    required = required
                }
            });
        }

        return definitions.ToArray();
    }

    /// <summary>
    /// Handles a tools/call request by invoking the appropriate method.
    /// </summary>
    public object HandleToolCall(JsonElement paramsElement, bool isModern = false)
    {
        if (!paramsElement.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return CreateToolResponse("Error: Missing tool 'name'", isError: true, isModern);
        }

        var toolName = nameElement.GetString()!;

        // Check if the tool exists
        if (!_toolMethods.TryGetValue(toolName, out var method))
        {
            return CreateToolResponse($"Tool '{toolName}' not found", isError: true, isModern);
        }

        try
        {
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
                var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();

                if (args.ValueKind == JsonValueKind.Object &&
                    args.TryGetProperty(param.Name!, out var value) &&
                    value.ValueKind != JsonValueKind.Null &&
                    value.ValueKind != JsonValueKind.Undefined)
                {
                    // Convert JSON value to the parameter type
                    paramValues[i] = ConvertJsonValue(value, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    paramValues[i] = param.DefaultValue;
                }
                else if (paramAttr?.Required == false || Nullable.GetUnderlyingType(param.ParameterType) != null)
                {
                    paramValues[i] = param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null
                        ? Activator.CreateInstance(param.ParameterType)
                        : null;
                }
                else
                {
                    throw new ArgumentException($"Missing required parameter: {param.Name}");
                }
            }

            // Invoke the tool method
            var result = method.Invoke(null, paramValues);

            // Unwrap Task / Task<T> if the tool method is async
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
                var taskType = task.GetType();
                result = taskType.IsGenericType
                    ? taskType.GetProperty("Result")?.GetValue(task)
                    : null;
            }

            // Return MCP-compliant response
            return CreateToolResponse(result?.ToString() ?? "", isError: false, isModern);
        }
        catch (Exception ex)
        {
            // Return error response if invocation fails
            var message = ex.InnerException?.Message ?? ex.Message;
            return CreateToolResponse($"Error: {message}", isError: true, isModern);
        }
    }

    private static object CreateToolResponse(string text, bool isError, bool isModern) =>
        isModern
            ? new
            {
                resultType = "complete",
                content = new[] { new { type = "text", text = text } },
                isError = isError
            }
            : new
            {
                content = new[] { new { type = "text", text = text } },
                isError = isError
            };

    /// <summary>
    /// Maps C# types to JSON schema type strings.
    /// </summary>
    private static string GetJsonSchemaType(Type type)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType switch
        {
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(int) || t == typeof(long) || t == typeof(short) => "integer",
            Type t when t == typeof(double) || t == typeof(float) || t == typeof(decimal) => "number",
            Type t when t == typeof(bool) => "boolean",
            Type t when t.IsArray => "array",
            _ => "object"
        };
    }

    /// <summary>
    /// Converts a JSON value to the specified C# type.
    /// </summary>
    private static object? ConvertJsonValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return underlyingType switch
        {
            Type t when t == typeof(string) => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText(),
            Type t when t == typeof(int) => element.ValueKind == JsonValueKind.String ? int.Parse(element.GetString()!) : element.GetInt32(),
            Type t when t == typeof(long) => element.ValueKind == JsonValueKind.String ? long.Parse(element.GetString()!) : element.GetInt64(),
            Type t when t == typeof(short) => element.ValueKind == JsonValueKind.String ? short.Parse(element.GetString()!) : element.GetInt16(),
            Type t when t == typeof(double) => element.ValueKind == JsonValueKind.String ? double.Parse(element.GetString()!) : element.GetDouble(),
            Type t when t == typeof(float) => element.ValueKind == JsonValueKind.String ? float.Parse(element.GetString()!) : element.GetSingle(),
            Type t when t == typeof(decimal) => element.ValueKind == JsonValueKind.String ? decimal.Parse(element.GetString()!) : element.GetDecimal(),
            Type t when t == typeof(bool) => element.ValueKind == JsonValueKind.String ? bool.Parse(element.GetString()!) : element.GetBoolean(),
            _ => element.GetRawText()
        };
    }
}
