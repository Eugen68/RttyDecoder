using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RttyDecoder.Core.Tx;

namespace RttyDecoder.Benchmarks;

public sealed record TimingResult(string Load, int BusyThreads, int Intervals, double MedianAbsoluteErrorMs,
    double P95AbsoluteErrorMs, double MaxAbsoluteErrorMs, double MaxPositiveErrorMs, double MedianBitMs,
    string OS, string Runtime);

public static class FskTiming
{
    private sealed class Output : IFskOutput
    {
        public readonly List<double> Times = new();
        public void SetPtt(bool on) { }
        public void SetLine(bool active) => Times.Add(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    public static async Task Run(string output)
    {
        var results = new List<TimingResult>();
        foreach (bool load in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource();
            int workers = load ? Math.Min(Environment.ProcessorCount, 4) : 0;
            var busy = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
            {
                double value = 1;
                while (!cancel.IsCancellationRequested)
                    for (int i = 0; i < 1000; i++) value = Math.Sqrt(value + 1.001);
                GC.KeepAlive(value);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            var recorder = new Output();
            using var tx = new RttyFskTransmitter(recorder)
            {
                Settings = new RttyTxSettings { PreambleLtrs = 0, AutoCrLf = false, TailMs = 0, MaxTxSeconds = 30 }
            };
            var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            tx.Stopped += reason => done.TrySetResult(reason);
            const int chars = 48;
            try
            {
                await Task.Delay(200);
                tx.Send(string.Concat(Enumerable.Repeat("RY", chars / 2)));
                string? reason = await done.Task.WaitAsync(TimeSpan.FromSeconds(40));
                if (reason != null) throw new InvalidOperationException("FSK timing: " + reason);
                if (recorder.Times.Count < chars * 7 + 2) throw new InvalidDataException("Prea putine esantioane FSK.");
                double bit = 1.0 / RttyTxSettings.Baud;
                var errors = new List<double>();
                var bits = new List<double>();
                var rows = new StringBuilder("interval,expected_ms,actual_ms,error_ms\n");
                for (int i = 1; i <= chars * 7; i++)
                {
                    double expected = (i % 7 == 0 ? 1.5 : 1) * bit;
                    double actual = recorder.Times[i + 1] - recorder.Times[i];
                    errors.Add((actual - expected) * 1000);
                    if (i % 7 != 0) bits.Add(actual * 1000);
                    rows.AppendLine($"{i},{expected * 1000:R},{actual * 1000:R},{(actual - expected) * 1000:R}");
                }
                string name = load ? "cpu-load" : "idle";
                await File.WriteAllTextAsync(Path.Combine(output, "fsk-" + name + ".csv"), rows.ToString());
                results.Add(new(name, workers, errors.Count, Scoring.Percentile(errors.Select(Math.Abs), 0.5),
                    Scoring.Percentile(errors.Select(Math.Abs), 0.95), errors.Max(Math.Abs), errors.Max(),
                    Scoring.Percentile(bits, 0.5), System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription));
            }
            finally { cancel.Cancel(); await Task.WhenAll(busy); }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "fsk-timing.json"), JsonSerializer.Serialize(results, Program.Json));
        var report = new StringBuilder("# FSK: temporizare software\n\n");
        report.AppendLine($"{results[0].OS}; {results[0].Runtime}. Bit nominal: {1000 / RttyTxSettings.Baud:0.000} ms; 1.5 biti de stop. 48 caractere RY pe fiecare proba.\n");
        report.AppendLine("| Sarcina | Fire CPU ocupate | Intervale | Bit median ms | Eroare absoluta mediana ms | P95 ms | Max ms |\n|---|---:|---:|---:|---:|---:|---:|");
        foreach (var r in results) report.AppendLine($"| {r.Load} | {r.BusyThreads} | {r.Intervals} | {r.MedianBitMs:0.000} | {r.MedianAbsoluteErrorMs:0.000} | {r.P95AbsoluteErrorMs:0.000} | {r.MaxAbsoluteErrorMs:0.000} |");
        report.AppendLine("\nMasurare la intrarea in IFskOutput.SetLine, fara port serial, PTT fizic sau emisie radio. Nu masoara jitter-ul USB/driverului/pinului. Runnerul CI este partajat; rezultatele sunt diagnostic, nu limita garantata si nu blocheaza build-ul pe un prag de jitter. CSV-urile pastreaza fiecare interval.\n");
        await File.WriteAllTextAsync(Path.Combine(output, "fsk-report.md"), report.ToString());
        Console.WriteLine("FSK_REPORT_BEGIN\n" + report + "FSK_REPORT_END");
    }
}
