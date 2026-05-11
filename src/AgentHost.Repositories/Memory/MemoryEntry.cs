namespace AgentHost.Repositories.Memory;

/// <summary>A persisted memory entry produced by summarising a conversation thread.</summary>
public sealed record MemoryEntry(
    Guid Id,
    string AgentId,
    string ThreadId,
    string Summary,
    IReadOnlyList<string> Tags,
    Guid? VectorId,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

/// <summary>A raw turn recorded in short-term memory.</summary>
public sealed record TurnRecord(
    string AgentId,
    string ThreadId,
    string Role,
    string Content,
    DateTime RecordedAt);
