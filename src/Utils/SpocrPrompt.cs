using System;
using System.Collections.Generic;

namespace SpocR.Utils
{
    public static class SpocrPrompt
    {
        /// <summary>
        /// Gets a <see cref="Choice"/> response from the console after displaying a <paramref name="prompt"/>
        /// </summary>
        /// <param name="prompt">The question to display on screen</param>
        /// <param name="options">The user choices</param>
        /// <returns></returns>
        public static Choice GetSelection(string prompt, List<string> options)
        {
            var currentSelectedIndex = 0;
            var answerHint = options[currentSelectedIndex];
            var result = options[currentSelectedIndex];

            Write($"{prompt} ");
            Write($"[{string.Join(", ", options)}] ", ConsoleColor.White);
            Write("(Use <tab> or <up/down> to choose)");
            Write(": ");
            Write(answerHint, ConsoleColor.Green);

            ConsoleKeyInfo keyInfo;

            keyInfo = Console.ReadKey(true);

            // user hitted enter
            while (keyInfo.Key != ConsoleKey.Enter)
            {
                // user hitted up, down, or tab
                if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
                {
                    // display next option
                    ClearInput(result.Length);

                    if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.Tab)
                    {
                        if (currentSelectedIndex > 0)
                            currentSelectedIndex--;
                        else
                            currentSelectedIndex = options.Count - 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow)
                    {
                        if (currentSelectedIndex < options.Count - 1)
                            currentSelectedIndex++;
                        else
                            currentSelectedIndex = 0;
                    }

                    // write next option to screen
                    var newOption = options[currentSelectedIndex];
                    Write(newOption, ConsoleColor.Green);

                    result = newOption;
                }

                keyInfo = Console.ReadKey(true);
            }

            return new Choice(currentSelectedIndex, result);
        }

        /// <summary>
        /// Gets a <see cref="Choice"/> response from the console after displaying a <paramref name="prompt"/>
        /// </summary>
        /// <param name="prompt">The question to display on screen</param>
        /// <param name="options">The user choices</param>
        /// <returns></returns>
        public static Choice GetSelectionMultiline(string prompt, List<string> options)
        {
            var currentSelectedIndex = 0;
            var result = options[currentSelectedIndex];

            Write($"{prompt} ");
            Write("(Use <tab> or <up/down> to choose)");
            Write(": \n\r");
            WriteOptions(options, currentSelectedIndex);

            ConsoleKeyInfo keyInfo;

            keyInfo = Console.ReadKey(true);

            // user hitted enter
            while (keyInfo.Key != ConsoleKey.Enter)
            {
                // user hitted up, down, or tab
                if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
                {
                    // display next option
                    foreach (var option in options)
                    {
                        Console.SetCursorPosition(0, Console.CursorTop - 1);
                        ClearCurrentConsoleLine();
                    }

                    if (keyInfo.Key == ConsoleKey.UpArrow)
                    {
                        if (currentSelectedIndex > 0)
                            currentSelectedIndex--;
                        else
                            currentSelectedIndex = options.Count - 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
                    {
                        if (currentSelectedIndex < options.Count - 1)
                            currentSelectedIndex++;
                        else
                            currentSelectedIndex = 0;
                    }

                    // write next option to screen
                    var newOption = options[currentSelectedIndex];
                    WriteOptions(options, currentSelectedIndex);

                    result = newOption;
                }

                keyInfo = Console.ReadKey(true);
            }

            return new Choice(currentSelectedIndex, result);
        }

        /// <summary>
        /// Gets a <see cref="bool"/> response from the console after displaying a <paramref name="prompt"/>
        /// </summary>
        /// <param name="prompt">The question to display on screen</param>
        /// <param name="options">The user choices</param>
        /// <returns></returns>
        public static bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null)
        {
            var confirmed = isDefaultConfirmed;
            var output = isDefaultConfirmed ? "yes" : "no";

            Write($"{prompt} ");
            Write("(Use <tab> or <up/down> to choose)");
            Write(": ");
            Write(output, ConsoleColor.Green);

            ConsoleKeyInfo keyInfo;

            keyInfo = Console.ReadKey(true);

            // user hitted enter
            while (keyInfo.Key != ConsoleKey.Enter)
            {
                // user hitted up, down, or tab
                if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
                {
                    // display next option
                    ClearInput(output.Length);

                    // write next option to screen
                    confirmed = !confirmed;
                    var newOption = confirmed ? "yes" : "no";
                    Write(newOption, ConsoleColor.Green);

                    output = newOption;
                }

                keyInfo = Console.ReadKey(true);
            }

            return confirmed;
        }

        private static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }

        private static void WriteOptions(List<string> options, int currentSelectedIndex)
        {

            for (var i = 0; i < options.Count; i++)
            {
                if (i == currentSelectedIndex)
                {
                    var output = $"> {options[i]}{Environment.NewLine}";
                    Write(output, ConsoleColor.Green);
                }
                else
                {
                    var output = $"  {options[i]}{Environment.NewLine}";
                    Write(output);
                }
            }
        }


        private static void ClearInput(int lengthToClear)
        {
            for (int i = 0; i < lengthToClear; i++)
            {
                Console.Write("\b" + " " + "\b");
            }
        }

        private static void Write(string value, ConsoleColor? foreground = null, ConsoleColor? background = null)
        {
            if (foreground.HasValue)
            {
                Console.ForegroundColor = foreground.Value;
            }

            if (background.HasValue)
            {
                Console.BackgroundColor = background.Value;
            }

            Console.Write(value);

            if (foreground.HasValue || background.HasValue)
            {
                Console.ResetColor();
            }
        }

        private class CursorState : IDisposable
        {
            private readonly bool _original;

            public CursorState()
            {
                try
                {
                    _original = Console.CursorVisible;
                }
                catch
                {
                    // some platforms throw System.PlatformNotSupportedException
                    // Assume the cursor should be shown
                    _original = true;
                }

                TrySetVisible(true);
            }

            private void TrySetVisible(bool visible)
            {
                try
                {
                    Console.CursorVisible = visible;
                }
                catch
                {
                    // setting cursor may fail if output is piped or permission is denied.
                }
            }

            public void Dispose()
            {
                TrySetVisible(_original);
            }
        }
    }

    /// <summary>
    /// A response chosen by the user
    /// </summary>
    public class Choice
    {
        public int Key { get; set; }
        public string Value { get; set; }

        public Choice(int key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
