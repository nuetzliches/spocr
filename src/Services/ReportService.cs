using System;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using SpocR.Models;
using SpocR.Serialization;

namespace SpocR.Services
{
    public interface IReportService
    {
        void Output(string message);
        void Error(string message);
        void Warn(string message);
        void Verbose(string message);

        void Yellow(string message);
        void Red(string message);
        void Gray(string message);

        // Custom 
        void PrintDryRunMessage();
        void PrintConfiguration(ConfigurationModel config);
    }

    public class ReportService : IReportService
    {
        private readonly IReporter _reporter;

        public ReportService(IReporter reporter)
        {
            _reporter = reporter;
        }

        public void Output(string message)
        {
            _reporter.Output(message);
        }

        public void Error(string message)
        {
            _reporter.Error($"ERROR: {message}");
        }

        public void Warn(string message)
        {
            _reporter.Warn($"WARNING: {message}");
        }

        public void Verbose(string message)
        {
            _reporter.Verbose($"VRB: {message}");
        }

        public void Note(string message)
        {
            _reporter.Warn($"NOTE: {message}");
        }

        public void PrintDryRunMessage()
        {
            Note("Run with \"dry run\" no changes were made");
        }

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

        public void Yellow(string message)
        {
            _reporter.Warn(message);
        }

        public void Red(string message)
        {
            _reporter.Error(message);
        }

        public void Gray(string message)
        {
            _reporter.Verbose(message);
        }
    }
}