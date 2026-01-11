namespace MCPServer.Attributes;

/// <summary>
/// Attribute to mark a method as an MCP Resource.
/// Resources provide read-only data to clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpResourceAttribute : Attribute
{
    /// <summary>
    /// The URI that identifies this resource.
    /// </summary>
    public string Uri { get; }

    /// <summary>
    /// Human-readable name of the resource.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description of what the resource provides.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// MIME type of the resource content.
    /// </summary>
    public string MimeType { get; }

    public McpResourceAttribute(string uri, string name, string description, string mimeType = "text/plain")
    {
        Uri = uri;
        Name = name;
        Description = description;
        MimeType = mimeType;
    }
}
