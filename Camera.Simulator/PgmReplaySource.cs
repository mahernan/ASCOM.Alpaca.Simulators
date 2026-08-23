using System;
using System.Globalization;
using System.IO;
using System.Linq;
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

    internal enum ReplaySourceMode { Normal = 0, SingleImage = 1, Directory = 2 }

    internal interface IReplaySource
    {
        int Width { get; }
        int Height { get; }
        int MaxValue { get; }
        PgmReplaySource SelectNext();
        void CompleteSelection();
        void CancelSelection();
    }

    internal sealed class SinglePgmReplaySource : IReplaySource
    {
        private readonly PgmReplaySource image;
        internal SinglePgmReplaySource(string path) => image = PgmReplaySource.Load(path);
        public int Width => image.Width;
        public int Height => image.Height;
        public int MaxValue => image.MaxValue;
        public PgmReplaySource SelectNext() => image;
        public void CompleteSelection() { }
        public void CancelSelection() { }
    }

    /// <summary>Provides P5 PGM files ordered by ordinal filename and advances only on completion.</summary>
    internal sealed class PgmDirectoryReplaySource : IReplaySource
    {
        private readonly object sync = new object();
        private readonly PgmReplaySource[] images;
        private readonly bool loop;
        private int nextIndex;
        private bool selectionActive;

        internal PgmDirectoryReplaySource(string directoryPath, bool loop)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new InvalidDataException("A replay PGM directory path is required.");
            if (!Directory.Exists(directoryPath))
                throw new InvalidDataException($"Replay PGM directory '{directoryPath}' does not exist.");

            string[] paths;
            try
            {
                paths = Directory.GetFiles(directoryPath, "*")
                    .Where(path => string.Equals(Path.GetExtension(path), ".pgm", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Unable to enumerate replay PGM directory '{directoryPath}': {ex.Message}", ex);
            }
            if (paths.Length == 0)
                throw new InvalidDataException($"Replay PGM directory '{directoryPath}' contains no .pgm files.");

            images = paths.Select(PgmReplaySource.Load).ToArray();
            PgmReplaySource first = images[0];
            for (int index = 1; index < images.Length; index++)
                if (images[index].Width != first.Width || images[index].Height != first.Height || images[index].MaxValue != first.MaxValue)
                    throw new InvalidDataException($"Replay PGM '{paths[index]}' is incompatible with '{paths[0]}'; width, height, and maxval must match.");
            this.loop = loop;
        }

        public int Width => images[0].Width;
        public int Height => images[0].Height;
        public int MaxValue => images[0].MaxValue;

        public PgmReplaySource SelectNext()
        {
            lock (sync)
            {
                if (selectionActive) throw new ASCOM.InvalidOperationException("A replay exposure is already in progress.");
                if (nextIndex >= images.Length) throw new ASCOM.InvalidOperationException("No replay images remain in the configured directory.");
                selectionActive = true;
                return images[nextIndex];
            }
        }

        public void CompleteSelection()
        {
            lock (sync)
            {
                if (!selectionActive) return;
                nextIndex++;
                if (loop && nextIndex == images.Length) nextIndex = 0;
                selectionActive = false;
            }
        }

        public void CancelSelection()
        {
            lock (sync) selectionActive = false;
        }
    }
}
