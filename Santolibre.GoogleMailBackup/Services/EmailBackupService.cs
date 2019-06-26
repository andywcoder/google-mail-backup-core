using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using Microsoft.Extensions.Logging;
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

        public void ExportEmails(string query, string exportPath)
        {
            var emailThreads = _googleApiService.GetEmailThreads(query);
            foreach (var emailThread in emailThreads)
            {
                try
                {
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
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "PDF conversion error");
                }
            }
        }
    }
}
