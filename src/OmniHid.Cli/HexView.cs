using System;
using System.Text;

namespace OmniHid.Cli
{
    /// <summary>
    /// Utility class for rendering formatted, colorized hex dumps to the console and parsing raw hex strings.
    /// Highlights non-zero payload data, changed bytes (diffs), and candidate battery percentage offsets.
    /// </summary>
    public static class HexView
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Hex Dump Rendering
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prints a formatted hex dump of the provided byte array with optional diff or candidate highlight.
        /// </summary>
        /// <param name="data">Raw byte array to dump.</param>
        /// <param name="bytesPerLine">Number of hex bytes to display per line (default: 16).</param>
        /// <param name="highlightOffset">Byte index to highlight as a diff/changed byte (-1 for none).</param>
        /// <param name="candidateOffset">Byte index to highlight as a candidate battery byte (-1 for none).</param>
        public static void PrintHexDump(byte[] data, int bytesPerLine = 16, int highlightOffset = -1, int candidateOffset = -1)
        {
            if (data == null || data.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("    (Empty data buffer)");
                Console.ResetColor();
                return;
            }

            for (int i = 0; i < data.Length; i += bytesPerLine)
            {
                // Print line byte offset: 0x0000 |
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(string.Format("    0x{0:X4} | ", i));

                // Print hex byte values
                for (int j = 0; j < bytesPerLine; j++)
                {
                    int index = i + j;
                    if (index < data.Length)
                    {
                        byte b = data[index];
                        if (index == candidateOffset)
                        {
                            // Battery candidate highlight: Green background
                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(string.Format("{0:X2} ", b));
                            Console.ResetColor();
                        }
                        else if (index == highlightOffset)
                        {
                            // Diff/changed byte highlight: Dark yellow background
                            Console.BackgroundColor = ConsoleColor.DarkYellow;
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.Write(string.Format("{0:X2} ", b));
                            Console.ResetColor();
                        }
                        else if (b != 0)
                        {
                            // Non-zero data byte: Bright white
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(string.Format("{0:X2} ", b));
                        }
                        else
                        {
                            // Zero padding byte: Dim dark gray
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("00 ");
                        }
                    }
                    else
                    {
                        Console.Write("   ");
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("| ");

                // Print ASCII representation
                for (int j = 0; j < bytesPerLine; j++)
                {
                    int index = i + j;
                    if (index < data.Length)
                    {
                        byte b = data[index];
                        char c = (b >= 32 && b <= 126) ? (char)b : '.';

                        if (index == candidateOffset)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(c);
                            Console.ResetColor();
                        }
                        else if (index == highlightOffset)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkYellow;
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.Write(c);
                            Console.ResetColor();
                        }
                        else if (b != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write(c);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(c);
                        }
                    }
                }

                Console.ResetColor();
                Console.WriteLine();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Hex String Parsing
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parses a space- or delimiter-separated hex string into a raw byte array.
        /// </summary>
        /// <param name="hex">Input string containing hexadecimal characters.</param>
        /// <returns>Decoded byte array.</returns>
        public static byte[] ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];

            string sanitized = hex.Replace("0x", "")
                                  .Replace(" ", "")
                                  .Replace(",", "")
                                  .Replace("-", "")
                                  .Replace(":", "");

            if (sanitized.Length % 2 != 0)
            {
                sanitized = "0" + sanitized;
            }

            byte[] bytes = new byte[sanitized.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(sanitized.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}
