namespace MCPServer.Attributes;

/// <summary>
/// Attribute to mark a method as an MCP Tool.
/// The method will be automatically discovered and registered.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpToolAttribute : Attribute
{
    /// <summary>
    /// The unique name of the tool (used in tools/call).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    public string Description { get; }

    public McpToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
