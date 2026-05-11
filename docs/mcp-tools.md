# MCP tools

The Expert Agents MCP server is exposed at `POST /mcp` (JSON-RPC 2.0) and `GET /mcp` (SSE).
Use `GET /mcp/health` for a lightweight readiness check.

## Common JSON-RPC envelope

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "search_code",
    "arguments": {
      "repo": "fhir-server",
      "query": "export"
    }
  }
}
```

Tool responses are returned as text content whose `text` field contains a JSON string.

## Tool catalog

### `search_code`
Search indexed code snippets in a managed repository.

**Input schema**
```json
{
  "type": "object",
  "required": ["repo", "query"],
  "properties": {
    "repo": { "type": "string", "description": "Repository identifier." },
    "query": { "type": "string", "description": "Free-text search query." },
    "topK": { "type": "integer", "description": "Maximum results to return.", "default": 5 }
  }
}
```

**Example arguments**
```json
{
  "repo": "fhir-server",
  "query": "export job",
  "topK": 3
}
```

### `get_file_content`
Fetch the full content of a known file path.

**Input schema**
```json
{
  "type": "object",
  "required": ["repo", "filePath"],
  "properties": {
    "repo": { "type": "string" },
    "filePath": { "type": "string", "description": "Repository-relative file path." }
  }
}
```

**Example arguments**
```json
{
  "repo": "fhir-server",
  "filePath": "src/Microsoft.Health.Fhir.Core/Features/Operations/Export/ExportJobWorker.cs"
}
```

### `explain_architecture`
Route an architecture question to the best expert agent.

**Input schema**
```json
{
  "type": "object",
  "required": ["component"],
  "properties": {
    "component": { "type": "string", "description": "Component or subsystem name." }
  }
}
```

**Example arguments**
```json
{
  "component": "FHIR export pipeline"
}
```

### `create_pr`
Return pull request guidance for a repository.

**Input schema**
```json
{
  "type": "object",
  "required": ["repo"],
  "properties": {
    "repo": { "type": "string" },
    "title": { "type": "string", "default": "Untitled PR" },
    "description": { "type": "string", "default": "" }
  }
}
```

**Example arguments**
```json
{
  "repo": "healthcare-shared-components",
  "title": "Add retry metrics",
  "description": "Captures retry counters for storage operations."
}
```

### `list_agents`
Return all registered expert agents and their advertised skills.

**Input schema**
```json
{
  "type": "object",
  "properties": {}
}
```

**Example arguments**
```json
{}
```

### `ask_agent`
Ask a specific agent a question and receive a tracked thread id.

**Input schema**
```json
{
  "type": "object",
  "required": ["agentId", "message"],
  "properties": {
    "agentId": { "type": "string" },
    "message": { "type": "string" }
  }
}
```

**Example arguments**
```json
{
  "agentId": "fhir-server-expert",
  "message": "How does SQL search build queries?"
}
```

### `list_repositories`
List managed repositories from `IRepositoryRegistry` or built-in seed data.

**Input schema**
```json
{
  "type": "object",
  "properties": {}
}
```

**Example arguments**
```json
{}
```

### `ask_repo_expert`
Ask the expert agent that owns a repository.

**Input schema**
```json
{
  "type": "object",
  "required": ["repoId", "question"],
  "properties": {
    "repoId": { "type": "string" },
    "question": { "type": "string" },
    "threadId": { "type": "string", "description": "Optional prior thread to continue." }
  }
}
```

**Example arguments**
```json
{
  "repoId": "healthcare-shared-components",
  "question": "Explain the retry wrapper layering.",
  "threadId": "optional-existing-thread"
}
```

### `submit_followup`
Continue an MCP conversation thread. If the thread is unknown, a new one is created.

**Input schema**
```json
{
  "type": "object",
  "required": ["threadId", "message"],
  "properties": {
    "threadId": { "type": "string" },
    "message": { "type": "string" }
  }
}
```

**Example arguments**
```json
{
  "threadId": "9bc8d8b60e2042d0aa0a1f2f44ecf0d1",
  "message": "Give me the SQL-specific details next."
}
```

## Sample tool call payloads

### `search_code`
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "search_code",
    "arguments": {
      "repo": "fhir-server",
      "query": "export",
      "topK": 5
    }
  }
}
```

### `list_agents`
```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "tools/call",
  "params": {
    "name": "list_agents",
    "arguments": {}
  }
}
```

## Health endpoint

```http
GET /mcp/health
```

Returns:

```json
{
  "status": "ok",
  "protocolVersion": "2024-11-05",
  "tools": 9
}
```
