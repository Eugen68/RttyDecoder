namespace RttyDecoder.Core.Baudot;

/// <summary>
/// Codec ITA2 (Baudot) pentru RTTY - caractere &lt;-&gt; coduri de 5 biti, cu starea LTRS/FIGS.
/// Port din BaudotCodec.java (aplicatia Android). Fiecare directie (RX, TX) are propria instanta,
/// deci propria stare de shift.
/// </summary>
public sealed class BaudotCodec
{
    public const int Ltrs = 0x1F;
    public const int Figs = 0x1B;

    public static readonly char[] Ita2LtrsMap =
    {
        '\0', 'E', '\n', 'A', ' ', 'S', 'I', 'U', '\n', 'D', 'R', 'J', 'N', 'F', 'C', 'K',
        'T', 'Z', 'L', 'W', 'H', 'Y', 'P', 'Q', 'O', 'B', 'G', '\0', 'M', 'X', 'V', '\0',
    };

    public static readonly char[] Ita2FigsMap =
    {
        '\0', '3', '\n', '-', ' ', '\'', '8', '7', '\n', '4', '4', '\u0007', ',', '!', ':', '(',
        '5', '+', ')', '2', '#', '6', '0', '1', '9', '?', '&', '\0', '.', '/', ';', '\0',
    };

    /// <summary>
    /// Varianta US-TTY (multe statii americane): difera doar in FIGS - S = BELL, J = apostrof,
    /// Z = ghilimele, D = $.
    /// </summary>
    public static readonly char[] UsTtyFigsMap =
    {
        '\0', '3', '\n', '-', ' ', '\u0007', '8', '7', '\n', '$', '4', '\'', ',', '!', ':', '(',
        '5', '"', ')', '2', '#', '6', '0', '1', '9', '?', '&', '\0', '.', '/', ';', '\0',
    };

    private static readonly int[] CharToCode = new int[256];
    private static readonly bool[] CharIsFigs = new bool[256];
    private static readonly int[] CharToCodeUs = new int[256];
    private static readonly bool[] CharIsFigsUs = new bool[256];

    static BaudotCodec()
    {
        BuildTxTable(Ita2FigsMap, CharToCode, CharIsFigs);
        BuildTxTable(UsTtyFigsMap, CharToCodeUs, CharIsFigsUs);
    }

    private static void BuildTxTable(char[] figsMap, int[] codes, bool[] isFigs)
    {
        for (int i = 0; i < 32; i++)
        {
            char l = Ita2LtrsMap[i];
            if (l != '\0' && l != '\n') { codes[l] = i; isFigs[l] = false; }
            char f = figsMap[i];
            if (f != '\0' && f != '\n' && f != '\u0007') { codes[f] = i; isFigs[f] = true; }
        }
        codes[' '] = 4; isFigs[' '] = false;
        codes['\n'] = 2; isFigs['\n'] = false;
        codes['\r'] = 8; isFigs['\r'] = false;
    }

    private volatile bool _usTty;

    /// <summary>Starea curenta: 'L' (LTRS) sau 'F' (FIGS).</summary>
    public char Shift { get; private set; } = 'L';

    /// <summary>false = tabelul ITA2, true = US-TTY.</summary>
    public bool UsTty
    {
        get => _usTty;
        set => _usTty = value;
    }

    /// <summary>Copie cu aceeasi stare - pentru text provizoriu decodat fara sa schimbe codecul real.</summary>
    public BaudotCodec Copy() => new() { Shift = Shift, UsTty = UsTty };

    /// <summary>Revine pe LTRS (inceputul unei receptii/transmisii noi).</summary>
    public void ResetShift() => Shift = 'L';

    private bool IsFigsChar(char c) => c < 256 && (UsTty ? CharIsFigsUs : CharIsFigs)[c];

    private int CodeFor(char c) => c < 256 ? (UsTty ? CharToCodeUs : CharToCode)[c] : 0;

    /// <summary>
    /// Decodeaza un cod de 5 biti (0-31) si actualizeaza LTRS/FIGS.
    /// </summary>
    /// <returns>caracterul, sau '\0' pentru coduri de control fara caracter vizibil.</returns>
    public char Decode(int code)
    {
        code &= 0x1F;
        if (code == Ltrs) { Shift = 'L'; return '\0'; }
        if (code == Figs) { Shift = 'F'; return '\0'; }
        if (code == 0x04) Shift = 'L'; // spatiul revine mereu pe LTRS (USOS)
        return Shift == 'L' ? Ita2LtrsMap[code] : (UsTty ? UsTtyFigsMap : Ita2FigsMap)[code];
    }

    /// <summary>Caracterul exista in tabel (literele mici trebuie facute mari inainte).</summary>
    public bool CanEncode(char c) => c is '\n' or '\r' or ' ' || (c < 256 && CodeFor(c) != 0);

    /// <summary>
    /// Encodeaza un caracter (deja uppercase) in unul sau mai multe coduri Baudot.
    /// </summary>
    public int[] Encode(char c)
    {
        if (c == '\n' || c == '\r')
        {
            // CR + LF. Shift-ul NU se schimba: receptoarele revin pe LTRS doar la spatiu, deci
            // dupa un rand terminat in FIGS urmatoarea litera trimite explicit LTRS.
            return new[] { 2, 8 };
        }

        if (c == ' ')
        {
            Shift = 'L';
            return new[] { 4 };
        }

        bool needFigs = IsFigsChar(c);
        if (needFigs && Shift == 'L')
        {
            Shift = 'F';
            return new[] { Figs, CodeFor(c) };
        }
        if (!needFigs && Shift == 'F')
        {
            Shift = 'L';
            return new[] { Ltrs, CodeFor(c) };
        }
        return new[] { CodeFor(c) };
    }
}
