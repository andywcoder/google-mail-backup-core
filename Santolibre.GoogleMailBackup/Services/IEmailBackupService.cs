using Santolibre.GoogleMailBackup.Models;
using System;

namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleMailBackupService
    {
        /// <summary>Lists the threads matching the query and exports each one to a PDF.</summary>
        /// <param name="onThreadsFound">Called once with the number of threads that will be exported.</param>
        /// <param name="onThreadStarting">Called before each thread with its 1-based index and the total.</param>
        /// <param name="onThreadFailed">Called with the thread id when a thread could not be exported.</param>
        ExportResult ExportEmails(string query, string exportPath, Action<int>? onThreadsFound = null, Action<int, int>? onThreadStarting = null, Action<string>? onThreadFailed = null);
    }
}
