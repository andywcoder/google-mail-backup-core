using System;

namespace Santolibre.GoogleMailBackup.Models
{
    public class EmailAttachment
    {
        public string Filename { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
