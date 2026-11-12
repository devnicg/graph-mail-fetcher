namespace GraphMailFetcher.Tests;

public class MailFilterTests
{
    private static readonly MailMessage Message = new(
        "id-1",
        "Invoice 2026-041 for Contoso",
        "Dear customer,\nplease find order 12345 attached.",
        ["billing@fabrikam.com", "Fabrikam Billing"],
        ["jan@contoso.com", "Jan Peeters", "finance@contoso.com"],
        DateTimeOffset.UtcNow,
        false);

    [Theory]
    [InlineData("subject -like '*invoice*'", true)]
    [InlineData("subject -like 'invoice'", false)]
    [InlineData("subject -clike '*invoice*'", false)]
    [InlineData("subject -notlike '*invoice*'", false)]
    [InlineData("subject -eq 'INVOICE 2026-041 FOR CONTOSO'", true)]
    [InlineData("subject -ceq 'INVOICE 2026-041 FOR CONTOSO'", false)]
    [InlineData("subject -ne 'hello'", true)]
    [InlineData("subject -like 'Invoice 2026-0[0-9]? *'", true)]
    [InlineData("subject -match '^invoice \\d{4}-\\d+'", true)]
    [InlineData("subject -notmatch 'contoso$'", false)]
    [InlineData("body -like '*order 12345*'", true)]
    [InlineData("body -match 'customer,\\s+please'", true)]
    [InlineData("sender -eq 'billing@fabrikam.com'", true)]
    [InlineData("from -like '*billing'", true)]
    [InlineData("sender -eq jan@contoso.com", false)]
    [InlineData("recipient -eq 'finance@contoso.com'", true)]
    [InlineData("recipient -contains 'JAN@contoso.com'", true)]
    [InlineData("recipient -contains 'contoso.com'", false)]
    [InlineData("recipient -notcontains 'piet@contoso.com'", true)]
    [InlineData("recipient -like '*@contoso.com'", true)]
    [InlineData("recipient -notlike '*@contoso.com'", false)]
    [InlineData("recipient -ne 'jan@contoso.com'", false)]
    [InlineData("subject -like '*invoice*' -and sender -like '*@fabrikam.com'", true)]
    [InlineData("subject -like '*offer*' -or sender -like '*@fabrikam.com'", true)]
    [InlineData("subject -like '*offer*' -or sender -like '*@fabrikam.com' -and body -like '*nothing*'", false)]
    [InlineData("(subject -like '*offer*' -or sender -like '*@fabrikam.com') -and -not body -like '*nothing*'", true)]
    [InlineData("!(subject -like '*invoice*')", false)]
    [InlineData("-not subject -like 'RE:*' -and $subject -Like \"*contoso\"", true)]
    [InlineData("subject -like '*it''s*'", false)]
    public void Evaluates(string expression, bool expected) =>
        Assert.Equal(expected, MailFilter.Parse(expression).IsMatch(Message));

    [Fact]
    public void ReportsWhetherTheBodyIsNeeded()
    {
        Assert.False(MailFilter.Parse("subject -like 'a*' -and sender -eq 'b'").UsesBody);
        Assert.True(MailFilter.Parse("subject -like 'a*' -or body -match 'b'").UsesBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("subject")]
    [InlineData("subject -like")]
    [InlineData("subject -foo 'x'")]
    [InlineData("attachment -eq 'x'")]
    [InlineData("subject -eq 'x' sender -eq 'y'")]
    [InlineData("(subject -eq 'x'")]
    [InlineData("subject -eq 'x")]
    [InlineData("subject -match '('")]
    [InlineData("subject -eq 'x' -and")]
    public void RejectsInvalidExpressions(string expression) =>
        Assert.Throws<FormatException>(() => MailFilter.Parse(expression));
}
