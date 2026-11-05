using RttyDecoder.Core;

namespace RttyDecoder.Wpf;

public sealed record ReceptionProfileChoice(RttyReceptionProfile? Id, string Name, string Description);

public sealed partial class RttyDecoderViewModel
{
    public IReadOnlyList<ReceptionProfileChoice> ReceptionProfiles { get; } = new[]
    {
        new ReceptionProfileChoice(null, "Personalizat", "Reglaje manuale. Alegerea nu reseteaza valorile curente."),
        new ReceptionProfileChoice(RttyReceptionProfile.GoodSignal, "Semnal bun · rapid", "Matched Filter + SQL 35%. Acord bun, fading lent. Fara intarzierea modelului neural."),
        new ReceptionProfileChoice(RttyReceptionProfile.WeakSignal, "Semnal slab · DeepRTTY", "DeepRTTY: cele mai putine erori pe proba QRN; confirmarea textului are intarziere de aproximativ 8-12 s."),
        new ReceptionProfileChoice(RttyReceptionProfile.QrnFlutter, "QRN / flutter · rapid", "Chebyshev + SQL 35%. Alternativa clasica rapida; NB si ATC raman oprite deoarece nu au imbunatatit proba QRN masurata.")
    };

    private ReceptionProfileChoice? _selectedReceptionProfile;
    public ReceptionProfileChoice? SelectedReceptionProfile
    {
        get => _selectedReceptionProfile ?? ReceptionProfiles[0];
        set
        {
            if (value == null || !Set(ref _selectedReceptionProfile, value)) return;
            if (value.Id is { } id)
            {
                Receiver.Update(settings => RttyProfiles.Apply(id, settings));
                SyncFromSettings(Receiver.Settings);
                Message = value.Description;
            }
        }
    }

    private void SyncProfile(RttySettings settings, bool infer = false)
    {
        var current = SelectedReceptionProfile!;
        ReceptionProfileChoice selected = current;
        if (infer)
            selected = ReceptionProfiles.FirstOrDefault(p => p.Id is { } id && RttyProfiles.Matches(id, settings)) ?? ReceptionProfiles[0];
        else if (current.Id is { } id && !RttyProfiles.Matches(id, settings))
            selected = ReceptionProfiles[0];
        Set(ref _selectedReceptionProfile, selected, nameof(SelectedReceptionProfile));
    }

    public bool IsClassic => Decoder != RttyDecoderType.DeepRtty;
    public bool IsChebyshevDecoder => Decoder == RttyDecoderType.Chebyshev;
    public bool CanAdjustSnrGate => IsClassic && SnrGating;
    public bool CanAdjustSquelch => IsClassic && Squelch;
    public string DecoderHelp => Decoder switch
    {
        RttyDecoderType.DeepRtty => "DeepRTTY primeste audio-ul original. NB, DNR si latimea filtrului schimba numai masurarea SNR si urmarirea AFC/PLL, nu filtreaza intrarea modelului. SQL, SNR-G, ATC, Volant si Sensibilitate nu modifica textul neural. NORM/REV si US-TTY se aplica. Confirmarea textului: aproximativ 8-12 s.",
        RttyDecoderType.MatchedFilter => "Matched Filter integreaza un bit si normalizeaza automat cele doua tonuri. ATC si latimea filtrului Chebyshev nu se aplica. NB/DNR filtreaza intrarea, SNR-G si Sensibilitate influenteaza decizia, Volant sincronizeaza caracterele. SQL filtreaza numai textul afisat. La flutter rapid incercati Chebyshev sau DeepRTTY.",
        _ => "Chebyshev: NB/DNR filtreaza audio-ul decodorului; ATC compenseaza fading-ul selectiv. SNR-G si Sensibilitate influenteaza decizia, Volant sincronizeaza caracterele. SQL filtreaza numai textul afisat, fara sa schimbe bitii decodati. Profilurile pastreaza acordul, NORM/REV, AFC/PLL si alfabetul."
    };
}
