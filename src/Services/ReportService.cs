using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Commands;
using SpocR.Models;
using SpocR.Utils;

namespace SpocR.Services
{
    public enum FileAction
    {
        Undefined,
        Created,
        Modified,
        UpToDate
    }

    public interface IReportService
    {
        void Output(string message);
        void Error(string message);
        void Warn(string message);
        void Note(string message);
        void Verbose(string message);
        void Success(string message);

        void Green(string message);
        void Yellow(string message);
        void Red(string message);
        void Gray(string message);

        // Custom 
        void PrintTitle(string title);
        void PrintImportantTitle(string title);
        void PrintSubTitle(string title);
        void PrintSummary(IEnumerable<string> summary, string headline);
        void PrintTotal(string total);
        void PrintDryRunMessage();
        void PrintConfiguration(ConfigurationModel config);

        void PrintFileActionMessage(string fileName, FileAction fileAction);
        void PrintCorruptConfigMessage(string message);
    }

    public class ReportService : IReportService
    {
        private readonly string LineStar = new string('*', 50);
        private readonly string LineMinus = new string('-', 50);
        private readonly string LineUnderscore = new string('_', 50);

        private readonly IConsoleReporter _reporter;
        private readonly ICommandOptions _commandOptions;

        public ReportService(IConsoleReporter reporter, CommandOptions commandOptions)
        {
            _reporter = reporter;
            _commandOptions = commandOptions;
        }

        public void Output(string message)
            => _reporter.Output(message);

        public void Error(string message)
            => _reporter.Error($"{message}");

        public void Warn(string message)
            => _reporter.Warn($"{message}");

        public void Success(string message)
            => _reporter.Success($"{message}");

        public void Verbose(string message)
        {
            if (_commandOptions.Verbose)
                _reporter.Verbose($"{message}");
        }

        public void Note(string message)
            => _reporter.Warn($"{message}");

        public void PrintTitle(string title)
        {
            Output("");
            Output(LineStar);
            Output(title);
            Output(LineStar);
        }

        public void PrintImportantTitle(string title)
        {
            Red("");
            Red(LineStar);
            Red(title);
            Red(LineStar);
        }

        public void PrintSubTitle(string title)
        {
            Output("");
            Output(title);
            Output(LineUnderscore);
        }

        public void PrintSummary(IEnumerable<string> summary, string headline = null)
        {
            Green("");
            Green(LineStar);
            if (headline != null)
            {
                var linePartLength = (LineStar.Length - (headline.Length + 2)) / 2;
                var linePartPlus = new string('+', linePartLength);
                Green($"{linePartPlus} {headline} {linePartPlus}");
                Green(LineStar);
            }

            foreach (var message in summary)
            {
                Green(message);
            }
        }

        public void PrintTotal(string total)
        {
            Green(LineMinus);
            Green(total);
            Green("");
        }

        public void PrintDryRunMessage()
        {
            Output("");
            Warn("Run with \"dry run\" means no changes were made");
        }

        public void PrintConfiguration(ConfigurationModel config)
        {
            var jsonSettings = new JsonSerializerOptions()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true,
                Converters = {
                    new JsonStringEnumConverter()
                }
            };

            var json = JsonSerializer.Serialize(config, jsonSettings);

            _reporter.Warn(json);
            Output("");
        }

        public void Green(string message)
            => _reporter.Success(message);

        public void Yellow(string message)
            => _reporter.Warn(message);

        public void Red(string message)
            => _reporter.Error(message);

        public void Gray(string message)
            => _reporter.Verbose(message);

        public void PrintFileActionMessage(string fileName, FileAction fileAction)
        {
            switch (fileAction)
            {
                case FileAction.Created:
                    this.Green($"{fileName} (created)");
                    break;

                case FileAction.Modified:
                    this.Yellow($"{fileName} (modified)");
                    break;

                case FileAction.UpToDate:
                    this.Gray($"{fileName} (up to date)");
                    break;
            }
        }

        public void PrintCorruptConfigMessage(string message)
        {
            this.Warn($"Looks like your spocr.json config file is corrupt: {message}");
        }
    }
}