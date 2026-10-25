using System;
using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Models
{
    public class Email
    {
        public DateTime DateCreated { get; set; }
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public List<EmailContent> Content { get; set; } = new List<EmailContent>();
        public List<EmailAttachment> Attachments { get; set; } = new List<EmailAttachment>();
    }
}
