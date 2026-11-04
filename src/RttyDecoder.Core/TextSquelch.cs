namespace RttyDecoder.Core;

/// <summary>
/// Squelch pentru textul decodorului clasic, dupa <b>ritmul caracterelor</b>. Pe un semnal RTTY real
/// caracterele vin unul dupa altul, la ~7.5 biti distanta (start + 5 + stop 1.5) - chiar si cu pauze
/// de tastare, in interiorul cuvintelor; pe zgomot decodorul "vede" caractere la intervale
/// intamplatoare. Masurat pe semnal sintetic: zgomot de banda 6% caractere in ritm, semnal continuu
/// 100%, semnal cu pauze de tastare 56% (la 0..20 dB). Bitul de stop si SNR-ul nu deosebesc zgomotul
/// (pe zgomot ~76% din caractere au stopul "valid", iar SNR-ul masurat iese chiar mai mare).
///
/// <list type="bullet">
/// <item>Deschis: caracterele in ritm ies imediat; unul in afara ritmului (primul dupa o pauza) se
/// tine pana vine urmatorul in ritm, apoi ies amandoua.</item>
/// <item>Se inchide dupa <see cref="CloseAfterBad"/> caractere la rand in afara ritmului (sau cand
/// proportia din fereastra scade sub prag). Cele tinute se pastreaza putin: daca semnalul revine in
/// <see cref="PendingFrames"/> caractere (o pauza de tastare), se scriu; daca vine doar zgomot, se
/// arunca - deci nici gunoi la sfarsitul semnalului, nici text pierdut la pauze.</item>
/// <item>Se deschide cand proportia de caractere in ritm din fereastra trece de prag si ultimele doua
/// caractere sunt in ritm (zgomotul aproape niciodata). Se scrie si sirul de dinainte de deschidere
/// - inceputul mesajului ("CQ CQ") nu se pierde.</item>
/// </list>
/// Afecteaza doar textul afisat, nu decodarea. Nu e thread-safe.
/// </summary>
public sealed class TextSquelch
{
    /// <summary>Caracterele dupa care se judeca.</summary>
    public const int Window = 16;

    /// <summary>Minimul de caractere in fereastra ca sa se poata deschide.</summary>
    public const int MinFrames = 6;

    /// <summary>Histerezis: se inchide abia la prag - atat.</summary>
    public const double Hysteresis = 0.1;

    /// <summary>Distanta dintre inceputurile a doua caractere consecutive (biti): 7.5, cu toleranta.</summary>
    public const double RhythmMinBits = 7.0, RhythmMaxBits = 8.2;

    /// <summary>Atatea caractere la rand in afara ritmului inchid squelch-ul.</summary>
    public const int CloseAfterBad = 4;

    /// <summary>Cat se pastreaza caracterele tinute la inchidere, in caractere decodate dupa ea.</summary>
    public const int PendingFrames = 12;

    /// <summary>
    /// "Zgomot clar": atatea caractere la rand in afara ritmului. De aici incolo se tine textul pana
    /// la deschidere (pe zgomot se atinge imediat; pe un semnal tastat cu pauze aproape niciodata -
    /// cu ~45% caractere in afara ritmului, sansa e ~0.03%).
    /// </summary>
    public const int NoiseRun = 10;

    private readonly bool[] _window = new bool[Window];
    private int _pos;
    private int _count;
    private int _good;
    private int _badRun;
    private double _lastStart = double.NaN;

    private readonly List<int> _chain = new();     // sirul curent: primul caracter (dupa o rupere) + cele in ritm
    private int _chainGood;                        // cate din sir sunt in ritm
    private readonly List<int> _suspect = new();   // deschis: caractere in afara ritmului, inca nedecise
    private readonly List<int> _pending = new();   // inchis: cele tinute la inchidere
    private readonly List<int> _recent = new();    // inchis: tot ce a venit de la ultima serie clara de zgomot
    private int _sinceClose = int.MaxValue;

    /// <param name="emit">Codurile lasate sa treaca, in ordine.</param>
    public TextSquelch(Action<int> emit) => Emit = emit;

    private Action<int> Emit { get; }

    /// <summary>Pornit/oprit (oprit = tot ce e valid trece, ca inainte).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pragul: proportia minima de caractere in ritm (0.1 .. 0.8).</summary>
    public double Threshold { get; set; } = 0.35;

    /// <summary>Squelch-ul e deschis (textul iese).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Proportia curenta de caractere in ritm (0..1), pentru afisare.</summary>
    public double Quality => _count == 0 ? 0 : (double)_good / _count;

    /// <summary>
    /// Un caracter terminat: <paramref name="valid"/> = bitul de stop a fost Mark,
    /// <paramref name="startBits"/> = inceputul lui, in biti (de la un reper oarecare).
    /// </summary>
    public void Frame(bool valid, int code, double startBits)
    {
        bool inRhythm = !double.IsNaN(_lastStart)
                        && startBits - _lastStart >= RhythmMinBits
                        && startBits - _lastStart <= RhythmMaxBits;
        _lastStart = startBits;

        if (!Enabled)
        {
            if (valid) Emit(code);
            return;
        }

        bool good = valid && inRhythm;
        if (_count == Window)
        {
            if (_window[_pos]) _good--;
        }
        else
        {
            _count++;
        }
        _window[_pos] = good;
        if (good) _good++;
        _pos = (_pos + 1) % Window;
        _badRun = good ? 0 : _badRun + 1;

        // sirul curent
        if (!valid)
        {
            _chain.Clear();
            _chainGood = 0;
        }
        else if (!inRhythm)
        {
            _chain.Clear();
            _chain.Add(code);
            _chainGood = 0;
        }
        else
        {
            _chain.Add(code);
            _chainGood++;
            if (_chain.Count > 4 * Window) _chain.RemoveAt(0);
        }

        // inchis: tot ce vine de la ultima serie de cel putin NoiseRun caractere in afara
        // ritmului (zgomot clar) - inceputul unui mesaj cu pauze de tastare nu se pierde, iar dupa
        // zgomot taietura cade la inceputul semnalului
        if (!IsOpen)
        {
            if (!good && _badRun >= NoiseRun) _recent.Clear();
            if (valid)
            {
                _recent.Add(code);
                if (_recent.Count > 4 * Window) _recent.RemoveAt(0);
            }
        }

        double q = Quality;
        if (IsOpen)
        {
            if (good)
            {
                foreach (int c in _suspect) Emit(c);
                _suspect.Clear();
                Emit(code);
                return;
            }
            if (valid) _suspect.Add(code);
            if (_badRun >= CloseAfterBad || q < Threshold - Hysteresis)
            {
                IsOpen = false;
                _pending.Clear();
                _pending.AddRange(_suspect);
                _suspect.Clear();
                _recent.Clear();
                _sinceClose = 0;
            }
            return;
        }

        // inchis
        if (_sinceClose != int.MaxValue && ++_sinceClose > PendingFrames)
        {
            _pending.Clear();
            _sinceClose = int.MaxValue;
        }
        // dupa o inchidere recenta (pauza de tastare) ajunge un caracter in ritm; altfel doua la rand
        int needGood = _sinceClose != int.MaxValue ? 1 : 2;
        if (_count >= MinFrames && q >= Threshold && _chainGood >= needGood)
        {
            IsOpen = true;
            // ce era tinut la o inchidere recenta (pauza de tastare), apoi tot ce a venit de la
            // ultima serie clara de zgomot (inclusiv caracterul curent)
            foreach (int c in _pending) Emit(c);
            _pending.Clear();
            _sinceClose = int.MaxValue;
            foreach (int c in _recent) Emit(c);
            _recent.Clear();
            _chain.Clear();
            _chainGood = 0;
        }
    }

    /// <summary>Uita tot (reacordare, CLEAR): squelch inchis, fereastra goala.</summary>
    public void Reset()
    {
        Array.Clear(_window);
        _pos = _count = _good = _badRun = 0;
        _lastStart = double.NaN;
        _chain.Clear();
        _chainGood = 0;
        _suspect.Clear();
        _pending.Clear();
        _recent.Clear();
        _sinceClose = int.MaxValue;
        IsOpen = false;
    }
}
