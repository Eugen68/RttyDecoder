using System.Runtime.Versioning;
using NAudio.Wave;

namespace RttyDecoder.Audio;

/// <summary>
/// Monitorizare: reda in difuzoare semnalul primit de decodor (acelasi audio mono, de la placa de
/// sunet sau din fisier). Se alimenteaza de pe firul sursei cu <see cref="Write"/>; redarea porneste
/// la primul bloc (cand se stie rata) si se refa daca rata se schimba.
///
/// Intarzierea ramane mica: ceasurile placii de intrare si ale celei de iesire nu sunt identice, deci
/// bufferul ar creste incet; peste <see cref="MaxBufferMs"/> se goleste (un mic "pocnet", rar).
/// </summary>
public sealed class AudioMonitor : IDisposable
{
    /// <summary>Peste atata audio in asteptare, bufferul se goleste.</summary>
    public const int MaxBufferMs = 400;

    private readonly Func<IWavePlayer> _playerFactory;
    private readonly object _lock = new();
    private IWavePlayer? _player;
    private BufferedWaveProvider? _buffer;
    private int _rate;
    private byte[] _bytes = Array.Empty<byte>();
    private volatile bool _enabled;
    private volatile float _volume = 0.5f;

    /// <param name="playerFactory">Iesirea audio; implicit placa de iesire implicita (WaveOut).</param>
    public AudioMonitor(Func<IWavePlayer>? playerFactory = null)
    {
        _playerFactory = playerFactory ?? DefaultPlayer;
    }

    [SupportedOSPlatform("windows")]
    private static IWavePlayer CreateWaveOut() => new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };

    private static IWavePlayer DefaultPlayer() =>
        OperatingSystem.IsWindows() ? CreateWaveOut() : throw new PlatformNotSupportedException("Monitorizarea audio merge doar pe Windows.");

    /// <summary>Redarea s-a oprit din cauza unei erori (placa de iesire lipsa / deconectata). Pe firul sursei sau al redarii.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Monitorizarea e pornita (blocurile primite se redau).</summary>
    public bool IsEnabled => _enabled;

    /// <summary>Volumul, 0..1 (se aplica pe esantioane, inainte de redare).</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Cate ori s-a golit bufferul ca sa nu creasca intarzierea (pentru diagnostic/teste).</summary>
    public int BufferResets { get; private set; }

    public void Start() => _enabled = true;

    public void Stop()
    {
        _enabled = false;
        lock (_lock) ClosePlayer();
    }

    /// <summary>Firul sursei: un bloc mono (valori +-1) la rata data. Ignorat daca monitorizarea e oprita.</summary>
    public void Write(float[] samples, int count, int sampleRate)
    {
        if (!_enabled || count <= 0) return;
        Exception? eroare = null;
        lock (_lock)
        {
            if (!_enabled) return;
            try
            {
                if (_buffer == null || _rate != sampleRate) OpenPlayer(sampleRate);
                var buffer = _buffer!;
                if (buffer.BufferedDuration.TotalMilliseconds > MaxBufferMs)
                {
                    buffer.ClearBuffer();
                    BufferResets++;
                }

                int n = count * 2;
                if (_bytes.Length < n) _bytes = new byte[n];
                float vol = _volume;
                for (int i = 0; i < count; i++)
                {
                    float v = Math.Clamp(samples[i] * vol, -1f, 1f);
                    short s = (short)Math.Round(v * 32767f);
                    _bytes[2 * i] = (byte)s;
                    _bytes[2 * i + 1] = (byte)(s >> 8);
                }
                buffer.AddSamples(_bytes, 0, n);
            }
            catch (Exception e)
            {
                _enabled = false;
                ClosePlayer();
                eroare = e;
            }
        }
        if (eroare != null) Failed?.Invoke(eroare);
    }

    private void OpenPlayer(int sampleRate)
    {
        ClosePlayer();
        var buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true, // cand nu vine nimic se reda liniste, fara sa se opreasca redarea
        };
        var player = _playerFactory();
        try
        {
            player.Init(buffer);
            player.PlaybackStopped += OnPlaybackStopped;
            player.Play();
        }
        catch
        {
            player.Dispose();
            throw;
        }
        _player = player;
        _buffer = buffer;
        _rate = sampleRate;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception == null) return;
        _enabled = false;
        lock (_lock)
        {
            if (ReferenceEquals(sender, _player)) ClosePlayer();
        }
        Failed?.Invoke(e.Exception);
    }

    private void ClosePlayer()
    {
        var p = _player;
        _player = null;
        _buffer = null;
        _rate = 0;
        if (p == null) return;
        p.PlaybackStopped -= OnPlaybackStopped;
        try
        {
            p.Stop();
        }
        catch
        {
            // deja oprit
        }
        p.Dispose();
    }

    public void Dispose() => Stop();
}
