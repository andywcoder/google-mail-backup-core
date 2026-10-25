# google-mail-backup-core

See [README.md](README.md) for what the app does and how to use it.

## Build and run

- `dotnet build`
- `dotnet run --project Santolibre.GoogleMailBackup -- -q "<gmail query>" -o <export dir>`
- Running it hits the real Gmail account (read-only) using `credentials.json` and the cached `token.json/`. Don't use a dummy query to test.

## Conventions

- Target is net10.0. The CLI is built on System.CommandLine, and console shows plain progress lines; logging goes to a file via the small FileLoggerProvider in Logging/.
- Nullable reference types are enabled.
- PDF generation uses iText (AGPL), which is why the README has a licence note. Keep it in sync if the PDF library changes.
- Source files use CRLF line endings and UTF-8 with BOM.
- Never commit `credentials.json` or `token.json/`.
- Git commits: do not add attribution lines (no `Co-Authored-By`, no "Generated with" footers).
