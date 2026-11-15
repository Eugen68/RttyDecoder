using System.Globalization;

namespace RttyDecoder.Wpf;

/// <summary>Regulile orei din terminal (fara WPF, testabile).</summary>
public static class TerminalStamps
{
    /// <summary>Ora periodica la :00 si :30 din fiecare minut.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    /// <summary>Cu trafic, ora periodica asteapta o pauza atat de lunga...</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

    /// <summary>...dar cel mult atat dupa :00 / :30; apoi trece oricum pe rand nou.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

    /// <summary>O ora pusa (frecventa noua, emisie) cu mai putin de atat inainte de :00 / :30 o inlocuieste pe cea periodica.</summary>
    public static readonly TimeSpan RecentStamp = TimeSpan.FromSeconds(15);

    /// <summary>Sub atat, o schimbare de acord nu pune ora cu frecventa (click-uri mici, A-TUNE pe acelasi semnal).</summary>
    public const double MinFrequencyChangeHz = 10;

    /// <summary>
    /// Dupa o schimbare de acord: ora cu frecventa doar daca Mark-ul s-a mutat cu cel putin 10 Hz fata
    /// de ultima ora cu frecventa (<paramref name="lastHz"/> null = prima). Frecventa e cea RF a Mark-ului
    /// sau, fara statie, tonul audio Mark.
    /// </summary>
    public static bool FrequencyChanged(double? lastHz, double nowHz) =>
        lastHz is not { } last || Math.Abs(nowHz - last) >= MinFrequencyChangeHz;

    /// <summary>Ultimul :00 sau :30 cel mult egal cu <paramref name="utc"/>.</summary>
    public static DateTime LastBoundary(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % Period.Ticks, utc.Kind);

    /// <summary>
    /// Textul decodat care vine acum (<paramref name="now"/>) trebuie sa inceapa cu ora periodica: a trecut
    /// un :00 / :30 fara ora (nici una pusa cu mai putin de 15 s inainte de el), iar inaintea textului a
    /// fost o pauza de cel putin 3 s sau au trecut deja 10 s de la :00 / :30.
    /// </summary>
    public static bool PeriodicDue(DateTime now, DateTime lastText, DateTime lastStamp)
    {
        var boundary = LastBoundary(now);
        if (lastStamp >= boundary - RecentStamp) return false;
        return now - lastText >= Pause || now - boundary >= MaxWait;
    }

    /// <summary><c>[12:34:56]:</c> sau, cu frecventa, <c>[12:34:56] [3505.20]:</c>.</summary>
    public static string Header(DateTime utc, string? frequency) =>
        string.IsNullOrEmpty(frequency)
            ? $"[{utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}]:"
            : $"[{utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] [{frequency}]:";
}
