using Santolibre.GoogleMailBackup.Models;
using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleApiService
    {
        IReadOnlyList<string> GetThreadIds(string query);
        EmailThread GetEmailThread(string threadId);
    }
}
