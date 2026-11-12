namespace GraphMailFetcher.Tests;

public class OptionsTests
{
    [Fact]
    public void ParsesAllOptions()
    {
        var options = Options.Parse([
            "--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "AB CD", "--output", @"\\server\share\mail",
            "--filter", "subject -like '*a=b*' -and sender -eq 'x'", "--lookback", "1mo", "--mailbox", "a@x.com, b@x.com", "--mailbox", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "--format", "JSON"]);

        Assert.Equal("cid", options.ClientId);
        Assert.Equal("tid", options.TenantId);
        Assert.Equal("AB CD", options.Thumbprint);
        Assert.Equal(@"\\server\share\mail", options.OutputFolder);
        Assert.Equal("subject -like '*a=b*' -and sender -eq 'x'", options.Filter);
        Assert.Equal("1mo", options.Lookback);
        Assert.Equal(["a@x.com", "b@x.com", "3f2504e0-4f89-11d3-9a0c-0305e82c3301"], options.Mailboxes);
        Assert.Equal(ExportFormat.Json, options.Format);
    }

    [Fact]
    public void AcceptsPowerShellStyleNamesAndAppliesDefaults()
    {
        var options = Options.Parse(["-ClientId", "cid", "-TenantId:tid", "-Thumbprint=abc", @"--output=C:\mail", "-Mailbox", "a@x.com"]);

        Assert.Equal("tid", options.TenantId);
        Assert.Equal("abc", options.Thumbprint);
        Assert.Equal(@"C:\mail", options.OutputFolder);
        Assert.Null(options.Filter);
        Assert.Equal("2d", options.Lookback);
        Assert.Equal(["a@x.com"], options.Mailboxes);
        Assert.Equal(ExportFormat.Eml, options.Format);
    }

    [Theory]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--mailbox", "a@x.com")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--mailbox", "a@x.com", "--output")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--mailbox", "a@x.com", "--bogus", "1")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--mailbox", "a@x.com", "stray")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--mailbox", "jan")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--mailbox", "a@x.com,@x.com")]
    [InlineData("--client-id", "cid", "--tenant-id", "tid", "--thumbprint", "abc", "--output", "o", "--mailbox", "a@x.com", "--format", "pst")]
    [InlineData("--help")]
    public void RejectsInvalidCommandLines(params string[] args) =>
        Assert.Throws<UsageException>(() => Options.Parse(args));
}
