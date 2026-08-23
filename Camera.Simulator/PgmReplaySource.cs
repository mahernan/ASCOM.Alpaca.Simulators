using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ASCOM.Simulators
{
    /// <summary>Decodes a binary (P5) PGM without converting or scaling its samples.</summary>
    internal sealed class PgmReplaySource
    {
        private PgmReplaySource(int width, int height, int maxValue, int[,] pixels)
        {
            Width = width;
            Height = height;
            MaxValue = maxValue;
            Pixels = pixels;
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int MaxValue { get; }
        internal int[,] Pixels { get; }

        internal static PgmReplaySource Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("A replay PGM file path is required.");
            try
            {
                using FileStream stream = File.OpenRead(path);
                string magic = ReadHeaderToken(stream, "magic number");
                if (magic != "P5") throw new InvalidDataException($"Unsupported PGM format '{magic}'. Replay mode requires binary P5 PGM.");
                int width = ReadPositiveInt(stream, "width");
                int height = ReadPositiveInt(stream, "height");
                int maxValue = ReadPositiveInt(stream, "maxval");
                if (maxValue > ushort.MaxValue) throw new InvalidDataException("PGM maxval must be between 1 and 65535.");

                int[,] pixels = new int[width, height];
                bool twoBytes = maxValue >= 256;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int high = stream.ReadByte();
                        if (high < 0) throw new InvalidDataException("The PGM pixel data is shorter than its declared dimensions.");
                        int value = high;
                        if (twoBytes)
                        {
                            int low = stream.ReadByte();
                            if (low < 0) throw new InvalidDataException("The PGM pixel data is shorter than its declared dimensions.");
                            value = (high << 8) | low;
                        }
                        if (value > maxValue) throw new InvalidDataException($"PGM sample {value} exceeds maxval {maxValue}.");
                        pixels[x, y] = value;
                    }
                return new PgmReplaySource(width, height, maxValue, pixels);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Unable to load replay PGM '{path}': {ex.Message}", ex);
            }
        }

        private static int ReadPositiveInt(Stream stream, string name)
        {
            string token = ReadHeaderToken(stream, name);
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
                throw new InvalidDataException($"The PGM {name} '{token}' is invalid.");
            return value;
        }

        private static string ReadHeaderToken(Stream stream, string name)
        {
            int current;
            do
            {
                current = stream.ReadByte();
                if (current == '#') do current = stream.ReadByte(); while (current >= 0 && current != '\n' && current != '\r');
            } while (current >= 0 && IsWhitespace(current));
            if (current < 0) throw new InvalidDataException($"The PGM header is missing its {name}.");

            StringBuilder token = new StringBuilder();
            while (current >= 0 && !IsWhitespace(current))
            {
                token.Append((char)current);
                current = stream.ReadByte();
            }
            if (current == '\r' && stream.CanSeek && stream.Position < stream.Length)
            {
                int next = stream.ReadByte();
                if (next != '\n') stream.Position--;
            }
            return token.ToString();
        }

        private static bool IsWhitespace(int value) => value is ' ' or '\t' or '\r' or '\n' or '\f';
    }
}
