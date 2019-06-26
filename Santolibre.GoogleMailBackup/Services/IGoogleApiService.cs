using Santolibre.GoogleMailBackup.Models;
using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleApiService
    {
        List<EmailThread> GetEmailThreads(string query);
    }
}
