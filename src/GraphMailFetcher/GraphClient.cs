using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GraphMailFetcher;

internal sealed class GraphException(HttpStatusCode status, string code, string message)
    : Exception($"Graph returned {(int)status} {code}: {message}")
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Minimal Microsoft Graph client for listing mailboxes and messages and downloading MIME content.</summary>
internal sealed class GraphClient(HttpClient http, Func<CancellationToken, ValueTask<string>> getToken)
{
    public const string DefaultBaseUrl = "https://graph.microsoft.com/v1.0";
    private const int MaxAttempts = 6;

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>Lets tests skip the back-off delays.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Lists the messages in all folders of a mailbox received at or after <paramref name="since"/>.</summary>
    public async IAsyncEnumerable<MailMessage> ListMessagesAsync(
        string mailbox, DateTimeOffset since, bool includeBody, [EnumeratorCancellation] CancellationToken ct)
    {
        var select = "id,subject,receivedDateTime,isDraft,from,sender,toRecipients,ccRecipients,bccRecipients" + (includeBody ? ",body" : "");
        var url = $"{BaseUrl}/users/{Uri.EscapeDataString(mailbox)}/messages"
            + $"?$filter=receivedDateTime%20ge%20{since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"
            + "&$orderby=receivedDateTime%20desc"
            + $"&$select={select}&$top={(includeBody ? 50 : 200)}";

        // Immutable ids survive moves between folders, which keeps the duplicate check stable across runs.
        const string prefer = "IdType=\"ImmutableId\", outlook.body-content-type=\"text\"";

        await foreach (var item in GetPagedAsync(url, prefer, ct))
        {
            var senders = new List<string>();
            AddAddress(item, "from", senders);
            AddAddress(item, "sender", senders);

            var recipients = new List<string>();
            foreach (var property in (ReadOnlySpan<string>)["toRecipients", "ccRecipients", "bccRecipients"])
            {
                if (item.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var recipient in list.EnumerateArray())
                        AddEmailAddress(recipient, recipients);
            }

            string? body = null;
            if (item.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.Object)
                body = GetString(bodyElement, "content");

            yield return new MailMessage(
                item.GetProperty("id").GetString()!,
                GetString(item, "subject") ?? "",
                body,
                senders,
                recipients,
                item.TryGetProperty("receivedDateTime", out var received) && received.ValueKind == JsonValueKind.String
                    ? received.GetDateTimeOffset()
                    : DateTimeOffset.MinValue,
                item.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True);
        }
    }

    /// <summary>Downloads the MIME (.eml) content of a message into <paramref name="destination"/>.</summary>
    public async Task DownloadMimeAsync(string mailbox, string messageId, Stream destination, CancellationToken ct)
    {
        var url = $"{BaseUrl}/users/{Uri.EscapeDataString(mailbox)}/messages/{Uri.EscapeDataString(messageId)}/$value";
        using var response = await SendAsync(url, "IdType=\"ImmutableId\"", ct);
        await using var content = await response.Content.ReadAsStreamAsync(ct);
        await content.CopyToAsync(destination, ct);
    }

    /// <summary>Downloads a message with its attachments and writes it as JSON into <paramref name="destination"/>.</summary>
    public async Task DownloadJsonAsync(string mailbox, string messageId, Stream destination, CancellationToken ct)
    {
        var url = $"{BaseUrl}/users/{Uri.EscapeDataString(mailbox)}/messages/{Uri.EscapeDataString(messageId)}"
            + "?$select=id,internetMessageId,conversationId,subject,receivedDateTime,sentDateTime,from,sender,toRecipients,"
            + "ccRecipients,bccRecipients,replyTo,importance,isRead,hasAttachments,bodyPreview,body&$expand=attachments";
        using var response = await SendAsync(url, "IdType=\"ImmutableId\"", ct);
        await using var content = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: ct);
        await MessageJson.WriteAsync(document.RootElement, destination, ct);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AddAddress(JsonElement message, string property, List<string> target)
    {
        if (message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object)
            AddEmailAddress(value, target);
    }

    /// <summary>Adds both the address and the display name, so filters can match on either.</summary>
    private static void AddEmailAddress(JsonElement recipient, List<string> target)
    {
        if (!recipient.TryGetProperty("emailAddress", out var email) || email.ValueKind != JsonValueKind.Object)
            return;
        foreach (var property in (ReadOnlySpan<string>)["address", "name"])
        {
            var value = GetString(email, property);
            if (!string.IsNullOrEmpty(value) && !target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }
    }

    private async IAsyncEnumerable<JsonElement> GetPagedAsync(string url, string? prefer, [EnumeratorCancellation] CancellationToken ct)
    {
        string? next = url;
        while (next is not null)
        {
            using var response = await SendAsync(next, prefer, ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    yield return item;

            next = GetString(document.RootElement, "@odata.nextLink");
        }
    }

    /// <summary>Sends a GET request, retrying throttled and transient failures, and throws on any other error.</summary>
    private async Task<HttpResponseMessage> SendAsync(string url, string? prefer, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await getToken(ct));
            if (prefer is not null)
                request.Headers.TryAddWithoutValidation("Prefer", prefer);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                continue;
            }

            if (response.IsSuccessStatusCode)
                return response;

            using (response)
            {
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                if (transient && attempt < MaxAttempts)
                {
                    var wait = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                        ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                var (code, message) = await ReadErrorAsync(response, ct);
                throw new GraphException(response.StatusCode, code, message);
            }
        }
    }

    private static async Task<(string Code, string Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return (GetString(error, "code") ?? "", GetString(error, "message") ?? "");
        }
        catch (JsonException)
        {
        }
        return (response.ReasonPhrase ?? "", text.Length > 300 ? text[..300] : text);
    }
}
