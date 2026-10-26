using Santolibre.GoogleMailBackup.Models;
using System;
using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleApiService
    {
        /// <summary>Raised before waiting on a Gmail rate limit, with the delay and the retry attempt out of the maximum.</summary>
        event Action<TimeSpan, int, int>? RateLimited;

        IReadOnlyList<string> GetThreadIds(string query);
        EmailThread GetEmailThread(string threadId);
    }
}
