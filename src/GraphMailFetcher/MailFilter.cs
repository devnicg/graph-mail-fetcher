using System.Text;
using System.Text.RegularExpressions;

namespace GraphMailFetcher;

/// <summary>
/// A PowerShell-like filter expression over subject, body, sender and recipient, for example
/// <c>subject -like '*invoice*' -and (sender -eq 'a@b.com' -or -not body -match 'draft')</c>.
/// </summary>
internal sealed class MailFilter
{
    private readonly Func<MailMessage, bool> _predicate;

    private MailFilter(Func<MailMessage, bool> predicate, bool usesBody)
    {
        _predicate = predicate;
        UsesBody = usesBody;
    }

    /// <summary>True when the expression looks at the body, so the body has to be downloaded to evaluate it.</summary>
    public bool UsesBody { get; }

    public bool IsMatch(MailMessage message) => _predicate(message);

    public static MailFilter Parse(string expression)
    {
        var parser = new Parser(Tokenize(expression));
        var predicate = parser.ParseExpression();
        return new MailFilter(predicate, parser.UsesBody);
    }

    private enum TokenKind { Word, String, Open, Close, Not, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { tokens.Add(new(TokenKind.Open, "(", i++)); continue; }
            if (c == ')') { tokens.Add(new(TokenKind.Close, ")", i++)); continue; }
            if (c == '!') { tokens.Add(new(TokenKind.Not, "!", i++)); continue; }

            var start = i;
            if (c is '\'' or '"')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= text.Length)
                        throw new FormatException($"Filter: unterminated string starting at position {start + 1}.");
                    if (text[i] == c)
                    {
                        if (i + 1 < text.Length && text[i + 1] == c) { sb.Append(c); i += 2; continue; } // doubled quote
                        i++;
                        break;
                    }
                    sb.Append(text[i++]);
                }
                tokens.Add(new(TokenKind.String, sb.ToString(), start));
                continue;
            }

            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')'))
                i++;
            tokens.Add(new(TokenKind.Word, text[start..i], start));
        }
        tokens.Add(new(TokenKind.End, "", text.Length));
        return tokens;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        public bool UsesBody { get; private set; }

        private Token Current => tokens[_index];

        private bool IsKeyword(string keyword) =>
            Current.Kind == TokenKind.Word && Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

        private FormatException Error(string message) =>
            new($"Filter: {message} at position {Current.Position + 1}.");

        public Func<MailMessage, bool> ParseExpression()
        {
            var result = ParseOr();
            if (Current.Kind != TokenKind.End)
                throw Error($"unexpected '{Current.Text}' (expected -and, -or or end of filter)");
            return result;
        }

        private Func<MailMessage, bool> ParseOr()
        {
            var left = ParseAnd();
            while (IsKeyword("-or"))
            {
                _index++;
                var l = left;
                var r = ParseAnd();
                left = m => l(m) || r(m);
            }
            return left;
        }

        private Func<MailMessage, bool> ParseAnd()
        {
            var left = ParseUnary();
            while (IsKeyword("-and"))
            {
                _index++;
                var l = left;
                var r = ParseUnary();
                left = m => l(m) && r(m);
            }
            return left;
        }

        private Func<MailMessage, bool> ParseUnary()
        {
            if (Current.Kind == TokenKind.Not || IsKeyword("-not"))
            {
                _index++;
                var inner = ParseUnary();
                return m => !inner(m);
            }

            if (Current.Kind == TokenKind.Open)
            {
                _index++;
                var inner = ParseOr();
                if (Current.Kind != TokenKind.Close)
                    throw Error("missing ')'");
                _index++;
                return inner;
            }

            return ParseComparison();
        }

        private Func<MailMessage, bool> ParseComparison()
        {
            if (Current.Kind != TokenKind.Word)
                throw Error(Current.Kind == TokenKind.End
                    ? "expected a field (subject, body, sender, recipient)"
                    : $"unexpected '{Current.Text}' (expected subject, body, sender or recipient)");

            Func<MailMessage, IEnumerable<string>> values;
            switch (Current.Text.TrimStart('$').ToLowerInvariant())
            {
                case "subject": values = m => [m.Subject]; break;
                case "body": values = m => [m.Body ?? ""]; UsesBody = true; break;
                case "sender" or "from": values = m => m.Senders; break;
                case "recipient" or "recipients": values = m => m.Recipients; break;
                default: throw Error($"unknown field '{Current.Text}' (expected subject, body, sender or recipient)");
            }
            _index++;

            if (Current.Kind != TokenKind.Word || !Current.Text.StartsWith('-'))
                throw Error("expected an operator such as -eq, -like or -match");
            var op = Current.Text[1..].ToLowerInvariant();
            var caseSensitive = false;
            if (op.Length > 2 && op[0] is 'c' or 'i' && op != "contains")
            {
                caseSensitive = op[0] == 'c';
                op = op[1..];
            }
            var negate = false;
            if (op is "ne") { negate = true; op = "eq"; }
            else if (op is "notlike" or "notmatch" or "notcontains") { negate = true; op = op[3..]; }
            if (op is not ("eq" or "like" or "match" or "contains"))
                throw Error($"unknown operator '{Current.Text}'");
            _index++;

            if (Current.Kind is not (TokenKind.Word or TokenKind.String))
                throw Error("expected a value to compare with");
            var operand = Current.Text;
            _index++;

            Func<string, bool> test;
            if (op is "eq" or "contains")
            {
                // On a single value -contains behaves like -eq; on recipient it tests list membership, as in PowerShell.
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                test = v => v.Equals(operand, comparison);
            }
            else
            {
                var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                Regex regex;
                try
                {
                    regex = op == "like"
                        ? new Regex(WildcardToRegex(operand), options | RegexOptions.Singleline)
                        : new Regex(operand, options);
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"Filter: invalid pattern '{operand}': {ex.Message}");
                }
                test = regex.IsMatch;
            }

            // Multi-valued fields match when any value matches; negated operators require that none does.
            return m => values(m).Any(test) != negate;
        }

        private static string WildcardToRegex(string pattern)
        {
            var sb = new StringBuilder(@"\A");
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '?': sb.Append('.'); break;
                    case '`' when i + 1 < pattern.Length: sb.Append(Regex.Escape(pattern[++i].ToString())); break;
                    case '[':
                        var end = pattern.IndexOf(']', i + 1);
                        if (end > i + 1)
                        {
                            sb.Append('[').Append(pattern[(i + 1)..end].Replace(@"\", @"\\").Replace("^", @"\^").Replace("[", @"\[")).Append(']');
                            i = end;
                        }
                        else
                        {
                            sb.Append(@"\[");
                        }
                        break;
                    default: sb.Append(Regex.Escape(c.ToString())); break;
                }
            }
            return sb.Append(@"\z").ToString();
        }
    }
}
