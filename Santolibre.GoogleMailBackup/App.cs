using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Santolibre.GoogleMailBackup
{
    public class App
    {
        private readonly IGoogleMailBackupService _emailService;
        private readonly ILogger<App> _logger;

        public App(IGoogleMailBackupService emailService, ILogger<App> logger)
        {
            _emailService = emailService;
            _logger = logger;
        }

        public int Run(string? query, IReadOnlyList<string>? threadIds, string exportPath, int skip, string logFile)
        {
            var startTime = DateTime.Now;

            try
            {
                Directory.CreateDirectory(exportPath);

                _logger.LogInformation($"Export emails, Query={query}, ThreadIds={(threadIds == null ? "" : string.Join(",", threadIds))}, ExportPath={exportPath}, Skip={skip}");

                var result = _emailService.ExportEmails(
                    query,
                    threadIds,
                    exportPath,
                    skip,
                    threadCount => Console.WriteLine($"Found {threadCount} emails"),
                    (current, total, sender, subject) => Console.WriteLine(string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(sender) ? $"Backing up email {current}/{total}..." : $"Backing up email {current}/{total} ({sender}, {subject})..."),
                    threadId => Console.WriteLine($"  Failed to back up thread {threadId}"),
                    (delay, attempt, maxRetries) => Console.WriteLine($"  Gmail rate limit reached, waiting {delay.TotalSeconds:0} seconds (retry {attempt}/{maxRetries})..."));

                var elapsed = DateTime.Now - startTime;
                _logger.LogInformation($"Done, Exported={result.Exported}, Failed={result.Failed}, Seconds={elapsed.TotalSeconds}");

                Console.WriteLine($"Done: {result.Exported} backed up, {result.Failed} failed ({elapsed.TotalSeconds:0} seconds)");
                Console.WriteLine($"Log file: {logFile}");

                return result.Failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export failed");
                Console.Error.WriteLine($"Export failed: {ex.Message}");
                Console.Error.WriteLine($"Details in log file: {logFile}");
                return 1;
            }
        }
    }
}
