using System.Security.Cryptography.X509Certificates;

namespace GraphMailFetcher;

internal static class CertificateLoader
{
    /// <summary>Finds a certificate with a private key by thumbprint in the current user's personal store.</summary>
    public static X509Certificate2 FromUserStore(string thumbprint)
    {
        // Keep hex digits only: thumbprints copied from the Windows certificate dialog carry spaces and invisible characters.
        var normalized = new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length == 0)
            throw new UsageException($"'{thumbprint}' is not a valid certificate thumbprint.");

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);

        if (matches.Count == 0)
            throw new InvalidOperationException($"No certificate with thumbprint {normalized} was found in the current user's certificate store (CurrentUser\\My).");

        var certificate = matches.FirstOrDefault(c => c.HasPrivateKey)
            ?? throw new InvalidOperationException($"Certificate {normalized} was found but has no private key, so it cannot be used to authenticate.");

        if (DateTime.Now > certificate.NotAfter)
            Console.Error.WriteLine($"Warning: certificate {normalized} expired on {certificate.NotAfter:yyyy-MM-dd}.");

        return certificate;
    }
}
