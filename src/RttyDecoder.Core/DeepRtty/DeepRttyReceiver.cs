using System.Diagnostics;

namespace RttyDecoder.Core.DeepRtty;

/// <summary>Audio inca neprocesat (nu include coada de text provizoriu) si recuperari dupa suprasarcina.</summary>
public readonly record struct DeepRttyQueueStatus(double PendingAudioSeconds, int Overruns);

/// <summary>
/// Un singur fir de inferenta pentru toata viata receptorului. STOP/CLEAR/reacordarea invalideaza
/// sesiunea curenta, inclusiv rezultatele unei inferente deja pornite; START nu o poate suprapune
/// cu alta inferenta. Audio-ul asteapta intr-un buffer limitat (implicit 24 s, la 3200 Hz).
/// Feed nu blocheaza: la suprasarcina live abandoneaza sesiunea veche si reincepe de la audio-ul
/// nou. Pentru fisiere, apelantul foloseste WaitForCapacity inainte de fiecare bloc, fara lacate
/// externe tinute, ca sa nu piarda audio. Evenimentele vin pe firul de inferenta.
/// </summary>
public sealed class DeepRttyReceiver : IDisposable
{
    public const int MaxFeedSamples = 3840; // PCM 48 kHz, 80 ms
    private const int MaxReducedSamples = MaxFeedSamples / 15;
    private const int BatchSamples = 4 * DeepRttyConst.SampleRate;
    private const double SaltAcordHz = 20;
    private readonly object _gate = new();
    private readonly RttyInference _inference;
    private readonly int _capacity;
    private readonly Queue<object> _queue = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _worker;
    private Session? _session;
    private bool _disposed;
    private int _overruns;

    private sealed class Session
    {
        public readonly RttyFrontEnd FrontEnd = new();
        public readonly DeepRttyStream Stream;
        public readonly HashSet<TaskCompletionSource<bool>> Flushes = new();
        public int BufferedSamples;
        public double LastCentre = double.NaN;
        public bool Reverse;
        public long NextPreviewMs;
        public bool ClearPreview = true;

        public Session(DeepRttyReceiver owner)
        {
            Stream = new DeepRttyStream((input, dims) =>
                owner.IsCurrent(this) ? owner._inference(input, dims) : null);
        }
    }

    public event Action<int[]>? Confirmed;
    public event Action<int[]>? Provisional;
    public event Action<Exception>? Error;

    public DeepRttyReceiver(RttyInference inference, int maxBufferedSeconds = 24)
    {
        ArgumentNullException.ThrowIfNull(inference);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBufferedSeconds, 1);
        _inference = inference;
        _capacity = checked(maxBufferedSeconds * DeepRttyConst.SampleRate);
    }

    public bool IsRunning { get { lock (_gate) return _session != null; } }

    /// <summary>Dupa Dispose, se termina cand ultima inferenta a iesit si resursele modelului pot fi eliberate.</summary>
    public Task Completion { get { lock (_gate) return _worker == null ? Task.CompletedTask : _completion.Task; } }

    public DeepRttyQueueStatus QueueStatus
    {
        get
        {
            lock (_gate) return new((_session?.BufferedSamples ?? 0) / (double)DeepRttyConst.SampleRate, _overruns);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session != null) return;
            _overruns = 0;
            _session = new Session(this);
            if (_worker == null)
            {
                _worker = new Thread(Loop) { Name = "DeepRTTY", IsBackground = true };
                _worker.Start();
            }
            Monitor.PulseAll(_gate);
        }
    }

    public void Stop()
    {
        lock (_gate) CancelSession();
    }

    // Sub _gate. Elibereaza si Flush/WaitForCapacity, chiar daca inferenta nativa inca ruleaza.
    private void CancelSession()
    {
        if (_session != null)
            foreach (var flush in _session.Flushes) flush.TrySetResult(false);
        _session = null;
        _queue.Clear();
        Monitor.PulseAll(_gate);
    }

    private Session ReplaceSession()
    {
        CancelSession();
        return _session = new Session(this);
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_session != null) ReplaceSession();
        }
    }

    /// <summary>
    /// Asteapta loc pentru un bloc Feed (maximum 3840 PCM). False = sesiunea s-a oprit/schimbat.
    /// Un singur producator audio. Nu se apeleaza cu lacatul receptorului/UI tinut.
    /// </summary>
    public bool WaitForCapacity(TimeSpan? timeout = null)
    {
        lock (_gate)
        {
            var session = _session;
            if (session == null) return false;
            long start = Stopwatch.GetTimestamp();
            while (ReferenceEquals(_session, session) && session.BufferedSamples + MaxReducedSamples > _capacity)
            {
                if (timeout is { } limit)
                {
                    TimeSpan remaining = limit - Stopwatch.GetElapsedTime(start);
                    if (remaining <= TimeSpan.Zero) return false;
                    Monitor.Wait(_gate, remaining);
                }
                else Monitor.Wait(_gate);
            }
            return ReferenceEquals(_session, session);
        }
    }

    /// <summary>Audio PCM 48 kHz; centrul este Mark +/- 85 Hz. Pentru date mari, se imparte in blocuri de 80 ms.</summary>
    public void Feed(short[] pcm, int n, double centruHz, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        if (n > pcm.Length) throw new ArgumentOutOfRangeException(nameof(n));
        for (int offset = 0; offset < n; offset += MaxFeedSamples)
        {
            int count = Math.Min(MaxFeedSamples, n - offset);
            // RttyFrontEnd primeste un array de la indexul zero.
            short[] block = offset == 0 && count == n ? pcm : pcm.AsSpan(offset, count).ToArray();
            lock (_gate)
            {
                if (_session is not { } session) return;
                if ((!double.IsNaN(session.LastCentre) && Math.Abs(centruHz - session.LastCentre) > SaltAcordHz)
                    || (!double.IsNaN(session.LastCentre) && reverse != session.Reverse))
                    session = ReplaceSession();
                if (session.BufferedSamples + (count + 14) / 15 > _capacity)
                {
                    _overruns++;
                    session = ReplaceSession();
                }
                session.LastCentre = centruHz;
                session.Reverse = reverse;
                var audio = new float[(count + 14) / 15];
                int m = session.FrontEnd.Process(block, count, centruHz, reverse, audio, 0);
                if (m == 0) continue;
                if (m != audio.Length) Array.Resize(ref audio, m);
                _queue.Enqueue(audio);
                session.BufferedSamples += m;
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Bariera pentru audio-ul deja primit. False = timeout, STOP, CLEAR, reacordare sau Dispose.</summary>
    public bool Flush(TimeSpan timeout)
    {
        TaskCompletionSource<bool> flush;
        lock (_gate)
        {
            if (_session == null) return true;
            if (Thread.CurrentThread == _worker)
                throw new InvalidOperationException("Flush nu poate astepta pe firul de inferenta.");
            flush = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.Flushes.Add(flush);
            _queue.Enqueue(flush);
            Monitor.PulseAll(_gate);
        }
        return flush.Task.Wait(timeout) && flush.Task.Result;
    }

    private bool IsCurrent(Session session)
    {
        lock (_gate) return ReferenceEquals(_session, session);
    }

    private void Publish(Session session, Action callback)
    {
        if (IsCurrent(session)) callback();
    }

    private void ReportError(Session session)
    {
        if (session.Stream.LastError is not { } error) return;
        session.Stream.LastError = null;
        Publish(session, () => Error?.Invoke(error));
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                Session session;
                float[]? audio = null;
                TaskCompletionSource<bool>? flush = null;
                bool clear;
                bool preview;
                lock (_gate)
                {
                    while (true)
                    {
                        if (_disposed) return;
                        if (_session is { } active && (_queue.Count > 0 || active.ClearPreview || clock.ElapsedMilliseconds >= active.NextPreviewMs))
                        {
                            session = active;
                            break;
                        }
                        Monitor.Wait(_gate, 100);
                    }
                    clear = session.ClearPreview;
                    session.ClearPreview = false;
                    if (_queue.TryPeek(out var first) && first is float[])
                    {
                        var batch = new List<float[]>();
                        int total = 0;
                        while (_queue.TryPeek(out var item) && item is float[] f && total + f.Length <= BatchSamples)
                        {
                            _queue.Dequeue(); batch.Add(f); total += f.Length;
                        }
                        audio = new float[total];
                        int offset = 0;
                        foreach (var block in batch) { block.CopyTo(audio, offset); offset += block.Length; }
                    }
                    else if (_queue.Count > 0) flush = (TaskCompletionSource<bool>)_queue.Dequeue();
                    preview = audio == null && flush == null && clock.ElapsedMilliseconds >= session.NextPreviewMs;
                    if (preview) session.NextPreviewMs = clock.ElapsedMilliseconds + 1000;
                }
                try
                {
                    if (clear) Publish(session, () => Provisional?.Invoke(Array.Empty<int>()));
                    if (audio != null)
                    {
                        var tokens = session.Stream.Push(audio, audio.Length);
                        if (tokens.Count > 0) Publish(session, () => Confirmed?.Invoke(tokens.Select(t => t.Index).ToArray()));
                    }
                    else if (flush != null)
                    {
                        var tokens = session.Stream.Finish();
                        if (tokens.Count > 0) Publish(session, () => Confirmed?.Invoke(tokens.Select(t => t.Index).ToArray()));
                        Publish(session, () => Provisional?.Invoke(Array.Empty<int>()));
                    }
                    else if (preview)
                    {
                        var tokens = session.Stream.Preview();
                        Publish(session, () => Provisional?.Invoke(tokens.Select(t => t.Index).ToArray()));
                    }
                    ReportError(session);
                }
                catch (Exception error)
                {
                    // O eroare a unui abonat nu trebuie sa lase producatorul/Flush blocat.
                    try { Publish(session, () => Error?.Invoke(error)); } catch { }
                    flush?.TrySetResult(false);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (audio != null) session.BufferedSamples -= audio.Length;
                        if (flush != null)
                        {
                            session.Flushes.Remove(flush);
                            flush.TrySetResult(ReferenceEquals(_session, session));
                        }
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }
        finally { _completion.TrySetResult(); }
    }

    /// <summary>Oprire fara asteptarea inferentei native; asteptati Completion inainte de a elibera modelul.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelSession();
        }
    }
}
