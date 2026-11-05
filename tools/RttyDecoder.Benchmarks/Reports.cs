using System.Globalization;
using System.Text;

namespace RttyDecoder.Benchmarks;

public static class Reports
{
    public static string Decode(IReadOnlyList<DecodeResult> results)
    {
        var report = new StringBuilder("# Benchmark decodare\n\n");
        report.AppendLine($"Commit: `{results[0].Revision}`. {results[0].Runtime}; {results[0].OS}.");
        report.AppendLine("\nCER = Levenshtein dupa majuscule si uniformizarea whitespace; punctuatia se pastreaza. Include textul suplimentar dintre mesaje. CER indisponibil = lipseste o transcriere completa verificata.\n");
        report.AppendLine("| Caz | Configuratie | CER | Mesaje integrale gasite | Indicativ corect (aparitii) | Alte candidate | Zgomot: caractere/min | Viteza x timp real | Memorie maxima MiB | Primul text: s audio alimentat | Coada maxima s |\n|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in results)
            report.AppendLine($"| {r.Case} | {r.Profile} | {(r.Cer is { } cer ? cer.ToString("P2", CultureInfo.InvariantCulture) : "n/a")} | {Number(r.CompleteMessagesFound)} | {r.ExpectedCallHits}{(r.ExpectedCallCount is { } count ? " / " + count : "")} | {r.OtherCallCandidates} | {Number(r.NoiseCharactersPerMinute)} | {r.Speed:0.0} | {r.PeakWorkingSetMiB:0.0} | {Number(r.FirstTextAtFedAudioSeconds)} | {r.PeakPendingAudioSeconds:0.00} |");
        report.AppendLine("\nMemoria include incarcarea modelului si scoring-ul; fiecare combinatie ruleaza intr-un proces nou. Viteza exclude incarcarea modelului. Timpii de la primul text sunt praguri observate la alimentare accelerata, nu latenta pe caracter. Coada este esantionata dupa callback-urile audio.\n");
        report.AppendLine("Setarile exacte, hash-urile audio/referinta, timpii CPU/incarcare, primul text in timp de ceas si textul integral sunt in fisierele JSON/TXT din artefact. Alte candidate = forme recunoscute de HamCallsign care nu coincid cu indicativul asteptat; nu sunt confirmari ale unor statii reale.\n");
        return report.ToString();
    }

    private static string Number(double? number) => number?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a";
}
