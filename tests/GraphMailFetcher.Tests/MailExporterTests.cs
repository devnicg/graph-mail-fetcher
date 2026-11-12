using System.Net;
using System.Text;

namespace GraphMailFetcher.Tests;

public sealed class MailExporterTests : IDisposable
{
    private readonly string _output = Directory.CreateTempSubdirectory("graph-mail-fetcher-tests").FullName;
    private readonly FakeGraph _fake = new();

    public void Dispose() => Directory.Delete(_output, recursive: true);

    private MailExporter CreateExporter(string? filter = null, ExportFormat format = ExportFormat.Eml)
    {
        var graph = new GraphClient(new HttpClient(_fake), _ => ValueTask.FromResult("token"))
        {
            Delay = (_, _) => Task.CompletedTask,
        };
        return new MailExporter(graph, _output, filter is null ? null : MailFilter.Parse(filter), TextWriter.Null, format);
    }

    private static readonly DateTimeOffset Since = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportsAcrossPagesAndSkipsDrafts()
    {
        var result = await CreateExporter().ExportMailboxAsync("Jan@Contoso.com", Since, CancellationToken.None);

        Assert.Equal(new ExportResult(Found: 3, Matched: 3, Exported: 3, AlreadyPresent: 0, Failed: 0), result);
        var files = Directory.GetFiles(Path.Combine(_output, "jan@contoso.com")).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(3, files.Length);
        Assert.StartsWith("20261111-083000_Re What Why - ok_", files[0]);
        Assert.StartsWith("20261112-091500_Invoice 41_", files[2]);
        Assert.All(files, f => Assert.EndsWith(".eml", f));
        Assert.Equal("MIME of m1", await File.ReadAllTextAsync(Path.Combine(_output, "jan@contoso.com", files[2]!)));
    }

    [Fact]
    public async Task SecondRunExportsNothingAndOnlyFetchesNewMessages()
    {
        await CreateExporter().ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);
        var downloadsAfterFirstRun = _fake.Downloads;

        var result = await CreateExporter().ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Equal(new ExportResult(Found: 3, Matched: 3, Exported: 0, AlreadyPresent: 3, Failed: 0), result);
        Assert.Equal(downloadsAfterFirstRun, _fake.Downloads);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_output, "jan@contoso.com")).Length);
    }

    [Fact]
    public async Task AppliesTheFilterAndRequestsTheBodyOnlyWhenNeeded()
    {
        var result = await CreateExporter("subject -like '*invoice*' -or recipient -eq 'piet@contoso.com'")
            .ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Equal(new ExportResult(Found: 3, Matched: 2, Exported: 2, AlreadyPresent: 0, Failed: 0), result);
        Assert.DoesNotContain(_fake.Requests, r => r.Contains("body"));

        await CreateExporter("body -like '*hello*'").ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);
        Assert.Contains(_fake.Requests, r => r.Contains(",body"));
    }

    [Fact]
    public async Task SendsDateFilterTokenAndRetriesWhenThrottled()
    {
        _fake.ThrottleNext = 2;

        await CreateExporter().ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Contains(_fake.Requests, r => r.Contains("$filter=receivedDateTime%20ge%202026-11-11T00:00:00Z"));
        Assert.True(_fake.AllAuthorized);
    }

    [Fact]
    public async Task ADownloadFailureIsCountedAndLeavesNoFileBehind()
    {
        _fake.FailDownloadOf = "m2";

        var result = await CreateExporter().ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Equal(new ExportResult(Found: 3, Matched: 3, Exported: 2, AlreadyPresent: 0, Failed: 1), result);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_output, "jan@contoso.com")).Length);
    }

    [Fact]
    public async Task ExportsJsonWithFlattenedAddressesAndAttachments()
    {
        var result = await CreateExporter("subject -like 'Invoice*'", ExportFormat.Json)
            .ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Equal(new ExportResult(Found: 3, Matched: 1, Exported: 1, AlreadyPresent: 0, Failed: 0), result);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_output, "jan@contoso.com")));
        Assert.StartsWith("20261112-091500_Invoice 41_", Path.GetFileName(file));
        Assert.EndsWith(".json", file);

        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("<p>Bonjour & héllo</p>", text); // readable, not \u-escaped
        Assert.DoesNotContain("@odata", text);
        using var json = System.Text.Json.JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal("m1", root.GetProperty("id").GetString());
        Assert.Equal("<abc@fabrikam.com>", root.GetProperty("internetMessageId").GetString());
        Assert.Equal("2026-11-12T09:15:00Z", root.GetProperty("receivedDateTime").GetString());
        Assert.Equal("billing@fabrikam.com", root.GetProperty("from").GetProperty("address").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("sender").ValueKind);
        Assert.Equal("Jan", root.GetProperty("toRecipients")[0].GetProperty("name").GetString());
        Assert.Equal(0, root.GetProperty("ccRecipients").GetArrayLength());
        Assert.Equal("html", root.GetProperty("body").GetProperty("contentType").GetString());
        var attachment = root.GetProperty("attachments")[0];
        Assert.Equal("file", attachment.GetProperty("type").GetString());
        Assert.Equal("invoice.pdf", attachment.GetProperty("name").GetString());
        Assert.Equal("JVBERi0=", attachment.GetProperty("contentBytes").GetString());
    }

    [Fact]
    public async Task EmlAndJsonExportsAreTrackedSeparately()
    {
        await CreateExporter().ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        var first = await CreateExporter(format: ExportFormat.Json).ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);
        var second = await CreateExporter(format: ExportFormat.Json).ExportMailboxAsync("jan@contoso.com", Since, CancellationToken.None);

        Assert.Equal(3, first.Exported);
        Assert.Equal(new ExportResult(Found: 3, Matched: 3, Exported: 0, AlreadyPresent: 3, Failed: 0), second);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(_output, "jan@contoso.com")).Length);
    }

    [Fact]
    public void SanitizesFileNames()
    {
        Assert.Equal("a b c", MailExporter.SanitizeFileName("a/b\\c", 60));
        Assert.Equal("no subject", MailExporter.SanitizeFileName(" ?*.. ", 60));
        Assert.Equal("abcde", MailExporter.SanitizeFileName("abcde. fghij", 6));
    }

    private sealed class FakeGraph : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public int Downloads { get; private set; }
        public int ThrottleNext { get; set; }
        public string? FailDownloadOf { get; set; }
        public bool AllAuthorized { get; private set; } = true;

        private const string Page1 = """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/users/jan%40contoso.com/messages?$skip=2",
              "value": [
                { "id": "m1", "subject": "Invoice 41", "receivedDateTime": "2026-11-12T09:15:00Z", "isDraft": false,
                  "body": { "contentType": "text", "content": "hello" },
                  "from": { "emailAddress": { "name": "Billing", "address": "billing@fabrikam.com" } },
                  "toRecipients": [ { "emailAddress": { "name": "Jan", "address": "jan@contoso.com" } } ] },
                { "id": "m2", "subject": "Lunch?", "receivedDateTime": "2026-11-12T08:00:00Z", "isDraft": false,
                  "from": { "emailAddress": { "name": "An", "address": "an@contoso.com" } },
                  "toRecipients": [ { "emailAddress": { "name": "Jan", "address": "jan@contoso.com" } } ] },
                { "id": "d1", "subject": "Unfinished", "receivedDateTime": "2026-11-12T07:00:00Z", "isDraft": true }
              ]
            }
            """;

        private const string Detail = """
            {
              "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('jan')/messages/$entity",
              "@odata.etag": "W/\"x\"",
              "id": "m1", "internetMessageId": "<abc@fabrikam.com>", "conversationId": "c1", "subject": "Invoice 41",
              "receivedDateTime": "2026-11-12T09:15:00Z", "sentDateTime": "2026-11-12T09:14:58Z",
              "importance": "normal", "isRead": true, "hasAttachments": true, "bodyPreview": "Bonjour",
              "body": { "contentType": "html", "content": "<p>Bonjour & héllo</p>" },
              "from": { "emailAddress": { "name": "Billing", "address": "billing@fabrikam.com" } },
              "toRecipients": [ { "emailAddress": { "name": "Jan", "address": "jan@contoso.com" } } ],
              "ccRecipients": [], "bccRecipients": [], "replyTo": [],
              "attachments": [
                { "@odata.type": "#microsoft.graph.fileAttachment", "@odata.mediaContentType": "application/pdf", "id": "a1",
                  "name": "invoice.pdf", "contentType": "application/pdf", "size": 5, "isInline": false, "contentId": null,
                  "contentBytes": "JVBERi0=" }
              ]
            }
            """;

        private const string Page2 = """
            {
              "value": [
                { "id": "m3", "subject": "Re: What? / Why_- ok", "receivedDateTime": "2026-11-11T08:30:00Z", "isDraft": false,
                  "from": { "emailAddress": { "name": "Jan", "address": "jan@contoso.com" } },
                  "toRecipients": [ { "emailAddress": { "name": "An", "address": "an@contoso.com" } } ],
                  "ccRecipients": [ { "emailAddress": { "name": "Piet", "address": "piet@contoso.com" } } ] }
              ]
            }
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.OriginalString;
            Requests.Add(url);
            AllAuthorized &= request.Headers.Authorization is { Scheme: "Bearer", Parameter: "token" };

            if (ThrottleNext > 0)
            {
                ThrottleNext--;
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.TryAddWithoutValidation("Retry-After", "1");
                return Task.FromResult(throttled);
            }

            if (url.EndsWith("/$value"))
            {
                var id = url.Split('/')[^2];
                if (id == FailDownloadOf)
                    return Task.FromResult(Json(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound","message":"gone"}}"""));
                Downloads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"MIME of {id}") });
            }

            if (url.Contains("$expand=attachments"))
                return Task.FromResult(Json(HttpStatusCode.OK, Detail));

            return Task.FromResult(Json(HttpStatusCode.OK, url.Contains("$skip") ? Page2 : Page1));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
