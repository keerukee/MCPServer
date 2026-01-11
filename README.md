# MCPServer

A lightweight .NET library for building MCP (Model Context Protocol) servers without external dependencies. Simply decorate your methods with attributes and the library handles all the protocol communication.

## Features

- **Zero external dependencies** - Uses only built-in .NET libraries
- **Attribute-based registration** - Decorate methods with `[McpTool]`, `[McpResource]`, or `[McpPrompt]`
- **Automatic discovery** - Tools, resources, and prompts are discovered via reflection
- **JSON Schema generation** - Input schemas are automatically generated from method parameters
- **Full MCP compliance** - Supports all standard MCP methods for client compatibility

## Installation

Add a reference to the MCPServer project:

```xml
<ItemGroup>
  <ProjectReference Include="..\MCPServer\MCPServer.csproj" />
</ItemGroup>
```

Or if published as a NuGet package:

```xml
<ItemGroup>
  <PackageReference Include="MCPServer" Version="1.0.0" />
</ItemGroup>
```

## Quick Start

### 1. Create the Server Entry Point

```csharp
using MCPServer;

var server = new McpServerHost(new McpServerOptions
{
    ServerName = "MyMCPServer",
    ServerVersion = "1.0.0"
});

server.Run();
```

### 2. Add Tools

Create a class with static methods decorated with `[McpTool]`:

```csharp
using MCPServer.Attributes;

public static class MyTools
{
    [McpTool("greet", "Greets a person by name")]
    public static string Greet(
        [McpParameter("The name of the person to greet")] string name)
    {
        return $"Hello, {name}!";
    }

    [McpTool("calculate_sum", "Adds two numbers together")]
    public static string CalculateSum(
        [McpParameter("First number")] double a,
        [McpParameter("Second number")] double b)
    {
        return $"The sum is {a + b}";
    }
}
```

### 3. Add Resources (Optional)

Resources provide read-only data to clients:

```csharp
using MCPServer.Attributes;

public static class MyResources
{
    [McpResource("config://app", "App Configuration", "Returns application configuration", "application/json")]
    public static string GetAppConfig()
    {
        return """{"version": "1.0.0", "environment": "production"}""";
    }
}
```

### 4. Add Prompts (Optional)

Prompts are reusable prompt templates:

```csharp
using MCPServer.Attributes;

public static class MyPrompts
{
    [McpPrompt("code_review", "Generates a code review prompt")]
    public static string CodeReviewPrompt(
        [McpParameter("Programming language")] string language,
        [McpParameter("Code to review")] string code)
    {
        return $"Please review this {language} code:\n```{language}\n{code}\n```";
    }
}
```

## Attributes Reference

### `[McpTool(name, description)]`

Marks a static method as an MCP tool.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | string | Unique identifier for the tool |
| `description` | string | Human-readable description |

### `[McpParameter(description, required)]`

Adds metadata to method parameters.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `description` | string | - | Description for the JSON schema |
| `required` | bool | `true` | Whether the parameter is required |

### `[McpResource(uri, name, description, mimeType)]`

Marks a static method as an MCP resource.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `uri` | string | - | URI that identifies this resource |
| `name` | string | - | Human-readable name |
| `description` | string | - | Description of the resource |
| `mimeType` | string | `"text/plain"` | MIME type of the content |

### `[McpResourceTemplate(uriTemplate, name, description, mimeType)]`

Marks a static method as an MCP resource template.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `uriTemplate` | string | - | URI template pattern (e.g., `"file:///{path}"`) |
| `name` | string | - | Human-readable name |
| `description` | string | - | Description of the template |
| `mimeType` | string | `"text/plain"` | MIME type of the content |

### `[McpPrompt(name, description)]`

Marks a static method as an MCP prompt.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | string | Unique identifier for the prompt |
| `description` | string | Human-readable description |

## Server Configuration

```csharp
var server = new McpServerHost(new McpServerOptions
{
    ServerName = "MyServer",           // Name shown to clients
    ServerVersion = "1.0.0",           // Your server version
    ProtocolVersion = "2024-11-05"     // MCP protocol version
});
```

## Supported Parameter Types

The library automatically converts JSON values to these C# types:

| C# Type | JSON Schema Type |
|---------|------------------|
| `string` | `string` |
| `int`, `long`, `short` | `integer` |
| `double`, `float`, `decimal` | `number` |
| `bool` | `boolean` |
| Arrays | `array` |
| Other types | `object` |

## MCP Client Configuration

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "my-server": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/your/project.csproj"]
    }
  }
}
```

Or if using a compiled executable:

```json
{
  "mcpServers": {
    "my-server": {
      "command": "path/to/your/server.exe"
    }
  }
}
```

### VS Code / Continue

Add to your MCP configuration:

```json
{
  "servers": {
    "my-server": {
      "command": "dotnet",
      "args": ["path/to/your/server.dll"]
    }
  }
}
```

## Supported MCP Methods

| Method | Description |
|--------|-------------|
| `initialize` | Returns server capabilities and info |
| `ping` | Health check |
| `tools/list` | Lists available tools |
| `tools/call` | Executes a tool |
| `resources/list` | Lists available resources |
| `resources/read` | Reads a resource |
| `resources/templates/list` | Lists resource templates |
| `prompts/list` | Lists available prompts |
| `prompts/get` | Gets a prompt with arguments |
| `completion/complete` | Autocompletion (returns empty) |

## Example Project Structure

```
MyMCPServer/
├── MyMCPServer.csproj
├── Program.cs
└── Features/
    ├── Tools.cs
    ├── Resources.cs
    └── Prompts.cs
```

## License

MIT License
