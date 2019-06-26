namespace Santolibre.GoogleMailBackup.Models
{
    public class EmailAttachment
    {
        public string Filename { get; set; }
        public string MimeType { get; set; }
        public byte[] Data { get; set; }
    }
}
