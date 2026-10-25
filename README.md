# google-mail-backup-core

A command-line tool that backs up Gmail conversations as PDF files.

It searches your mailbox with a standard Gmail query, and writes one PDF per conversation (thread). Every message in the thread is rendered in order, each starting with a small header (created at, from, to, subject). PDF attachments are appended after the message they belong to, so the result is a single self-contained document per thread. Files are named `<date of last message> - <sender address> - <subject>.pdf`.

## Features

- Select what to back up with any Gmail search query (labels, dates, senders, ...)
- HTML emails keep their original look; plain-text emails are rendered as preformatted text
- PDF attachments are merged into the thread's PDF
- Read-only: the app only asks for the `gmail.readonly` scope and never changes your mailbox
- Everything stays local: PDFs and the OAuth token are written to disk, nothing is sent anywhere except to Google's API

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and run
- A Google Cloud OAuth client (type "Desktop app") with the Gmail API enabled, saved as `credentials.json` in the project folder (it is copied next to the executable on build)

On the first run a browser window opens to authorize read-only access. The resulting token is cached in `token.json/` next to the executable. Neither `credentials.json` nor `token.json/` should be committed or shared.

## Usage

```
dotnet run --project Santolibre.GoogleMailBackup -- --query <gmail-search> --export-path <directory>

  -q, --query          Gmail search query, e.g. "label:invoices after:2024/01/01"
  -o, --export-path    Directory the PDFs are written to (created if missing)
  -l, --log-file       Log file to append to (default: google-mail-backup.log next to the executable)
  -h, --help           Show help
  --version            Show version
```

The console prints one line per email (`Backing up email 1/2000...`). Details, warnings and errors are appended to the log file. The exit code is 0 on success and non-zero on invalid arguments or a failed export.

## Licenses

This project is licensed under the terms in [LICENSE](LICENSE).

PDF generation uses [iText](https://itextpdf.com/) (`itext7.pdfhtml`, which pulls in iText Core), which is **dual-licensed: AGPL v3 or a commercial license**. Under the AGPL, if you distribute this application, or make it available to users over a network, you must make the complete corresponding source code available under the AGPL as well. Using it privately to back up your own mail carries no such obligation. If you want to build a closed-source or commercial product on top of this code, you need a commercial iText license.
