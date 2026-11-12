# graph-mail-fetcher

.NET 10 console application that exports mail from Exchange Online mailboxes to `.eml` files through
Microsoft Graph, authenticating as an application with a client certificate.

## Usage

```
graph-mail-fetcher --client-id <guid> --tenant-id <guid|domain> --thumbprint <hex> --output <folder>
                   --mailbox <upn|object-id>[,...] [--filter "<expression>"] [--lookback <period>]
                   [--format eml|json]
```

| Option | Required | Description |
| --- | --- | --- |
| `--client-id` | yes | Application (client) id of the Entra ID app registration. |
| `--tenant-id` | yes | Directory (tenant) id or tenant domain. |
| `--thumbprint` | yes | Thumbprint of the client certificate in the current user's certificate store (`CurrentUser\My`). It must have a private key. |
| `--output` | yes | Destination folder: a local path or a UNC path (`\\server\share\mail`). Created if missing. |
| `--mailbox` | yes | Mailbox to export: user principal name (`jan@contoso.com`) or object id (GUID). Repeat or comma-separate for several mailboxes. |
| `--filter` | no | PowerShell-like filter, see below. |
| `--lookback` | no | How far back to go, today included: `<n>d`, `<n>w`, `<n>mo`, `<n>y`. Default `2d` (today and yesterday). |
| `--format` | no | `eml` (default) or `json`, see below. |

Options can also be written PowerShell-style (`-ClientId`, `-TenantId`, `-Thumbprint`, `-Output`, `-Mailbox`,
`-Filter`, `-Lookback`, `-Format`) or as `--name=value`.

```powershell
graph-mail-fetcher --client-id 00000000-0000-0000-0000-000000000000 --tenant-id contoso.onmicrosoft.com `
  --thumbprint 1A2B3C... --output \\fileserver\archive\mail --mailbox jan@contoso.com --lookback 1mo `
  --filter "subject -like '*invoice*' -and sender -like '*@fabrikam.com'"
```

Exit code: `0` success, `1` one or more errors, `2` invalid command line.

## Filter

Fields: `subject`, `body`, `sender`, `recipient`.

| Operator | Meaning |
| --- | --- |
| `-eq`, `-ne` | Equals / does not equal. |
| `-like`, `-notlike` | Wildcard match on the whole value (`*`, `?`, `[a-z]`). |
| `-match`, `-notmatch` | .NET regular expression. |
| `-contains`, `-notcontains` | Value is (not) one of the entries, e.g. `recipient -contains 'jan@contoso.com'`. Use `-like '*text*'` for substrings. |
| `-and`, `-or`, `-not` / `!`, `( )` | Combine conditions; `-and` binds tighter than `-or`. |

- Comparisons are case-insensitive; prefix with `c` for case-sensitive (`-clike`, `-cmatch`, `-ceq`).
- Values are quoted with `'...'` or `"..."` (double the quote to embed it); values without spaces need no quotes.
- `sender` matches the address or display name of From/Sender; `recipient` matches the address or display name
  of any To, Cc or Bcc recipient. Negated operators are true when no entry matches.
- `body` is compared as plain text.

## Look-back period

The period is counted in local calendar days and always includes today: `1d` is today only, `2d` today and
yesterday, `7d`/`1w` the last seven days, `1mo` and `1y` reach back to the day after the same date last
month/year.

## Output and duplicates

Mails are written to `<output>\<mailbox>\<yyyyMMdd-HHmmss>_<subject>_<key>.eml` (or `.json`; received time in
UTC). The key is derived from the message's immutable Graph id; a message whose key is already present in the
mailbox folder is not downloaded again, so the tool can be scheduled repeatedly against the same destination.
Renaming the date/subject part of a file is fine as long as the `_<key>.eml` / `_<key>.json` ending stays.
The two formats are tracked separately, so an existing `.eml` does not stop the `.json` of the same mail from
being exported.

All folders of the mailbox are searched (including Sent Items and Deleted Items); drafts are skipped.

## JSON format

`--format json` writes one UTF-8 JSON file per mail, readable with any JSON parser
(`Get-Content file.json -Raw | ConvertFrom-Json`). Every file has the same properties; missing values are `null`.

```json
{
  "id": "AAkALg...",
  "internetMessageId": "<abc@fabrikam.com>",
  "conversationId": "AAQkAD...",
  "subject": "Invoice 41",
  "receivedDateTime": "2026-11-12T09:15:00Z",
  "sentDateTime": "2026-11-12T09:14:58Z",
  "from": { "name": "Billing", "address": "billing@fabrikam.com" },
  "sender": { "name": "Billing", "address": "billing@fabrikam.com" },
  "toRecipients": [ { "name": "Jan", "address": "jan@contoso.com" } ],
  "ccRecipients": [],
  "bccRecipients": [],
  "replyTo": [],
  "importance": "normal",
  "isRead": true,
  "hasAttachments": true,
  "bodyPreview": "Please find the invoice attached...",
  "body": { "contentType": "html", "content": "<html>...</html>" },
  "attachments": [
    { "type": "file", "name": "invoice.pdf", "contentType": "application/pdf", "size": 48213,
      "isInline": false, "contentId": null, "contentBytes": "JVBERi0xLjcK..." }
  ]
}
```

- Dates are UTC in ISO 8601. `body.contentType` is `html` or `text`, as the mail was stored; `bodyPreview` is
  the first 255 characters as plain text.
- `attachments[].type` is `file` (content in `contentBytes`, base64), `item` (an attached mail, event or
  contact) or `reference` (a link to a cloud file). Only `file` attachments carry `contentBytes`; for the
  others it is `null`.

## App registration

Application permissions (admin consent required):

- `Mail.Read`

Upload the certificate's public key (`.cer`) under *Certificates & secrets* of the app registration.

## Releases

The version lives in `<Version>` in `src/GraphMailFetcher/GraphMailFetcher.csproj`. When a commit on `main` carries a
version that has no `v<version>` tag yet, the *Release* workflow runs the tests and publishes a GitHub release
with self-contained single-file builds for `win-x64` and `linux-x64` (no .NET installation needed).

## Build

```
dotnet build
dotnet test
dotnet publish src/GraphMailFetcher -c Release -r win-x64 --self-contained false -o publish
```
