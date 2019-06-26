using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Services;
using System;
using System.Linq;

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

        public void Run(string[] args)
        {
            var startTime = DateTime.Now;

            var query = args.SingleOrDefault(x => x.Contains("--query="));
            if (string.IsNullOrEmpty(query))
            {
                _logger.LogError("Parameter --query is missing");
                return;
            }
            else
            {
                query = query.Replace("--query=", "");
            }

            var exportPath = args.SingleOrDefault(x => x.Contains("--export-path="));
            if (string.IsNullOrEmpty(exportPath))
            {
                _logger.LogError("Parameter --export-path is missing");
                return;
            }
            else
            {
                exportPath = exportPath.Replace("--export-path=", "");
            }

            try
            {
                _logger.LogInformation("Export emails");

                _emailService.ExportEmails(query, exportPath);
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    _logger.LogError(e.Message);
                }
            }

            _logger.LogInformation($"Done ({(DateTime.Now - startTime).TotalSeconds} seconds)");
        }
    }
}
