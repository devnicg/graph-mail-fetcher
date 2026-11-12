using System.Text.Encodings.Web;
using System.Text.Json;

namespace GraphMailFetcher;

/// <summary>Writes a Graph message (with expanded attachments) as a compact, self-contained JSON document.</summary>
internal static class MessageJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        // Keeps HTML bodies and non-ASCII text readable instead of \uXXXX-escaping them; the output is still valid JSON.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(JsonElement message, Stream destination, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(destination, WriterOptions);
        writer.WriteStartObject();

        foreach (var property in (ReadOnlySpan<string>)["id", "internetMessageId", "conversationId", "subject", "receivedDateTime", "sentDateTime"])
            Copy(writer, message, property);

        foreach (var property in (ReadOnlySpan<string>)["from", "sender"])
        {
            writer.WritePropertyName(property);
            if (message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object)
                WriteAddress(writer, value);
            else
                writer.WriteNullValue();
        }

        foreach (var property in (ReadOnlySpan<string>)["toRecipients", "ccRecipients", "bccRecipients", "replyTo"])
        {
            writer.WriteStartArray(property);
            foreach (var recipient in Items(message, property))
                WriteAddress(writer, recipient);
            writer.WriteEndArray();
        }

        foreach (var property in (ReadOnlySpan<string>)["importance", "isRead", "hasAttachments", "bodyPreview"])
            Copy(writer, message, property);

        writer.WriteStartObject("body");
        message.TryGetProperty("body", out var body);
        Copy(writer, body, "contentType");
        Copy(writer, body, "content");
        writer.WriteEndObject();

        writer.WriteStartArray("attachments");
        foreach (var attachment in Items(message, "attachments"))
        {
            writer.WriteStartObject();
            // file = regular attachment with contentBytes; item = attached mail/event/contact; reference = link to a cloud file.
            var type = attachment.TryGetProperty("@odata.type", out var odataType) ? odataType.GetString() : null;
            writer.WriteString("type", type switch
            {
                "#microsoft.graph.fileAttachment" => "file",
                "#microsoft.graph.itemAttachment" => "item",
                "#microsoft.graph.referenceAttachment" => "reference",
                _ => type,
            });
            foreach (var property in (ReadOnlySpan<string>)["name", "contentType", "size", "isInline", "contentId", "contentBytes"])
                Copy(writer, attachment, property);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        await writer.FlushAsync(ct);
    }

    /// <summary>Copies a property as is, or writes null when it is absent, so every file has the same shape.</summary>
    private static void Copy(Utf8JsonWriter writer, JsonElement parent, string property)
    {
        writer.WritePropertyName(property);
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value))
            value.WriteTo(writer);
        else
            writer.WriteNullValue();
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    /// <summary>Flattens Graph's { emailAddress: { name, address } } to { name, address }.</summary>
    private static void WriteAddress(Utf8JsonWriter writer, JsonElement recipient)
    {
        recipient.TryGetProperty("emailAddress", out var email);
        writer.WriteStartObject();
        Copy(writer, email, "name");
        Copy(writer, email, "address");
        writer.WriteEndObject();
    }
}
