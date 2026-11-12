using System.Security.Cryptography;
using System.Text;

namespace GraphMailFetcher;

internal sealed record ExportResult(int Found, int Matched, int Exported, int AlreadyPresent, int Failed);

/// <summary>Exports the matching messages of a mailbox as .eml files, skipping the ones that are already there.</summary>
internal sealed class MailExporter(GraphClient graph, string outputFolder, MailFilter? filter, TextWriter log)
{
    private const int MaxSubjectLength = 60;

    public async Task<ExportResult> ExportMailboxAsync(string mailbox, DateTimeOffset since, CancellationToken ct)
    {
        var folder = Path.Combine(outputFolder, SanitizeFileName(mailbox.ToLowerInvariant(), 100));
        Directory.CreateDirectory(folder);

        // Every exported file name ends in a key derived from the message id; those keys tell us what is already there.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*.eml"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            existing.Add(name[(name.LastIndexOf('_') + 1)..]);
        }

        int found = 0, matched = 0, exported = 0, alreadyPresent = 0, failed = 0;

        await foreach (var message in graph.ListMessagesAsync(mailbox, since, filter?.UsesBody == true, ct))
        {
            if (message.IsDraft)
                continue;
            found++;
            if (filter is not null && !filter.IsMatch(message))
                continue;
            matched++;

            var key = GetKey(message.Id);
            if (existing.Contains(key))
            {
                alreadyPresent++;
                continue;
            }

            var fileName = $"{message.Received.UtcDateTime:yyyyMMdd-HHmmss}_{SanitizeFileName(message.Subject, MaxSubjectLength)}_{key}.eml";
            var path = Path.Combine(folder, fileName);
            var temporary = path + ".partial";
            try
            {
                // Download to a temporary name first so an interrupted run never leaves a truncated .eml behind.
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    await graph.DownloadMimeAsync(mailbox, message.Id, stream, ct);
                File.Move(temporary, path, overwrite: true);
                existing.Add(key);
                exported++;
                log.WriteLine($"  exported {fileName}");
            }
            catch (Exception ex) when (ex is GraphException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                failed++;
                log.WriteLine($"  FAILED   '{message.Subject}' ({message.Received:u}): {ex.Message}");
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        return new ExportResult(found, matched, exported, alreadyPresent, failed);
    }

    internal static string GetKey(string messageId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId)), 0, 8).ToLowerInvariant();

    /// <summary>Makes a string safe as a file name on both Windows (local and UNC) and Unix file systems.</summary>
    internal static string SanitizeFileName(string value, int maxLength)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            // '_' separates the parts of the file name, so it is replaced as well.
            if (char.IsControl(c) || char.IsSurrogate(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' or '_')
                sb.Append(' ');
            else
                sb.Append(c);
        }

        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength];
        cleaned = cleaned.Trim(' ', '.');
        return cleaned.Length == 0 ? "no subject" : cleaned;
    }
}
