using System.Diagnostics;
using NAudio.Wave;

namespace RttyDecoder.Audio;

/// <summary>
/// Citeste un fisier WAV (PCM 8/16/24/32 bit sau float, mono sau stereo, orice rata) pe un fir
/// propriu. In timp real (pentru urmarirea waterfall-ului) sau cat de repede se poate (pentru
/// decodarea unei inregistrari lungi).
/// </summary>
public sealed class WavFileSource : IAudioSource
{
    private const int ChunkMs = 50;

    private readonly string _path;
    private readonly AudioChannel _channel;
    private Thread? _thread;
    private volatile bool _stop;

    public WavFileSource(string path, bool realTime, AudioChannel channel = AudioChannel.Left)
    {
        _path = path;
        RealTime = realTime;
        _channel = channel;
        Name = Path.GetFileName(path);
        using var reader = new WaveFileReader(path);
        SampleRate = reader.WaveFormat.SampleRate;
        Channels = reader.WaveFormat.Channels;
        Duration = reader.TotalTime;
    }

    public string Name { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public TimeSpan Duration { get; }

    /// <summary>Se poate schimba si in timpul citirii.</summary>
    public bool RealTime { get; set; }

    /// <summary>Pozitia curenta (0..1), actualizata de firul de citire.</summary>
    public double Progress { get; private set; }

    public event Action<float[], int, int>? SamplesAvailable;

    public event Action<Exception?>? Stopped;

    public void Start()
    {
        if (_thread != null) return;
        _stop = false;
        _thread = new Thread(Read) { Name = "WAV " + Name, IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(2000);
        _thread = null;
    }

    private void Read()
    {
        Exception? error = null;
        try
        {
            using var reader = new WaveFileReader(_path);
            var samples = reader.ToSampleProvider();
            int channels = samples.WaveFormat.Channels;
            int framesPerChunk = Math.Max(1, SampleRate * ChunkMs / 1000);
            var interleaved = new float[framesPerChunk * channels];
            var mono = new float[framesPerChunk];
            long totalFrames = Math.Max(1, reader.SampleCount);
            long framesRead = 0;
            var ceas = Stopwatch.StartNew();
            long startOffsetFrames = 0;
            bool eraRealTime = RealTime;

            while (!_stop)
            {
                int n = samples.Read(interleaved, 0, interleaved.Length);
                if (n <= 0) break;
                int frames = Downmix.ToMono(interleaved.AsSpan(0, n), channels, _channel, mono);
                SamplesAvailable?.Invoke(mono, frames, SampleRate);
                framesRead += frames;
                Progress = Math.Min(1.0, (double)framesRead / totalFrames);

                if (RealTime != eraRealTime)
                {
                    // comutare din mers: ceasul porneste de la pozitia curenta
                    eraRealTime = RealTime;
                    ceas.Restart();
                    startOffsetFrames = framesRead;
                }
                if (RealTime)
                {
                    double tinta = (framesRead - startOffsetFrames) * 1000.0 / SampleRate;
                    double asteapta = tinta - ceas.Elapsed.TotalMilliseconds;
                    if (asteapta > 1) Thread.Sleep((int)asteapta);
                }
            }
        }
        catch (Exception e)
        {
            error = e;
        }
        Stopped?.Invoke(error);
    }

    public void Dispose() => Stop();
}
