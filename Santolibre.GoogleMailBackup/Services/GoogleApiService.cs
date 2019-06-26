using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Models;
using System;
using System.Collections.Generic;
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

        public GoogleApiService(ILogger<GoogleApiService> logger)
        {
            _logger = logger;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            UserCredential credential;

            using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                string tokenFilename = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new string[] { GmailService.Scope.GmailReadonly },
                    "andreas.wyss@gmail.com",
                    System.Threading.CancellationToken.None,
                    new FileDataStore(tokenFilename, true)).Result;
            }

            _gmailService = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GooglGoogleMailBackup"
            });
        }

        public List<EmailThread> GetEmailThreads(string query)
        {
            var emailThreads = new List<EmailThread>();
            var messageThreads = GetMessageThreads(query);
            foreach (var thread in messageThreads.Threads)
            {
                var emailThread = new EmailThread();
                var fullThread = GetMessageThread(thread.Id);
                _logger.LogInformation($"Process thread, Id={fullThread.Id}, MessageCount={fullThread.Messages.Count}");

                foreach (var message in fullThread.Messages)
                {
                    var fullMessage = GetMessage(message.Id);
                    _logger.LogInformation($"Process message, Id={message.Id}");
                    try
                    {
                        emailThread.Emails.Add(ProcessMessage(fullMessage));
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Message parsing error");
                    }
                }

                emailThreads.Add(emailThread);
            }

            return emailThreads;
        }

        private Email ProcessMessage(Message message)
        {
            var email = new Email();

            foreach (var header in message.Payload.Headers)
            {
                if (header.Name == "Date")
                {
                    var dateTimePattern1 = @"([A-Za-z]{3}, \d+ [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} ([+-]\d{4}|GMT))";
                    var dateTimePattern2 = @"(\d+ [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4})";
                    DateTime dateTime;
                    if (Regex.IsMatch(header.Value, dateTimePattern1))
                    {
                        dateTime = DateTime.Parse(Regex.Match(header.Value, dateTimePattern1).Groups[0].Captures[0].Value);
                    }
                    else if (Regex.IsMatch(header.Value, dateTimePattern2))
                    {
                        dateTime = DateTime.Parse(Regex.Match(header.Value, dateTimePattern2).Groups[0].Captures[0].Value);
                    }
                    else
                    {
                        throw new ArgumentException($"Couldn't parse date header {header.Value}");
                    }
                    email.DateCreated = dateTime;
                }
                if (header.Name == "To")
                {
                    email.To = header.Value;
                }
                if (header.Name == "From")
                {
                    email.From = header.Value;
                }
                if (header.Name == "Subject")
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
                var attachment = GetAttachment(messageId, parentMessagePart.Body.AttachmentId);
                var data = attachment.Data.UrlSafeBase64StringToByteArray();
                email.Attachments.Add(new EmailAttachment() { Filename = parentMessagePart.Filename, MimeType = parentMessagePart.MimeType, Data = data });
            }
            else if (parentMessagePart.MimeType == "text/plain" || parentMessagePart.MimeType == "text/html")
            {
                var (mimeType, content) = GetMessagePartTextOrHtmlContent(parentMessagePart);
                email.Content.Add(new EmailContent() { MimeType = mimeType, Content = content });
            }
        }

        private (string MimeType, string Content) GetMessagePartTextOrHtmlContent(MessagePart part)
        {
            var contentTypeHeader = part.Headers.FirstOrDefault(x => x.Name == "Content-Type");
            var contentType = contentTypeHeader != null ? new ContentType(contentTypeHeader.Value) : new ContentType($"{part.MimeType}; charset=utf-8");
            var data = part.Body.Data.UrlSafeBase64StringToByteArray();
            try
            {
                if (contentType.CharSet == null)
                {
                    _logger.LogWarning($"Content type {contentType.MediaType} has no charSet specified, assuming utf-8");
                    contentType.CharSet = "utf-8";
                }
                var encoding = Encoding.GetEncoding(contentType.CharSet.ToLower());
                var messagePartContent = encoding.GetString(data);
                return (part.MimeType, messagePartContent);
            }
            catch (Exception e)
            {
                throw new Exception($"character set {contentType.CharSet} not supported", e);
            }
        }

        private ListThreadsResponse GetMessageThreads(string query)
        {
            var request = _gmailService.Users.Threads.List("me");
            request.Q = query;
            var messageThreads = request.Execute();
            return messageThreads;
        }

        private Thread GetMessageThread(string threadId)
        {
            var request = _gmailService.Users.Threads.Get("me", threadId);
            return request.Execute();
        }

        private Message GetMessage(string messageId)
        {
            var request = _gmailService.Users.Messages.Get("me", messageId);
            return request.Execute();
        }

        private MessagePartBody GetAttachment(string messageId, string attachmentId)
        {
            var request = _gmailService.Users.Messages.Attachments.Get("me", messageId, attachmentId);
            return request.Execute();
        }
    }
}
