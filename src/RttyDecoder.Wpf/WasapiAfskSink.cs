using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RttyDecoder.Core.Tx;

using RttyDecoder.Audio;

namespace RttyDecoder.Wpf;

/// <summary>
/// Iesirea AFSK pe o placa de sunet (WASAPI shared, formatul mixerului Windows: rata si canalele
/// placii, fara reesantionare). Se deschide la PTT si se inchide la sfarsitul emisiei.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAfskSink : IAfskSink
{
    /// <summary>Bufferul WASAPI (ms): destul pentru un PC incarcat, putin pentru STOP.</summary>
    public const int LatencyMs = 60;

    private readonly string _deviceId;
    private WaveFormat? _format;
    private WasapiOut? _out;
    private MMDevice? _device;
    private volatile bool _stopping;

    /// <param name="deviceId">Id-ul WASAPI; gol = placa de redare implicita.</param>
    public WasapiAfskSink(string? deviceId) => _deviceId = deviceId ?? "";

    public event Action<Exception?>? Failed;

    public double LatencySeconds => LatencyMs / 1000.0;

    /// <summary>Citita din mixerul Windows la fiecare emisie (placa poate fi reconfigurata intre timp).</summary>
    public int SampleRate
    {
        get
        {
            using var device = OpenDevice();
            var mix = device.AudioClient.MixFormat;
            _format = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, Math.Clamp(mix.Channels, 1, 8));
            return _format.SampleRate;
        }
    }

    /// <summary>Placile de redare active (iesiri), cu cea implicita marcata.</summary>
    public static IReadOnlyList<AudioDevice> ListDevices()
    {
        using var en = new MMDeviceEnumerator();
        string? implicitId = null;
        try
        {
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            implicitId = d.ID;
        }
        catch
        {
            // fara dispozitiv implicit
        }
        var list = new List<AudioDevice>();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d) list.Add(new AudioDevice(d.ID, d.FriendlyName, d.ID == implicitId));
        }
        return list;
    }

    private MMDevice OpenDevice()
    {
        using var en = new MMDeviceEnumerator();
        if (string.IsNullOrEmpty(_deviceId)) return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            var d = en.GetDevice(_deviceId);
            if (d.State == DeviceState.Active) return d;
            d.Dispose();
        }
        catch
        {
            // placa scoasa
        }
        throw new InvalidOperationException("placa audio pentru AFSK nu este conectata (Setari transmisie)");
    }

    public void Start(AfskModulator modulator)
    {
        Stop();
        var format = _format ?? throw new InvalidOperationException("SampleRate se citeste inainte de Start");
        if (modulator.SampleRate != format.SampleRate) throw new ArgumentException("rata modulatorului difera de a placii");
        _stopping = false;
        _device = OpenDevice();
        _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, LatencyMs);
        _out.PlaybackStopped += (_, e) => { if (!_stopping) Failed?.Invoke(e.Exception); };
        _out.Init(new Provider(modulator, format));
        _out.Play();
    }

    public void Stop()
    {
        _stopping = true;
        var o = _out;
        _out = null;
        if (o != null)
        {
            try { o.Stop(); } catch { }
            o.Dispose();
        }
        _device?.Dispose();
        _device = null;
    }

    /// <summary>Tonul mono copiat pe toate canalele placii.</summary>
    private sealed class Provider : ISampleProvider
    {
        private readonly AfskModulator _modulator;
        private float[] _mono = new float[4096];

        public Provider(AfskModulator modulator, WaveFormat format)
        {
            _modulator = modulator;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int channels = WaveFormat.Channels;
            int frames = count / channels;
            if (_mono.Length < frames) _mono = new float[frames];
            _modulator.Render(_mono.AsSpan(0, frames));
            for (int i = 0, k = offset; i < frames; i++)
                for (int c = 0; c < channels; c++) buffer[k++] = _mono[i];
            return frames * channels;
        }
    }
}
