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

    /// <summary>
    /// Acordul fiecarei configuratii cu DeepRTTY pe acelasi caz (Levenshtein / lungimea textului DeepRTTY).
    /// Pentru cazurile fara transcriere (inregistrari de concurs): DeepRTTY nu e adevarul, doar cel mai
    /// bun decodor masurat; un acord mai mare la clasice = mai aproape de el.
    /// </summary>
    public static string DeepAgreement(IReadOnlyList<DecodeResult> results, Func<DecodeResult, string> text)
    {
        var report = new StringBuilder("\n## Acord cu DeepRTTY\n\n");
        report.AppendLine("Diferenta fata de textul DeepRTTY al aceluiasi caz (Levenshtein / lungimea textului DeepRTTY, dupa normalizare). Nu este CER: DeepRTTY nu este o transcriere verificata, doar cel mai bun decodor masurat. Include textul scos din zgomot cand statia de pe pereche nu emite.\n");
        report.AppendLine("| Caz | Configuratie | Diferenta fata de DeepRTTY | Caractere DeepRTTY |\n|---|---|---:|---:|");
        foreach (var group in results.GroupBy(r => r.Case))
        {
            if (group.FirstOrDefault(r => r.Profile == "DeepRTTY") is not { } deep) continue;
            string reference = Scoring.Normalize(text(deep));
            if (reference.Length == 0) continue;
            foreach (var r in group.Where(r => r.Profile != "DeepRTTY"))
            {
                double diff = (double)Scoring.Distance(reference, Scoring.Normalize(text(r))) / reference.Length;
                report.AppendLine($"| {r.Case} | {r.Profile} | {diff.ToString("P1", CultureInfo.InvariantCulture)} | {reference.Length} |");
            }
        }
        return report.ToString();
    }

    private static string Number(double? number) => number?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a";
}
