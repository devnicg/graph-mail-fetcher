namespace GraphMailFetcher;

internal sealed class UsageException(string message) : Exception(message);

internal enum ExportFormat { Eml, Json }

internal sealed record Options(
    string ClientId,
    string TenantId,
    string Thumbprint,
    string OutputFolder,
    string? Filter,
    string Lookback,
    IReadOnlyList<string> Mailboxes,
    ExportFormat Format)
{
    public const string DefaultLookback = "2d";

    public const string Usage = """
        Usage:
          graph-mail-fetcher --client-id <guid> --tenant-id <guid|domain> --thumbprint <hex> --output <folder>
                             --mailbox <upn|object-id>[,...] [--filter "<expression>"] [--lookback <period>]
                             [--format eml|json]

        Required:
          --client-id    Application (client) id of the Entra ID app registration.
          --tenant-id    Directory (tenant) id or tenant domain name.
          --thumbprint   Thumbprint of the client certificate in the current user's certificate store
                         (CurrentUser\My). The certificate must have a private key.
          --output       Folder the mails are exported to. Local path or UNC path (\\server\share\folder).
          --mailbox      Mailbox to export: user principal name (jan@contoso.com) or object id (GUID).
                         Repeat the option or separate with commas for several mailboxes.

        Optional:
          --filter       PowerShell-like filter on subject, body, sender and recipient, e.g.
                           "subject -like '*invoice*' -and sender -eq 'billing@contoso.com'"
                           "(recipient -like '*@contoso.com' -or body -match 'order \d+') -and -not subject -like 'RE:*'"
                         Operators: -eq -ne -like -notlike -match -notmatch -contains -notcontains,
                         combined with -and, -or, -not (or !) and parentheses. Case-insensitive;
                         prefix an operator with c for case-sensitive (-clike, -cmatch, ...).
          --lookback     How far back to go, today included: <n>d, <n>w, <n>mo or <n>y (7d, 2w, 1mo, 1y).
                         Default: 2d (today and yesterday).
          --format       eml (default): the original message as a MIME .eml file.
                         json: a .json file with headers, body and attachments (base64) per mail.

        Options may also be written PowerShell-style (-ClientId, -TenantId, -Thumbprint, -Output,
        -Mailbox, -Filter, -Lookback, -Format) or as --name=value.
        """;

    public static Options Parse(string[] args)
    {
        string? clientId = null, tenantId = null, thumbprint = null, output = null, filter = null, lookback = null, format = null;
        var mailboxes = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-'))
                throw new UsageException($"Unexpected argument '{arg}'.");

            string name = arg;
            string? value = null;
            var separator = arg.IndexOfAny(['=', ':']);
            if (separator > 0)
            {
                name = arg[..separator];
                value = arg[(separator + 1)..];
            }

            var key = name.TrimStart('-').Replace("-", "").ToLowerInvariant();
            if (key is "help" or "h" or "?")
                throw new UsageException("");

            if (value is null)
            {
                if (i + 1 >= args.Length)
                    throw new UsageException($"Missing value for '{name}'.");
                value = args[++i];
            }

            switch (key)
            {
                case "clientid": clientId = value; break;
                case "tenantid": tenantId = value; break;
                case "thumbprint" or "certificatethumbprint" or "clientcertificatethumbprint": thumbprint = value; break;
                case "output" or "outputfolder" or "outputpath": output = value; break;
                case "filter": filter = value; break;
                case "lookback": lookback = value; break;
                case "format": format = value; break;
                case "mailbox" or "mailboxes":
                    mailboxes.AddRange(value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                default:
                    throw new UsageException($"Unknown option '{name}'.");
            }
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(clientId)) missing.Add("--client-id");
        if (string.IsNullOrWhiteSpace(tenantId)) missing.Add("--tenant-id");
        if (string.IsNullOrWhiteSpace(thumbprint)) missing.Add("--thumbprint");
        if (string.IsNullOrWhiteSpace(output)) missing.Add("--output");
        if (mailboxes.Count == 0) missing.Add("--mailbox");
        if (missing.Count > 0)
            throw new UsageException($"Missing required option(s): {string.Join(", ", missing)}.");

        foreach (var mailbox in mailboxes)
        {
            var isUpn = mailbox.IndexOf('@') is > 0 and var at && at < mailbox.Length - 1 && !mailbox.Any(char.IsWhiteSpace);
            if (!isUpn && !Guid.TryParse(mailbox, out _))
                throw new UsageException($"Invalid mailbox '{mailbox}'. Use a user principal name (jan@contoso.com) or an object id (GUID).");
        }

        var exportFormat = (format ?? "eml").Trim().ToLowerInvariant() switch
        {
            "eml" => ExportFormat.Eml,
            "json" => ExportFormat.Json,
            _ => throw new UsageException($"Invalid format '{format}'. Use eml or json."),
        };

        return new Options(
            clientId!.Trim(),
            tenantId!.Trim(),
            thumbprint!,
            output!,
            string.IsNullOrWhiteSpace(filter) ? null : filter,
            string.IsNullOrWhiteSpace(lookback) ? DefaultLookback : lookback.Trim(),
            mailboxes,
            exportFormat);
    }
}
