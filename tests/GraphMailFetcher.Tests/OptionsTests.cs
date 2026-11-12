namespace GraphMailFetcher.Tests;

public class OptionsTests
{
    [Fact]
    public void ParsesAllOptions()
    {
        var options = Options.Parse([
            "--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "AB CD", "--output", @"\\server\share\mail",
            "--filter", "subject -like '*a=b*' -and sender -eq 'x'", "--lookback", "1mo", "--mailbox", "a@x.com, b@x.com", "--mailbox", "c@x.com"]);

        Assert.Equal("cid", options.ClientId);
        Assert.Equal("tid", options.TenantId);
        Assert.Equal("AB CD", options.Thumbprint);
        Assert.Equal(@"\\server\share\mail", options.OutputFolder);
        Assert.Equal("subject -like '*a=b*' -and sender -eq 'x'", options.Filter);
        Assert.Equal("1mo", options.Lookback);
        Assert.Equal(["a@x.com", "b@x.com", "c@x.com"], options.Mailboxes);
    }

    [Fact]
    public void AcceptsPowerShellStyleNamesAndAppliesDefaults()
    {
        var options = Options.Parse(["-ClientId", "cid", "-TenantId:tid", "-Thumbprint=abc", @"--output=C:\mail"]);

        Assert.Equal("tid", options.TenantId);
        Assert.Equal("abc", options.Thumbprint);
        Assert.Equal(@"C:\mail", options.OutputFolder);
        Assert.Null(options.Filter);
        Assert.Equal("2d", options.Lookback);
        Assert.Empty(options.Mailboxes);
    }

    [Theory]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--bogus", "1")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "stray")]
    [InlineData("--help")]
    public void RejectsInvalidCommandLines(params string[] args) =>
        Assert.Throws<UsageException>(() => Options.Parse(args));
}
