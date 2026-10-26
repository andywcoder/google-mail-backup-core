using Santolibre.GoogleMailBackup.Models;
using System;
using System.Collections.Generic;

namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleMailBackupService
    {
        /// <summary>Exports each thread to a PDF, either the given threads or those matching the query.</summary>
        /// <param name="query">Gmail search query, used when no thread ids are given.</param>
        /// <param name="threadIds">Ids of the threads to export instead of searching with the query.</param>
        /// <param name="skip">Number of threads at the start of the result list to skip, e.g. those exported by a former run.</param>
        /// <param name="onThreadsFound">Called once with the number of threads that will be exported.</param>
        /// <param name="onThreadStarting">Called for each thread once it is loaded, with its 1-based index, the total, and the sender and subject of its first message (both null if the thread could not be loaded).</param>
        /// <param name="onThreadFailed">Called with the thread id when a thread could not be exported.</param>
        /// <param name="onRateLimited">Called before waiting on a Gmail rate limit, with the delay and the retry attempt out of the maximum.</param>
        ExportResult ExportEmails(string? query, IReadOnlyList<string>? threadIds, string exportPath, int skip = 0, Action<int>? onThreadsFound = null, Action<int, int, string?, string?>? onThreadStarting = null, Action<string>? onThreadFailed = null, Action<TimeSpan, int, int>? onRateLimited = null);
    }
}
