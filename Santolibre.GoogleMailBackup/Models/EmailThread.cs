using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Models
{
    public class EmailThread
    {
        public List<Email> Emails { get; set; } = new List<Email>();
    }
}
