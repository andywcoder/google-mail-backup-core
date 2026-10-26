using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;

namespace Santolibre.GoogleMailBackup.Services
{
    public class GoogleApiService : IGoogleApiService
    {
        private GmailService _gmailService;
        private readonly ILogger<GoogleApiService> _logger;

        public event Action<TimeSpan, int, int>? RateLimited;

        public GoogleApiService(ILogger<GoogleApiService> logger)
        {
            _logger = logger;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            UserCredential credential;

            using (var stream = new FileStream(Path.Combine(AppContext.BaseDirectory, "credentials.json"), FileMode.Open, FileAccess.Read))
            {
                string tokenFilename = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new string[] { GmailService.Scope.GmailReadonly },
                    "andreas.wyss@gmail.com",
                    System.Threading.CancellationToken.None,
                    new FileDataStore(Path.Combine(AppContext.BaseDirectory, tokenFilename), true)).Result;
            }

            _gmailService = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GooglGoogleMailBackup"
            });
        }

        public IReadOnlyList<string> GetThreadIds(string query)
        {
            var threadIds = new List<string>();
            string? pageToken = null;

            do
            {
                var request = _gmailService.Users.Threads.List("me");
                request.Q = query;
                request.MaxResults = 500;
                request.PageToken = pageToken;

                var response = ExecuteWithRetry(() => request.Execute());
                threadIds.AddRange((response.Threads ?? new List<Thread>()).Select(x => x.Id));
                pageToken = response.NextPageToken;

                _logger.LogInformation($"Listed threads, Count={threadIds.Count}");
            }
            while (!string.IsNullOrEmpty(pageToken));

            return threadIds;
        }

        public EmailThread GetEmailThread(string threadId)
        {
            var emailThread = new EmailThread();
            var fullThread = GetMessageThread(threadId);
            _logger.LogInformation($"Process thread, Id={fullThread.Id}, MessageCount={fullThread.Messages.Count}");

            foreach (var message in fullThread.Messages)
            {
                _logger.LogInformation($"Process message, Id={message.Id}");
                try
                {
                    emailThread.Emails.Add(ProcessMessage(message));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Message parsing error");
                }
            }

            return emailThread;
        }

        private Email ProcessMessage(Message message)
        {
            var email = new Email();

            foreach (var header in message.Payload.Headers)
            {
                if (string.Equals(header.Name, "Date", StringComparison.OrdinalIgnoreCase))
                {
                    var dateTimePattern1 = @"([A-Za-z]{3}, \d+ [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} ([+-]\d{4}|GMT))";
                    var dateTimePattern2 = @"(\d+ [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4})";
                    // UT and UTC are valid RFC 822 zones but DateTime.Parse only knows GMT
                    var dateValue = Regex.Replace(header.Value, @"\bUTC?\b", "GMT");
                    DateTime dateTime;
                    if (Regex.IsMatch(dateValue, dateTimePattern1))
                    {
                        dateTime = DateTime.Parse(Regex.Match(dateValue, dateTimePattern1).Groups[0].Captures[0].Value);
                    }
                    else if (Regex.IsMatch(dateValue, dateTimePattern2))
                    {
                        dateTime = DateTime.Parse(Regex.Match(dateValue, dateTimePattern2).Groups[0].Captures[0].Value);
                    }
                    else if (DateTime.TryParse(Regex.Replace(dateValue, @"^[A-Za-z]{3},\s*", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDateTime))
                    {
                        // Looser forms, e.g. a two-digit year or no seconds: "28 Jul 09 04:18 -0800"
                        dateTime = parsedDateTime;
                    }
                    else
                    {
                        throw new ArgumentException($"Couldn't parse date header {header.Value}");
                    }
                    email.DateCreated = dateTime;
                }
                if (string.Equals(header.Name, "To", StringComparison.OrdinalIgnoreCase))
                {
                    email.To = header.Value;
                }
                if (string.Equals(header.Name, "From", StringComparison.OrdinalIgnoreCase))
                {
                    email.From = header.Value;
                }
                if (string.Equals(header.Name, "Subject", StringComparison.OrdinalIgnoreCase))
                {
                    email.Subject = header.Value;
                }
            }

            if (message.Payload.Parts == null)
            {
                if (message.Payload.MimeType == "text/plain" || message.Payload.MimeType == "text/html")
                {
                    var (mimeType, content) = GetMessagePartTextOrHtmlContent(message.Payload);
                    email.Content.Add(new EmailContent() { MimeType = mimeType, Content = content });
                }
            }
            else
            {
                foreach (var messagePart in message.Payload.Parts)
                {
                    ProcessMessageParts(message.Id, email, messagePart);
                }
            }

            return email;
        }

        private void ProcessMessageParts(string messageId, Email email, MessagePart parentMessagePart)
        {
            if (parentMessagePart.Parts != null)
            {
                foreach (var messagePart in parentMessagePart.Parts)
                {
                    ProcessMessageParts(messageId, email, messagePart);
                }
            }

            if (!string.IsNullOrEmpty(parentMessagePart.Filename))
            {
                // Only PDF attachments are merged into the export, so don't spend API quota on downloading the rest
                if (parentMessagePart.MimeType == "application/pdf")
                {
                    var attachment = GetAttachment(messageId, parentMessagePart.Body.AttachmentId);
                    var data = attachment.Data.UrlSafeBase64StringToByteArray();
                    email.Attachments.Add(new EmailAttachment() { Filename = parentMessagePart.Filename, MimeType = parentMessagePart.MimeType, Data = data });
                }
                else
                {
                    _logger.LogInformation($"Skip attachment, Filename={parentMessagePart.Filename}, MimeType={parentMessagePart.MimeType}");
                }
            }
            else if (parentMessagePart.MimeType == "text/plain" || parentMessagePart.MimeType == "text/html")
            {
                var (mimeType, content) = GetMessagePartTextOrHtmlContent(parentMessagePart);
                email.Content.Add(new EmailContent() { MimeType = mimeType, Content = content });
            }
        }

        private (string MimeType, string Content) GetMessagePartTextOrHtmlContent(MessagePart part)
        {
            // Not parsed with System.Net.Mime.ContentType, which throws on slightly malformed headers found in real emails
            var contentTypeHeader = part.Headers.FirstOrDefault(x => string.Equals(x.Name, "Content-Type", StringComparison.OrdinalIgnoreCase));
            var charSet = contentTypeHeader != null ? Regex.Match(contentTypeHeader.Value, @"charset\s*=\s*""?([^"";\s]+)", RegexOptions.IgnoreCase).Groups[1].Value : "";
            if (string.IsNullOrEmpty(part.Body?.Data))
            {
                return (part.MimeType, string.Empty);
            }

            var data = part.Body.Data.UrlSafeBase64StringToByteArray();
            if (string.IsNullOrEmpty(charSet))
            {
                _logger.LogWarning($"Content type {part.MimeType} has no charSet specified, assuming utf-8");
                charSet = "utf-8";
            }

            return (part.MimeType, GetEncoding(charSet).GetString(data));
        }

        private Encoding GetEncoding(string charSet)
        {
            var name = charSet.ToLower();
            if (name == "utf8")
            {
                name = "utf-8";
            }

            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                _logger.LogWarning($"Character set {charSet} not supported, assuming utf-8");
                return Encoding.UTF8;
            }
        }

        private Thread GetMessageThread(string threadId)
        {
            var request = _gmailService.Users.Threads.Get("me", threadId);
            return ExecuteWithRetry(() => request.Execute());
        }

        private MessagePartBody GetAttachment(string messageId, string attachmentId)
        {
            var request = _gmailService.Users.Messages.Attachments.Get("me", messageId, attachmentId);
            return ExecuteWithRetry(() => request.Execute());
        }

        // Gmail allows ~250 quota units per minute per user, so on a rate limit wait for the window to reset and retry
        private T ExecuteWithRetry<T>(Func<T> execute)
        {
            const int maxRetries = 6;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return execute();
                }
                catch (GoogleApiException e) when (attempt <= maxRetries && IsRateLimitError(e))
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(15 * attempt, 60));
                    _logger.LogWarning($"Gmail rate limit reached, retrying in {delay.TotalSeconds} seconds (attempt {attempt}/{maxRetries})");
                    RateLimited?.Invoke(delay, attempt, maxRetries);
                    System.Threading.Thread.Sleep(delay);
                }
            }
        }

        private static bool IsRateLimitError(GoogleApiException e)
        {
            return e.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests
                || (e.HttpStatusCode == System.Net.HttpStatusCode.Forbidden && e.Error?.Errors?.Any(x => x.Reason == "rateLimitExceeded" || x.Reason == "userRateLimitExceeded") == true);
        }
    }
}
