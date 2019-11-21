using System;
using System.IO;
using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Utils
{
    public interface IConsoleReporter : IReporter
    {
        void Success(string message);
    }

    public class ColoredConsoleReporter : IConsoleReporter
    {
        private readonly object _writeLock = new object();

        public ColoredConsoleReporter(IConsole console)
            : this(console, verbose: false, quiet: false) { }

        public ColoredConsoleReporter(IConsole console, bool verbose, bool quiet)
        {
            Console = console ?? throw new ArgumentNullException(nameof(console));
            IsVerbose = verbose;
            IsQuiet = quiet;
        }

        /// <summary>
        /// The console to write to.
        /// </summary>
        protected IConsole Console { get; }

        /// <summary>
        /// Is verbose output displayed.
        /// </summary>
        public bool IsVerbose { get; set; }

        /// <summary>
        /// Is verbose output and regular output hidden.
        /// </summary>
        public bool IsQuiet { get; set; }

        /// <summary>
        /// Write a line with color.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="message"></param>
        /// <param name="foregroundColor"></param>
        /// <param name="backgroundColor"></param>
        protected virtual void WriteLine(TextWriter writer, string message, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = default)
        {
            lock (_writeLock)
            {
                if (foregroundColor.HasValue)
                {
                    Console.ForegroundColor = foregroundColor.Value;
                }

                if (backgroundColor.HasValue)
                {
                    Console.BackgroundColor = backgroundColor.Value;
                }

                writer.WriteLine(message);

                if (foregroundColor.HasValue)
                {
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// Writes a message in <see cref="ConsoleColor.Red"/> to <see cref="IConsole.Error"/>.
        /// </summary>
        /// <param name="message"></param>
        public void Error(string message)
            => WriteLine(Console.Error, message, ConsoleColor.Red);

        /// <summary>
        /// Writes a message in <see cref="ConsoleColor.Yellow"/> to <see cref="IConsole.Out"/>.
        /// </summary>
        /// <param name="message"></param>
        public void Warn(string message)
            => WriteLine(Console.Out, message, ConsoleColor.Yellow);

        public void Success(string message)
            => WriteLine(Console.Out, message, ConsoleColor.Green);

        public void Output(string message)
        {
            if (IsQuiet)
            {
                return;
            }

            WriteLine(Console.Out, message, foregroundColor: null);
        }

        /// <summary>
        /// Writes a message in <see cref="ConsoleColor.DarkGray"/> to <see cref="IConsole.Out"/>.
        /// </summary>
        /// <param name="message"></param>
        public void Verbose(string message)
        {
            if (!IsVerbose)
            {
                return;
            }

            WriteLine(Console.Out, message, ConsoleColor.DarkGray);
        }
    }
}