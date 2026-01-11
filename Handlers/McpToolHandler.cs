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
                var isRequired = paramAttr?.Required ?? true;

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
    public object HandleToolCall(JsonElement paramsElement)
    {
        var toolName = paramsElement.GetProperty("name").GetString()!;

        // Check if the tool exists
        if (!_toolMethods.TryGetValue(toolName, out var method))
        {
            return new
            {
                content = new[] { new { type = "text", text = $"Tool '{toolName}' not found" } },
                isError = true
            };
        }

        try
        {
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
                    // Convert JSON value to the parameter type
                    paramValues[i] = ConvertJsonValue(value, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    paramValues[i] = param.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Missing required parameter: {param.Name}");
                }
            }

            // Invoke the tool method
            var result = method.Invoke(null, paramValues);

            // Return MCP-compliant response
            return new
            {
                content = new[] { new { type = "text", text = result?.ToString() ?? "" } },
                isError = false
            };
        }
        catch (Exception ex)
        {
            // Return error response if invocation fails
            var message = ex.InnerException?.Message ?? ex.Message;
            return new
            {
                content = new[] { new { type = "text", text = $"Error: {message}" } },
                isError = true
            };
        }
    }

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
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return underlyingType switch
        {
            Type t when t == typeof(string) => element.GetString(),
            Type t when t == typeof(int) => element.GetInt32(),
            Type t when t == typeof(long) => element.GetInt64(),
            Type t when t == typeof(double) => element.GetDouble(),
            Type t when t == typeof(float) => element.GetSingle(),
            Type t when t == typeof(decimal) => element.GetDecimal(),
            Type t when t == typeof(bool) => element.GetBoolean(),
            _ => element.GetRawText()
        };
    }
}
