# graph-mail-fetcher

.NET 10 console app that exports Exchange Online mail through Microsoft Graph. See README.md for behaviour.

- Build and test: `dotnet test`
- **Versioning:** every change to the application must bump `<Version>` in
  `src/GraphMailFetcher/GraphMailFetcher.csproj` (semantic versioning: patch for fixes, minor for new
  features, major for breaking command-line or output changes). `.github/workflows/release.yml` publishes a
  GitHub release for each new version that reaches `main`.
- Keep README.md and the usage text in `Options.cs` in sync with the command line.
