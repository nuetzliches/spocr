using System;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using SpocR.Models;
using SpocR.Serialization;
using SpocR.Utils;

namespace SpocR.Services
{
    public enum PrintFileAction
    {
        Created,
        Modified,
        UpToDate
    }

    public interface IReportService
    {
        void Output(string message);
        void Error(string message);
        void Warn(string message);
        void Verbose(string message);

        void Green(string message);
        void Yellow(string message);
        void Red(string message);
        void Gray(string message);

        // Custom 
        void PrintDryRunMessage();
        void PrintConfiguration(ConfigurationModel config);

        void PrintFileActionMessage(string fileName, PrintFileAction fileAction);
    }

    public class ReportService : IReportService
    {
        private readonly IConsoleReporter _reporter;

        public ReportService(IConsoleReporter reporter)
        {
            _reporter = reporter;
        }

        public void Output(string message)
            => _reporter.Output(message);

        public void Error(string message)
            => _reporter.Error($"ERROR: {message}");

        public void Warn(string message)
            => _reporter.Warn($"WARNING: {message}");

        public void Success(string message)
            => _reporter.Success($"SUCCESS: {message}");

        public void Verbose(string message)
            => _reporter.Verbose($"VRB: {message}");

        public void Note(string message)
            => _reporter.Warn($"NOTE: {message}");

        public void PrintDryRunMessage()
            => Note("Run with \"dry run\" means no changes were made");

        public void PrintConfiguration(ConfigurationModel config)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new SerializeContractResolver()
            };
            var json = JsonConvert.SerializeObject(config, Formatting.Indented, jsonSettings);

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

        public void PrintFileActionMessage(string fileName, PrintFileAction fileAction)
        {
            switch (fileAction)
            {
                case PrintFileAction.Created:
                    this.Green($"{fileName} (created)");
                    break;

                case PrintFileAction.Modified:
                    this.Yellow($"{fileName} (modified)");
                    break;

                case PrintFileAction.UpToDate:
                    this.Gray($"{fileName} (up to date)");
                    break;
            }
        }
    }
}