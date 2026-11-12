namespace GraphMailFetcher;

/// <summary>The parts of a message that are needed to filter and name it.</summary>
internal sealed record MailMessage(
    string Id,
    string Subject,
    string? Body,
    IReadOnlyList<string> Senders,
    IReadOnlyList<string> Recipients,
    DateTimeOffset Received,
    bool IsDraft);
