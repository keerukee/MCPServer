namespace MCPServer.Attributes;

/// <summary>
/// Attribute to mark a parameter with additional metadata for JSON schema generation.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class McpParameterAttribute : Attribute
{
    /// <summary>
    /// Description of the parameter for the JSON schema.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Whether this parameter is required (default: true).
    /// </summary>
    public bool Required { get; }

    public McpParameterAttribute(string description, bool required = true)
    {
        Description = description;
        Required = required;
    }
}
