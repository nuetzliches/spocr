using System;
using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Services
{
    public interface IReportService
    {
        void Output(string message);
        void Error(string message);
        void Warn(string message);
        void Verbose(string message);
        
        // Custom 
        void PrintDryRunMessage();
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
    }
}