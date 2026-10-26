using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Logging;
using Santolibre.GoogleMailBackup.Services;
using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace Santolibre.GoogleMailBackup
{
    class Program
    {
        public static int Main(string[] args)
        {
            var queryOption = new Option<string>("--query", "-q")
            {
                Description = "Gmail search query selecting the threads to export, e.g. \"label:invoices after:2024/01/01\" (required unless --thread-ids is given)"
            };
            var threadIdsOption = new Option<string>("--thread-ids", "-t")
            {
                Description = "Comma-separated ids of the threads to export instead of searching with --query, e.g. 1803e2e895527723,1803e2e895527724"
            };
            var exportPathOption = new Option<DirectoryInfo>("--export-path", "-o")
            {
                Description = "Directory the PDF files are written to (created if missing)",
                Required = true
            };
            var skipOption = new Option<int>("--skip", "-s")
            {
                Description = "Number of emails to skip at the start of the result list, e.g. those already backed up by a former run",
                DefaultValueFactory = _ => 0
            };
            skipOption.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int>() < 0)
                {
                    result.AddError("--skip must not be negative");
                }
            });
            var logFileOption = new Option<FileInfo>("--log-file", "-l")
            {
                Description = "Log file to append to",
                DefaultValueFactory = _ => new FileInfo(Path.Combine(AppContext.BaseDirectory, "google-mail-backup.log"))
            };

            var rootCommand = new RootCommand("Back up Gmail threads as PDF files")
            {
                queryOption,
                threadIdsOption,
                exportPathOption,
                skipOption,
                logFileOption
            };
            rootCommand.Validators.Add(result =>
            {
                var hasQuery = result.GetValue(queryOption) != null;
                var hasThreadIds = result.GetValue(threadIdsOption) != null;
                if (hasQuery == hasThreadIds)
                {
                    result.AddError("Specify either --query or --thread-ids");
                }
            });

            rootCommand.SetAction(parseResult =>
            {
                var logFile = parseResult.GetRequiredValue(logFileOption).FullName;

                using var host = BuildHost(logFile);
                var app = host.Services.GetRequiredService<App>();

                var threadIds = parseResult.GetValue(threadIdsOption)?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct()
                    .ToList();

                return app.Run(parseResult.GetValue(queryOption), threadIds, parseResult.GetRequiredValue(exportPathOption).FullName, parseResult.GetValue(skipOption), logFile);
            });

            return rootCommand.Parse(args).Invoke();
        }

        private static IHost BuildHost(string logFile)
        {
            var builder = Host.CreateApplicationBuilder();

            // Logs go to the file only, the console is reserved for the progress display
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddProvider(new FileLoggerProvider(logFile));

            builder.Services.AddSingleton<IGoogleApiService, GoogleApiService>();
            builder.Services.AddSingleton<IGoogleMailBackupService, GoogleMailBackupService>();
            builder.Services.AddTransient<App>();

            return builder.Build();
        }
    }
}
