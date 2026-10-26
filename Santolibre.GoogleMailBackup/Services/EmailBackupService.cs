using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Santolibre.GoogleMailBackup.Services
{
    public class GoogleMailBackupService : IGoogleMailBackupService
    {
        private readonly IGoogleApiService _googleApiService;
        private readonly ILogger<GoogleMailBackupService> _logger;
        private const int MaxSubjectFileNameLength = 100;

        private readonly FailFastResourceRetriever _resourceRetriever;

        public GoogleMailBackupService(IGoogleApiService googleApiService, ILogger<GoogleMailBackupService> logger)
        {
            _googleApiService = googleApiService;
            _logger = logger;
            _resourceRetriever = new FailFastResourceRetriever(host => _logger.LogWarning($"Could not load images from {host}, skipping that host for a while"));
        }

        public ExportResult ExportEmails(string? query, IReadOnlyList<string>? threadIds, string exportPath, int skip = 0, Action<int>? onThreadsFound = null, Action<int, int, string?, string?>? onThreadStarting = null, Action<string>? onThreadFailed = null, Action<TimeSpan, int, int>? onRateLimited = null)
        {
            if (onRateLimited != null)
            {
                _googleApiService.RateLimited += onRateLimited;
            }

            try
            {
                return ExportThreads(query, threadIds, exportPath, skip, onThreadsFound, onThreadStarting, onThreadFailed);
            }
            finally
            {
                if (onRateLimited != null)
                {
                    _googleApiService.RateLimited -= onRateLimited;
                }
            }
        }

        private ExportResult ExportThreads(string? query, IReadOnlyList<string>? requestedThreadIds, string exportPath, int skip, Action<int>? onThreadsFound, Action<int, int, string?, string?>? onThreadStarting, Action<string>? onThreadFailed)
        {
            var threadIds = requestedThreadIds ?? _googleApiService.GetThreadIds(query!);
            _logger.LogInformation($"Found {threadIds.Count} threads");
            onThreadsFound?.Invoke(threadIds.Count);

            var exported = 0;
            var failed = 0;
            var usedFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (skip > 0)
            {
                _logger.LogInformation($"Skipping the first {Math.Min(skip, threadIds.Count)} threads");
            }

            for (var i = Math.Min(skip, threadIds.Count); i < threadIds.Count; i++)
            {
                var threadId = threadIds[i];
                var announced = false;

                try
                {
                    var emailThread = _googleApiService.GetEmailThread(threadId);
                    if (emailThread.Emails.Count == 0)
                    {
                        throw new InvalidOperationException("None of the messages in the thread could be parsed, see the errors above");
                    }

                    announced = true;
                    onThreadStarting?.Invoke(i + 1, threadIds.Count, emailThread.Emails.First().From, emailThread.Emails.First().Subject);

                    var emailThreadDate = emailThread.Emails.Last().DateCreated.ToString("yyyy-MM-dd HH-mm-ss");
                    var emailThreadSubject = emailThread.Emails.First().Subject.ToValidFileName();
                    if (emailThreadSubject.Length > MaxSubjectFileNameLength)
                    {
                        emailThreadSubject = emailThreadSubject.Substring(0, MaxSubjectFileNameLength).Trim();
                    }
                    var emailThreadSender = GetSenderForFileName(emailThread.Emails.First().From);
                    var pdfFilename = Path.Combine(exportPath, $"{emailThreadDate} - {emailThreadSender} - {emailThreadSubject}.pdf");
                    if (!usedFilenames.Add(pdfFilename))
                    {
                        // Threads with identical date, sender and subject (or missing headers) must not overwrite each other
                        pdfFilename = Path.Combine(exportPath, $"{emailThreadDate} - {emailThreadSender} - {emailThreadSubject} ({threadId}).pdf");
                        usedFilenames.Add(pdfFilename);
                    }

                    _logger.LogInformation($"Create new pdf, Filename={pdfFilename}");

                    try
                    {
                        WriteThreadPdf(emailThread, pdfFilename, true);
                    }
                    catch (Exception e) when (emailThread.Emails.Any(x => x.Attachments.Any(a => a.MimeType == "application/pdf")))
                    {
                        // A malformed attachment can make iText fail while writing the merged document
                        _logger.LogWarning(e, $"Create pdf again without PDF attachments, Filename={pdfFilename}");
                        WriteThreadPdf(emailThread, pdfFilename, false);
                    }

                    exported++;
                }
                catch (Exception e)
                {
                    if (!announced)
                    {
                        onThreadStarting?.Invoke(i + 1, threadIds.Count, null, null);
                    }

                    failed++;
                    _logger.LogError(e, $"PDF conversion error, ThreadId={threadId}");
                    onThreadFailed?.Invoke(threadId);
                }
            }

            return new ExportResult(exported, failed);
        }

        private void WriteThreadPdf(EmailThread emailThread, string pdfFilename, bool includeAttachments)
        {
        using (var pdfDocument = new PdfDocument(new PdfWriter(pdfFilename)))
        {
            var pdfMerger = new PdfMerger(pdfDocument);

            foreach (var email in emailThread.Emails)
            {
                string emailContent;
                if (email.Content.Any(x => x.MimeType == "text/html"))
                {
                    emailContent = email.Content.First(x => x.MimeType == "text/html").Content;
                }
                else
                {
                    emailContent = $"<html><head></head><body><pre>{email.Content.FirstOrDefault()?.Content}</pre></body></html>";
                }

                if (Regex.IsMatch(emailContent, "(<head.*?>)"))
                {
                    var styleToInject = $@"
<style>
table.sl-header td {{
    font-family: Helvetica;
    font-size: 14px;
}}
</style>";

                    emailContent = Regex.Replace(emailContent, $"(<head.*?>)", $"$1{styleToInject}");

                }

                if (Regex.IsMatch(emailContent, "(<body.*?>)"))
                {
                    var descriptionToInject = $@"
<table class=""sl-header"">
<tr>
    <td width=""100"">Created at</td>
    <td>{email.DateCreated}</td>
</tr>
<tr>
    <td>From</td>
    <td>{HttpUtility.HtmlEncode(email.From)}</td>
</tr>
<tr>
    <td>To</td>
    <td>{HttpUtility.HtmlEncode(email.To)}</td>
</tr>
<tr>
    <td>Subject</td>
    <td>{email.Subject}
</tr>
</table>
<br/><hr/><br/>";

                    emailContent = Regex.Replace(emailContent, $"(<body.*?>)", $"$1{descriptionToInject}");

                    if (false)
                    {
                        emailContent = Regex.Replace(emailContent, @"<style>\s+@media.*?<\/style>", "", RegexOptions.Singleline);
                    }
                }

                var emailPdf = new PdfDocument(new PdfReader(new MemoryStream(ConvertHtmlToPdf(emailContent))));
                _logger.LogInformation($"Add new pdf page");
                pdfMerger.Merge(emailPdf, 1, emailPdf.GetNumberOfPages());

                foreach (var attachment in email.Attachments.Where(x => includeAttachments && x.MimeType == "application/pdf"))
                {
                    try
                    {
                        var attachmentPdf = new PdfDocument(new PdfReader(new MemoryStream(attachment.Data)));
                        _logger.LogInformation($"Add new pdf page");
                        pdfMerger.Merge(attachmentPdf, 1, attachmentPdf.GetNumberOfPages());
                    }
                    catch (Exception e)
                    {
                        // Some attachments are named and typed as PDF but aren't, the email itself is still worth keeping
                        _logger.LogWarning(e, $"Skip PDF attachment that can't be read, Filename={attachment.Filename}");
                    }
                }
            }
        }
        }

        // iText rejects some real-world images and CSS (e.g. odd media queries, garbage lengths), so retry with less rather than losing the email
        private byte[] ConvertHtmlToPdf(string html)
        {
            var attempts = new (string Name, Func<string, string> Simplify)[]
            {
                ("as is", x => x),
                ("without images", RemoveImages),
                ("without images and style blocks", x => RemoveStyleBlocks(RemoveImages(x))),
                ("without images, style blocks and presentation attributes", x => RemovePresentationAttributes(RemoveStyleBlocks(RemoveImages(x))))
            };

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var pdfWriteStream = new MemoryStream();
                    using var htmlSource = new MemoryStream(Encoding.UTF8.GetBytes(attempts[attempt].Simplify(html)));
                    HtmlConverter.ConvertToPdf(htmlSource, pdfWriteStream, new ConverterProperties().SetResourceRetriever(_resourceRetriever));
                    return pdfWriteStream.ToArray();
                }
                catch (Exception e) when (attempt < attempts.Length - 1)
                {
                    _logger.LogWarning(e, $"HTML conversion failed ({attempts[attempt].Name}), retrying {attempts[attempt + 1].Name}");
                }
            }
        }

        private static string RemoveImages(string html)
        {
            return Regex.Replace(html, @"<img\b[^>]*>", "", RegexOptions.IgnoreCase);
        }

        // Keeps the header style injected for the message header table
        private static string RemoveStyleBlocks(string html)
        {
            return Regex.Replace(html, @"<style\b.*?</style>", m => m.Value.Contains("table.sl-header") ? m.Value : "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        private static string RemovePresentationAttributes(string html)
        {
            return Regex.Replace(html, @"\s(style|width|height|bgcolor|background|border|cellpadding|cellspacing|align|valign)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);
        }

        // Some messages have no or an unparsable From header, those still need a usable file name
        private static string GetSenderForFileName(string from)
        {
            if (MailAddress.TryCreate(from, out var address))
            {
                return address.Address;
            }

            var sender = from.ToValidFileName().Trim();
            return sender.Length > 0 ? sender : "unknown";
        }
    }
}
