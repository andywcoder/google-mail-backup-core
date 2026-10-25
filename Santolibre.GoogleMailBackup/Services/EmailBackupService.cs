using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Models;
using System;
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

        public GoogleMailBackupService(IGoogleApiService googleApiService, ILogger<GoogleMailBackupService> logger)
        {
            _googleApiService = googleApiService;
            _logger = logger;
        }

        public ExportResult ExportEmails(string query, string exportPath, Action<int>? onThreadsFound = null, Action<int, int>? onThreadStarting = null, Action<string>? onThreadFailed = null)
        {
            var threadIds = _googleApiService.GetThreadIds(query);
            _logger.LogInformation($"Found {threadIds.Count} threads");
            onThreadsFound?.Invoke(threadIds.Count);

            var exported = 0;
            var failed = 0;

            for (var i = 0; i < threadIds.Count; i++)
            {
                var threadId = threadIds[i];
                onThreadStarting?.Invoke(i + 1, threadIds.Count);

                try
                {
                    var emailThread = _googleApiService.GetEmailThread(threadId);
                    if (emailThread.Emails.Count == 0)
                    {
                        throw new InvalidOperationException("None of the messages in the thread could be parsed, see the errors above");
                    }

                    var emailThreadDate = emailThread.Emails.Last().DateCreated.ToString("yyyy-MM-dd HH-mm-ss");
                    var emailThreadSubject = emailThread.Emails.First().Subject.ToValidFileName();
                    var emailThreadSender = new MailAddress(emailThread.Emails.First().From);
                    var pdfFilename = Path.Combine(exportPath, $"{emailThreadDate} - {emailThreadSender.Address} - {emailThreadSubject}.pdf");

                    _logger.LogInformation($"Create new pdf, Filename={pdfFilename}");

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
                                emailContent = $"<html><head></head><body><pre>{email.Content.First().Content}</pre></body></html>";
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

                            byte[] byteArray = Encoding.UTF8.GetBytes(emailContent);

                            using (var htmlSource = new MemoryStream(byteArray))
                            {
                                using (var pdfWriteStream = new MemoryStream())
                                {
                                    var converterProperties = new ConverterProperties();
                                    HtmlConverter.ConvertToPdf(htmlSource, pdfWriteStream, converterProperties);

                                    _logger.LogInformation($"Add new pdf page");
                                    var emailPdf = new PdfDocument(new PdfReader(new MemoryStream(pdfWriteStream.ToArray())));
                                    pdfMerger.Merge(emailPdf, 1, emailPdf.GetNumberOfPages());

                                    foreach (var attachment in email.Attachments.Where(x => x.MimeType == "application/pdf"))
                                    {
                                        _logger.LogInformation($"Add new pdf page");
                                        var attachmentPdf = new PdfDocument(new PdfReader(new MemoryStream(attachment.Data)));
                                        pdfMerger.Merge(attachmentPdf, 1, attachmentPdf.GetNumberOfPages());
                                    }
                                }
                            }
                        }
                    }

                    exported++;
                }
                catch (Exception e)
                {
                    failed++;
                    _logger.LogError(e, $"PDF conversion error, ThreadId={threadId}");
                    onThreadFailed?.Invoke(threadId);
                }
            }

            return new ExportResult(exported, failed);
        }
    }
}
