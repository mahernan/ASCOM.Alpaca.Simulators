using ASCOM.Common.DeviceInterfaces;
using ASCOM.Common.Interfaces;
using ASCOM.Simulators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using SimCamera = ASCOM.Simulators.Camera;

namespace Camera.Simulator.Tests;

public sealed class PgmReplayTests
{
    private static readonly int[] Samples = { 0, 1, 255, 256, 1000, 32767, 32768, 65534, 65535 };

    [Fact]
    public void DecoderPreserves16BitBigEndianSamplesAndOrientation()
    {
        string path = CreatePgm();
        try
        {
            PgmReplaySource image = PgmReplaySource.Load(path);
            Assert.Equal(3, image.Width);
            Assert.Equal(3, image.Height);
            Assert.Equal(65535, image.MaxValue);
            for (int index = 0; index < Samples.Length; index++)
                Assert.Equal(Samples[index], image.Pixels[index % 3, index / 3]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CameraReplaysIdenticalPixelsAcrossDifferentExposureDurations()
    {
        string path = CreatePgm();
        SimCamera camera = CreateCamera(new Dictionary<string, string>
        {
            ["UseCustomImage"] = "true", ["ImageFile"] = path, ["MinExposure"] = "0.001"
        });
        try
        {
            camera.Connected = true;
            Assert.Equal(3, camera.CameraXSize);
            Assert.Equal(3, camera.CameraYSize);
            Assert.Equal(65535, camera.MaxADU);
            Assert.Equal(SensorType.Monochrome, camera.SensorType);

            int[,] first = await Expose(camera, 0.02);
            AssertPixels(first);
            int[,] second = await Expose(camera, 0.06);
            AssertPixels(second);
            Assert.Equal(Flatten(first), Flatten(second));
        }
        finally
        {
            camera.Connected = false;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DefaultModeStillProducesAnImage()
    {
        SimCamera camera = CreateCamera(new Dictionary<string, string> { ["MinExposure"] = "0.001" });
        camera.Connected = true;
        try
        {
            int[,] image = await Expose(camera, 0.002);
            Assert.Equal(800, image.GetLength(0));
            Assert.Equal(600, image.GetLength(1));
        }
        finally { camera.Connected = false; }
    }

    [Fact]
    public void NormalModeRetainsConfiguredReplayPathWhenProfileIsSaved()
    {
        Dictionary<string, string> values = new Dictionary<string, string>
        {
            ["UseCustomImage"] = "false", ["ImageFile"] = "/tmp/future-replay.pgm"
        };
        SimCamera camera = CreateCamera(values);

        Assert.Equal("/tmp/future-replay.pgm", camera.replayImagePath);
        camera.SaveToProfile();
        Assert.Equal("/tmp/future-replay.pgm", values["ImageFile"]);
        Assert.Equal("False", values["UseCustomImage"]);
    }

    [Fact]
    public async Task DirectoryReplayUsesOrdinalOrderAdvancesSequentiallyAndLoops()
    {
        string directory = CreateDirectory(("b.pgm", 22), ("a.pgm", 11), ("c.pgm", 33));
        SimCamera camera = CreateDirectoryCamera(directory, loop: true);
        try
        {
            camera.Connected = true;
            Assert.Equal(11, (await Expose(camera, 0.002))[0, 0]);
            Assert.Equal(22, (await Expose(camera, 0.002))[0, 0]);
            Assert.Equal(33, (await Expose(camera, 0.002))[0, 0]);
            Assert.Equal(11, (await Expose(camera, 0.002))[0, 0]);
        }
        finally { camera.Connected = false; Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RepeatedReadsDoNotAdvanceAndRejectedExposureDoesNotConsumeAFile()
    {
        string directory = CreateDirectory(("a.pgm", 17), ("b.pgm", 29));
        SimCamera camera = CreateDirectoryCamera(directory, loop: true);
        try
        {
            camera.Connected = true;
            int[,] first = await Expose(camera, 0.002);
            Assert.Equal(17, first[0, 0]);
            Assert.Equal(17, Assert.IsType<int[,]>(camera.ImageArray)[0, 0]);
            Assert.Equal(17, Assert.IsType<int[,]>(camera.ImageArray)[0, 0]);
            Assert.Throws<ASCOM.InvalidValueException>(() => camera.StartExposure(99999, true));
            Assert.Equal(29, (await Expose(camera, 0.002))[0, 0]);
        }
        finally { camera.Connected = false; Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task StopAtEndRejectsNextExposureAndPreservesLastImage()
    {
        string directory = CreateDirectory(("a.pgm", 5), ("b.pgm", 9));
        SimCamera camera = CreateDirectoryCamera(directory, loop: false);
        try
        {
            camera.Connected = true;
            await Expose(camera, 0.002);
            Assert.Equal(9, (await Expose(camera, 0.002))[0, 0]);
            ASCOM.InvalidOperationException error = Assert.Throws<ASCOM.InvalidOperationException>(() => camera.StartExposure(0.002, true));
            Assert.Contains("No replay images remain", error.Message);
            Assert.Equal(9, Assert.IsType<int[,]>(camera.ImageArray)[0, 0]);
        }
        finally { camera.Connected = false; Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReconnectResetsDirectoryToFirstFile()
    {
        string directory = CreateDirectory(("a.pgm", 3), ("b.pgm", 7));
        SimCamera camera = CreateDirectoryCamera(directory, loop: true);
        try
        {
            camera.Connected = true;
            Assert.Equal(3, (await Expose(camera, 0.002))[0, 0]);
            Assert.Equal(7, (await Expose(camera, 0.002))[0, 0]);
            camera.Connected = false;
            camera.Connected = true;
            Assert.Equal(3, (await Expose(camera, 0.002))[0, 0]);
        }
        finally { camera.Connected = false; Directory.Delete(directory, true); }
    }

    [Fact]
    public void DirectoryValidationRejectsEmptyIncompatibleAndCorruptSources()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"camera-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try { Assert.Throws<InvalidDataException>(() => new PgmDirectoryReplaySource(empty, true)); }
        finally { Directory.Delete(empty, true); }

        string incompatible = CreateDirectory(("a.pgm", 1));
        WritePgm(Path.Combine(incompatible, "b.pgm"), 2, 1, 255, 2, 3);
        try { Assert.Throws<InvalidDataException>(() => new PgmDirectoryReplaySource(incompatible, true)); }
        finally { Directory.Delete(incompatible, true); }

        string corrupt = Path.Combine(Path.GetTempPath(), $"camera-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "bad.pgm"), "not a pgm");
        try { Assert.Throws<InvalidDataException>(() => new PgmDirectoryReplaySource(corrupt, true)); }
        finally { Directory.Delete(corrupt, true); }
    }

    private static async Task<int[,]> Expose(SimCamera camera, double duration)
    {
        camera.StartExposure(duration, true);
        Assert.False(camera.ImageReady);
        Assert.Equal(CameraState.Exposing, camera.CameraState);
        DateTime timeout = DateTime.UtcNow.AddSeconds(3);
        while (!camera.ImageReady && DateTime.UtcNow < timeout) await Task.Delay(2);
        Assert.True(camera.ImageReady);
        Assert.Equal(CameraState.Idle, camera.CameraState);
        return Assert.IsType<int[,]>(camera.ImageArray);
    }

    private static void AssertPixels(int[,] image)
    {
        Assert.Equal(3, image.GetLength(0));
        Assert.Equal(3, image.GetLength(1));
        for (int index = 0; index < Samples.Length; index++)
            Assert.Equal(Samples[index], image[index % 3, index / 3]);
    }

    private static int[] Flatten(int[,] image)
    {
        int[] values = new int[image.Length];
        for (int index = 0; index < values.Length; index++) values[index] = image[index % 3, index / 3];
        return values;
    }

    private static string CreatePgm()
    {
        string path = Path.Combine(Path.GetTempPath(), $"camera-replay-{Guid.NewGuid():N}.pgm");
        byte[] header = System.Text.Encoding.ASCII.GetBytes("P5\n# fidelity fixture\n3 3\n65535\n");
        byte[] data = new byte[header.Length + Samples.Length * 2];
        Buffer.BlockCopy(header, 0, data, 0, header.Length);
        for (int index = 0; index < Samples.Length; index++)
        {
            data[header.Length + index * 2] = (byte)(Samples[index] >> 8);
            data[header.Length + index * 2 + 1] = (byte)Samples[index];
        }
        File.WriteAllBytes(path, data);
        return path;
    }

    private static SimCamera CreateCamera(Dictionary<string, string> values)
    {
        IProfile profile = Proxy<IProfile>.Create((method, args) => method.Name switch
        {
            "ContainsKey" => values.ContainsKey((string)args![0]!),
            "GetValue" when args!.Length == 1 => values[(string)args[0]!],
            "GetValue" => values.TryGetValue((string)args![0]!, out string? value) ? value : (string)args[1]!,
            "WriteValue" => Write(values, (string)args![0]!, (string)args[1]!),
            _ => Default(method.ReturnType)
        });
        ILogger logger = Proxy<ILogger>.Create((method, args) => Default(method.ReturnType));
        return new SimCamera(0, logger, profile);
    }

    private static SimCamera CreateDirectoryCamera(string directory, bool loop) => CreateCamera(new Dictionary<string, string>
    {
        ["UseCustomImage"] = "true",
        ["ReplayMode"] = ((int)ReplaySourceMode.Directory).ToString(),
        ["ReplayDirectory"] = directory,
        ["ReplayLoop"] = loop.ToString(),
        ["MinExposure"] = "0.001"
    });

    private static string CreateDirectory(params (string Name, int Pixel)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"camera-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        foreach ((string name, int pixel) in files) WritePgm(Path.Combine(directory, name), 1, 1, 255, pixel);
        return directory;
    }

    private static void WritePgm(string path, int width, int height, int maxValue, params int[] pixels)
    {
        using FileStream stream = File.Create(path);
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P5\n{width} {height}\n{maxValue}\n");
        stream.Write(header);
        foreach (int pixel in pixels)
        {
            if (maxValue >= 256) stream.WriteByte((byte)(pixel >> 8));
            stream.WriteByte((byte)pixel);
        }
    }

    private static object? Write(Dictionary<string, string> values, string key, string value) { values[key] = value; return null; }
    private static object? Default(Type type) => type == typeof(void) ? null : type.IsValueType ? Activator.CreateInstance(type) : null;

    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[]?, object?> handler = null!;
        public static T Create(Func<MethodInfo, object?[]?, object?> handler)
        {
            T proxy = DispatchProxy.Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy).handler = handler;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
    }
}
