using Azure.Core;
using Azure.Identity;
using GraphMailFetcher;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await RunAsync(args, cancellation.Token);
}
catch (UsageException ex)
{
    if (ex.Message.Length > 0)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine();
    }
    Console.Error.WriteLine(Options.Usage);
    return 2;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex) when (ex is FormatException or InvalidOperationException or AuthenticationFailedException
    or GraphException or HttpRequestException or IOException or UnauthorizedAccessException)
{
    // Azure.Identity puts the actual reason (unknown tenant, certificate not registered, ...) in the inner exception.
    var detail = ex is AuthenticationFailedException && ex.InnerException is { } inner ? " " + inner.Message : "";
    Console.Error.WriteLine($"Error: {ex.Message.Trim()}{detail}");
    return 1;
}

static async Task<int> RunAsync(string[] args, CancellationToken ct)
{
    var options = Options.Parse(args);
    var filter = options.Filter is null ? null : MailFilter.Parse(options.Filter);
    var since = Lookback.GetStartUtc(options.Lookback, DateTimeOffset.Now, TimeZoneInfo.Local);

    var outputFolder = Path.GetFullPath(options.OutputFolder);
    Directory.CreateDirectory(outputFolder);

    using var certificate = CertificateLoader.FromUserStore(options.Thumbprint);
    var credential = new ClientCertificateCredential(options.TenantId, options.ClientId, certificate);
    var tokenRequest = new TokenRequestContext(["https://graph.microsoft.com/.default"]);

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    // The credential caches the token and renews it shortly before it expires.
    var graph = new GraphClient(http, async token => (await credential.GetTokenAsync(tokenRequest, token)).Token);

    Console.WriteLine($"Exporting mail received since {since.ToLocalTime():yyyy-MM-dd HH:mm} to {outputFolder} as {options.Format.ToString().ToLowerInvariant()}");

    var exporter = new MailExporter(graph, outputFolder, filter, Console.Out, options.Format);
    int exported = 0, alreadyPresent = 0, failed = 0, failedMailboxes = 0;

    foreach (var mailbox in options.Mailboxes)
    {
        Console.WriteLine($"{mailbox}:");
        try
        {
            var result = await exporter.ExportMailboxAsync(mailbox, since, ct);
            Console.WriteLine($"  {result.Found} in period, {result.Matched} matched, {result.Exported} exported, "
                + $"{result.AlreadyPresent} already present, {result.Failed} failed");
            exported += result.Exported;
            alreadyPresent += result.AlreadyPresent;
            failed += result.Failed;
        }
        catch (Exception ex) when (ex is GraphException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            failedMailboxes++;
            Console.Error.WriteLine($"  Error: {ex.Message}");
        }
    }

    Console.WriteLine($"Done: {exported} exported, {alreadyPresent} already present, {failed} failed"
        + (failedMailboxes > 0 ? $", {failedMailboxes} mailbox(es) could not be read" : "") + ".");
    return failed > 0 || failedMailboxes > 0 ? 1 : 0;
}
