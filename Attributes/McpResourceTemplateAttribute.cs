namespace MCPServer.Attributes;

/// <summary>
/// Attribute to mark a method as an MCP Resource Template.
/// Templates define URI patterns for dynamic resources.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpResourceTemplateAttribute : Attribute
{
    /// <summary>
    /// The URI template pattern (e.g., "file:///{path}").
    /// </summary>
    public string UriTemplate { get; }

    /// <summary>
    /// Human-readable name of the template.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description of what the template provides.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// MIME type of the resource content.
    /// </summary>
    public string MimeType { get; }

    public McpResourceTemplateAttribute(string uriTemplate, string name, string description, string mimeType = "text/plain")
    {
        UriTemplate = uriTemplate;
        Name = name;
        Description = description;
        MimeType = mimeType;
    }
}
