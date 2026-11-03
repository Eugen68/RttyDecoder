using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RttyDecoder.Audio;

/// <summary>Un dispozitiv de intrare audio (placa de sunet, interfata radio USB, cablu virtual).</summary>
public sealed record AudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? Name + " (implicit)" : Name;
}

/// <summary>
/// Captura de la o placa de sunet prin WASAPI (mod partajat). Rata e cea a dispozitivului (de
/// regula 48 kHz sau 44.1 kHz) - receptorul reesantioneaza el la 48 kHz.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiInputSource : IAudioSource
{
    private readonly string? _deviceId;
    private readonly AudioChannel _channel;
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private float[] _interleaved = Array.Empty<float>();
    private float[] _mono = Array.Empty<float>();
    private int _channels;
    private Func<byte[], int, int, Span<float>, int>? _convert;

    public WasapiInputSource(string? deviceId, AudioChannel channel = AudioChannel.Left)
    {
        _deviceId = deviceId;
        _channel = channel;
        Name = "?";
    }

    public string Name { get; private set; }

    public int SampleRate { get; private set; }

    public event Action<float[], int, int>? SamplesAvailable;

    public event Action<Exception?>? Stopped;

    /// <summary>Dispozitivele de intrare active.</summary>
    public static IReadOnlyList<AudioDevice> ListDevices()
    {
        using var en = new MMDeviceEnumerator();
        string? implicitId = null;
        try
        {
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            implicitId = d.ID;
        }
        catch
        {
            // fara dispozitiv implicit
        }
        var list = new List<AudioDevice>();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (d) list.Add(new AudioDevice(d.ID, d.FriendlyName, d.ID == implicitId));
        }
        return list;
    }

    public void Start()
    {
        if (_capture != null) return;
        using var en = new MMDeviceEnumerator();
        _device = _deviceId != null ? en.GetDevice(_deviceId) : en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        Name = _device.FriendlyName;
        _capture = new WasapiCapture(_device, true, 50);
        var fmt = _capture.WaveFormat;
        SampleRate = fmt.SampleRate;
        _channels = fmt.Channels;
        _convert = Converter(fmt);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_convert == null || e.BytesRecorded == 0) return;
        int maxSamples = e.BytesRecorded; // margine larga (>= numarul de esantioane pentru orice format)
        if (_interleaved.Length < maxSamples) _interleaved = new float[maxSamples];
        int n = _convert(e.Buffer, 0, e.BytesRecorded, _interleaved);
        if (_mono.Length < n) _mono = new float[n];
        int frames = Downmix.ToMono(_interleaved.AsSpan(0, n), _channels, _channel, _mono);
        SamplesAvailable?.Invoke(_mono, frames, SampleRate);
    }

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private static Func<byte[], int, int, Span<float>, int> Converter(WaveFormat fmt)
    {
        var enc = fmt.Encoding == WaveFormatEncoding.Extensible
            ? ((fmt as WaveFormatExtensible)?.SubFormat == IeeeFloatSubFormat
                ? WaveFormatEncoding.IeeeFloat
                : WaveFormatEncoding.Pcm)
            : fmt.Encoding;

        if (enc == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            return (buf, off, len, dst) =>
            {
                var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buf.AsSpan(off, len));
                src.CopyTo(dst);
                return src.Length;
            };
        }
        return fmt.BitsPerSample switch
        {
            16 => (buf, off, len, dst) =>
            {
                var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(buf.AsSpan(off, len));
                for (int i = 0; i < src.Length; i++) dst[i] = src[i] / 32768f;
                return src.Length;
            },
            24 => (buf, off, len, dst) =>
            {
                int n = len / 3;
                for (int i = 0; i < n; i++)
                {
                    int p = off + 3 * i;
                    int v = (buf[p] << 8) | (buf[p + 1] << 16) | (buf[p + 2] << 24);
                    dst[i] = (v >> 8) / 8388608f;
                }
                return n;
            },
            32 => (buf, off, len, dst) =>
            {
                var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(buf.AsSpan(off, len));
                for (int i = 0; i < src.Length; i++) dst[i] = src[i] / 2147483648f;
                return src.Length;
            },
            _ => throw new NotSupportedException($"Format audio nesuportat: {fmt}"),
        };
    }

    public void Dispose()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // deja oprita
        }
        _capture?.Dispose();
        _capture = null;
        _device?.Dispose();
        _device = null;
    }
}
