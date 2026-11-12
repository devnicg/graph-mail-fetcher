# graph-mail-fetcher

.NET 10 console application that exports mail from Exchange Online mailboxes to `.eml` files through
Microsoft Graph, authenticating as an application with a client certificate.

## Usage

```
graph-mail-fetcher --client-id <guid> --tenant-id <guid|domain> --thumbprint <hex> --output <folder>
                   [--filter "<expression>"] [--lookback <period>] [--mailbox <address>[,<address>...]]
```

| Option | Required | Description |
| --- | --- | --- |
| `--client-id` | yes | Application (client) id of the Entra ID app registration. |
| `--tenant-id` | yes | Directory (tenant) id or tenant domain. |
| `--thumbprint` | yes | Thumbprint of the client certificate in the current user's certificate store (`CurrentUser\My`). It must have a private key. |
| `--output` | yes | Destination folder: a local path or a UNC path (`\\server\share\mail`). Created if missing. |
| `--filter` | no | PowerShell-like filter, see below. |
| `--lookback` | no | How far back to go, today included: `<n>d`, `<n>w`, `<n>mo`, `<n>y`. Default `2d` (today and yesterday). |
| `--mailbox` | no | Mailbox(es) to export (UPN, address or object id); repeat or comma-separate. Default: every mailbox in the tenant. |

Options can also be written PowerShell-style (`-ClientId`, `-TenantId`, `-Thumbprint`, `-Output`, `-Filter`,
`-Lookback`, `-Mailbox`) or as `--name=value`.

```powershell
graph-mail-fetcher --client-id 00000000-0000-0000-0000-000000000000 --tenant-id contoso.onmicrosoft.com `
  --thumbprint 1A2B3C... --output \\fileserver\archive\mail --lookback 1mo `
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

Mails are written to `<output>\<mailbox>\<yyyyMMdd-HHmmss>_<subject>_<key>.eml` (received time in UTC). The key
is derived from the message's immutable Graph id; a message whose key is already present in the mailbox folder
is not downloaded again, so the tool can be scheduled repeatedly against the same destination. Renaming the
date/subject part of a file is fine as long as the `_<key>.eml` ending stays.

All folders of the mailbox are searched (including Sent Items and Deleted Items); drafts are skipped.

## App registration

Application permissions (admin consent required):

- `Mail.Read`
- `User.Read.All` — only when `--mailbox` is omitted, to enumerate the tenant's mailboxes

Upload the certificate's public key (`.cer`) under *Certificates & secrets* of the app registration.

## Build

```
dotnet build
dotnet test
dotnet publish src/GraphMailFetcher -c Release -r win-x64 --self-contained false -o publish
```
