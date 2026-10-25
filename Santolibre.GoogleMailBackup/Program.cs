using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Santolibre.GoogleMailBackup.Logging;
using Santolibre.GoogleMailBackup.Services;
using System;
using System.CommandLine;
using System.IO;

namespace Santolibre.GoogleMailBackup
{
    class Program
    {
        public static int Main(string[] args)
        {
            var queryOption = new Option<string>("--query", "-q")
            {
                Description = "Gmail search query selecting the threads to export, e.g. \"label:invoices after:2024/01/01\"",
                Required = true
            };
            var exportPathOption = new Option<DirectoryInfo>("--export-path", "-o")
            {
                Description = "Directory the PDF files are written to (created if missing)",
                Required = true
            };
            var logFileOption = new Option<FileInfo>("--log-file", "-l")
            {
                Description = "Log file to append to",
                DefaultValueFactory = _ => new FileInfo(Path.Combine(AppContext.BaseDirectory, "google-mail-backup.log"))
            };

            var rootCommand = new RootCommand("Back up Gmail threads as PDF files")
            {
                queryOption,
                exportPathOption,
                logFileOption
            };

            rootCommand.SetAction(parseResult =>
            {
                var logFile = parseResult.GetRequiredValue(logFileOption).FullName;

                using var host = BuildHost(logFile);
                var app = host.Services.GetRequiredService<App>();

                return app.Run(parseResult.GetRequiredValue(queryOption), parseResult.GetRequiredValue(exportPathOption).FullName, logFile);
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
