using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HamCallsign;
using NAudio.Wave;
using RttyDecoder.Audio;
using RttyDecoder.Core;
using RttyDecoder.Core.DeepRtty;

namespace RttyDecoder.Benchmarks;

public sealed record CorpusCase(string Id, string File, double MarkHz, string? Reference,
    string? ExpectedCall, int? ExpectedCallCount, string? Sha256, int GenerateNoiseSeconds = 0);
public sealed record Corpus(CorpusCase[] Cases);
public sealed record DecodeResult(string Case, string Profile, string Revision, string Runtime, string OS,
    string AudioSha256, string? ReferenceSha256, RttySettings Settings, double AudioSeconds,
    int? Errors, double? Cer, int? CompleteMessagesFound, int ExpectedCallHits, int? ExpectedCallCount,
    int OtherCallCandidates, int? NoiseCharacters, double? NoiseCharactersPerMinute,
    double LoadSeconds, double DecodeSeconds, double Speed, double CpuSeconds, double PeakWorkingSetMiB,
    double? FirstTextWallSeconds, double? FirstTextAtFedAudioSeconds, double PeakPendingAudioSeconds,
    int Overruns, string TextFile);

public static class Program
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    public static readonly string[] Profiles = ["Chebyshev", "MatchedFilter", "DeepRTTY", "Chebyshev-ATC", "Chebyshev-NB-ATC"];

    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            if (args.Length == 0) throw new ArgumentException("run|case|fsk-timing --output dossier [--input dossier --manifest fichier --revision SHA --only sample]");
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--")) throw new ArgumentException("Optiune incompleta: " + args[i]);
                options.Add(args[i][2..], args[i + 1]);
            }
            string output = Path.GetFullPath(options.GetValueOrDefault("output", "benchmark-results"));
            Directory.CreateDirectory(output);
            if (args[0] == "fsk-timing") { await FskTiming.Run(output); return 0; }
            string manifest = Path.GetFullPath(options.GetValueOrDefault("manifest", "tools/RttyDecoder.Benchmarks/manifest.json"));
            var corpus = JsonSerializer.Deserialize<Corpus>(await File.ReadAllTextAsync(manifest), Json)
                ?? throw new InvalidDataException("Manifest gol.");
            string input = Path.GetFullPath(options.GetValueOrDefault("input", "recordings-data"));
            Directory.CreateDirectory(input);
            string revision = options.GetValueOrDefault("revision", "unknown");
            if (args[0] == "case")
            {
                var item = corpus.Cases.Single(c => c.Id == options["id"]);
                var result = await Decode(item, options["profile"], input, output, revision);
                await File.WriteAllTextAsync(Path.Combine(output, item.Id + "-" + options["profile"] + ".json"), JsonSerializer.Serialize(result, Json));
                Console.WriteLine("RESULT " + JsonSerializer.Serialize(result, Json));
            }
            else if (args[0] == "run")
            {
                var results = new List<DecodeResult>();
                foreach (var item in corpus.Cases.Where(c => !options.TryGetValue("only", out var only) || c.Id == only))
                {
                    if (item.GenerateNoiseSeconds > 0) GenerateNoise(Path.Combine(input, item.File), item.GenerateNoiseSeconds);
                    foreach (string profile in item.Id == "noise" ? Profiles.Concat(["Chebyshev-no-SQL", "MatchedFilter-no-SQL"]) : Profiles)
                    {
                        // Un proces per combinatie: varful de memorie nu mosteneste modelul cazului precedent.
                        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
                        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                        foreach (var arg in new[] { "case", "--manifest", manifest, "--input", input, "--output", output,
                            "--revision", revision, "--id", item.Id, "--profile", profile }) start.ArgumentList.Add(arg);
                        using var child = Process.Start(start) ?? throw new InvalidOperationException("Nu pot porni benchmark-ul.");
                        var stdout = child.StandardOutput.ReadToEndAsync();
                        var stderr = child.StandardError.ReadToEndAsync();
                        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(15)); }
                        catch { child.Kill(entireProcessTree: true); throw; }
                        string log = await stdout;
                        if (child.ExitCode != 0) throw new InvalidOperationException(log + await stderr);
                        Console.Write(log);
                        var result = JsonSerializer.Deserialize<DecodeResult>(await File.ReadAllTextAsync(Path.Combine(output, item.Id + "-" + profile + ".json")), Json)!;
                        results.Add(result);
                    }
                }
                if (results.Count == 0) throw new ArgumentException("Niciun caz selectat.");
                await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, Json));
                string report = Reports.Decode(results);
                await File.WriteAllTextAsync(Path.Combine(output, "decode-report.md"), report);
                Console.WriteLine("DECODE_REPORT_BEGIN\n" + report + "DECODE_REPORT_END");
            }
            else throw new ArgumentException("Comanda necunoscuta: " + args[0]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    public static RttySettings Settings(string profile, double markHz)
    {
        var s = new RttySettings { MarkFrequencyHz = markHz, UsTty = true };
        switch (profile)
        {
            case "Chebyshev": break;
            case "MatchedFilter": s.Decoder = RttyDecoderType.MatchedFilter; break;
            case "DeepRTTY": s.Decoder = RttyDecoderType.DeepRtty; break;
            case "Chebyshev-ATC": s.Atc = true; break;
            case "Chebyshev-NB-ATC": s.Atc = true; s.NoiseBlanker = true; break;
            case "Chebyshev-no-SQL": s.Squelch = false; break;
            case "MatchedFilter-no-SQL": s.Decoder = RttyDecoderType.MatchedFilter; s.Squelch = false; break;
            default: throw new ArgumentException("Profil necunoscut: " + profile);
        }
        return s;
    }

    public static string? Reference(string? kind) => kind switch
    {
        "numbered-w6iwi" => string.Join("\n", Enumerable.Range(0, 100).Select(i => $"{i:00} THE QUICK BROWN FOX JUMPED OVER THE LAZY DOG'S BACK. TEST DE W6IWI")),
        "sample" => string.Concat(Enumerable.Repeat("CQ CQ CQ DE YO9VA YO9VA YO9VA PSE K\nTHE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 1234567890\nUR RST 599 599 NAME EUGEN QTH PLOIESTI KN 35 73 SK\n", 2)),
        "empty" => "", null => null,
        _ => throw new ArgumentException("Referinta necunoscuta: " + kind)
    };

    private static async Task<DecodeResult> Decode(CorpusCase item, string profile, string input, string output, string revision)
    {
        string path = System.IO.File.Exists(item.File) ? Path.GetFullPath(item.File) : Path.Combine(input, item.File);
        string hash;
        using (var file = System.IO.File.OpenRead(path)) hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        if (item.Sha256 != null && !hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA256 gresit: " + path);
        var settings = Settings(profile, item.MarkHz);
        var load = Stopwatch.StartNew();
        await using var receiver = new RttyReceiver();
        receiver.ApplySettings(settings);
        await receiver.WaitForModelAsync();
        if (settings.Decoder == RttyDecoderType.DeepRtty && !receiver.IsDeepRttyReady) throw new InvalidOperationException("Model DeepRTTY indisponibil.");
        load.Stop();
        var text = new StringBuilder();
        long frames = 0;
        double? firstWall = null, firstAudio = null;
        double peakQueue = 0;
        var clock = new Stopwatch();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        using var source = new WavFileSource(path, realTime: false, AudioChannel.Left);
        var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.TextReceived += value =>
        {
            lock (text)
            {
                firstWall ??= clock.Elapsed.TotalSeconds;
                firstAudio ??= Interlocked.Read(ref frames) / (double)source.SampleRate;
                text.Append(value);
            }
        };
        source.SamplesAvailable += (buffer, count, rate) =>
        {
            Interlocked.Add(ref frames, count);
            receiver.Process(buffer.AsSpan(0, count), rate, waitForDecoder: true);
            peakQueue = Math.Max(peakQueue, receiver.DeepRttyQueueStatus.PendingAudioSeconds);
        };
        source.Stopped += error =>
        {
            try
            {
                if (error == null && !receiver.TryFlush(TimeSpan.FromMinutes(10))) error = new TimeoutException("Flush incomplet.");
            }
            catch (Exception failure) { error = failure; }
            done.TrySetResult(error);
        };
        receiver.Start();
        clock.Start();
        source.Start();
        Exception? failure = await done.Task.WaitAsync(TimeSpan.FromMinutes(12));
        clock.Stop();
        receiver.Stop();
        source.Stop();
        if (failure != null) throw new InvalidOperationException("Decodare esuata: " + item.Id, failure);
        var queue = receiver.DeepRttyQueueStatus;
        if (queue.Overruns != 0) throw new InvalidOperationException("Pierdere audio in benchmark-ul WAV.");
        string decoded;
        lock (text) decoded = text.ToString();
        string textFile = item.Id + "-" + profile + ".txt";
        await System.IO.File.WriteAllTextAsync(Path.Combine(output, textFile), decoded);
        string? expected = Reference(item.Reference);
        string normalized = Scoring.Normalize(decoded);
        string? reference = expected == null ? null : Scoring.Normalize(expected);
        int? errors = reference == null ? null : Scoring.Distance(reference, normalized);
        int? complete = expected == null || expected.Length == 0 ? null : expected.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => normalized.Contains(Scoring.Normalize(line), StringComparison.Ordinal));
        var recognized = CallsignRecognizer.CreateDefault().RecognizeAll(decoded).ToArray();
        process.Refresh();
        return new(item.Id, profile, revision, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            hash, reference == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference))).ToLowerInvariant(),
            receiver.Settings, source.Duration.TotalSeconds, errors,
            reference is { Length: > 0 } ? errors / (double)reference.Length : null, complete,
            item.ExpectedCall == null ? 0 : Scoring.CountCall(decoded, item.ExpectedCall), item.ExpectedCallCount,
            recognized.Count(m => m.Callsign.Full != item.ExpectedCall), expected == "" ? normalized.Length : null,
            expected == "" ? normalized.Length / source.Duration.TotalMinutes : null,
            load.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds, source.Duration.TotalSeconds / Math.Max(clock.Elapsed.TotalSeconds, 1e-9),
            (process.TotalProcessorTime - cpu).TotalSeconds, process.PeakWorkingSet64 / 1048576.0, firstWall, firstAudio,
            peakQueue, queue.Overruns, textFile);
    }

    public static void GenerateNoise(string path, int seconds)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1));
        uint seed = 12345;
        for (int i = 0; i < seconds * 8000; i++)
        {
            // Somme de 12 uniformes (gaussienne approchee), PRNG entier fixe.
            double sample = 0;
            for (int j = 0; j < 12; j++) { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; sample += seed / 4294967296.0; }
            writer.WriteSample((float)((sample - 6) * 0.03));
        }
    }
}
