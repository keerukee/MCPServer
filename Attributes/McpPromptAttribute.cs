namespace MCPServer.Attributes;

/// <summary>
/// Attribute to mark a method as an MCP Prompt.
/// Prompts are reusable prompt templates with arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpPromptAttribute : Attribute
{
    /// <summary>
    /// The unique name of the prompt.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description of what the prompt does.
    /// </summary>
    public string Description { get; }

    public McpPromptAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
