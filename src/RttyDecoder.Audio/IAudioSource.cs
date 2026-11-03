namespace RttyDecoder.Audio;

/// <summary>Ce canal se foloseste dintr-o sursa stereo.</summary>
public enum AudioChannel
{
    Left = 0,
    Right = 1,

    /// <summary>Media canalelor. Atentie: la interfete care dau acelasi semnal in antifaza, se anuleaza.</summary>
    Mix = 2,
}

/// <summary>
/// O sursa de audio mono (valori +-1). Evenimentele vin pe firul sursei, nu pe firul UI.
/// </summary>
public interface IAudioSource : IDisposable
{
    /// <summary>Nume afisabil (dispozitivul sau fisierul).</summary>
    string Name { get; }

    /// <summary>Rata de esantionare (cunoscuta dupa Start pentru placa de sunet).</summary>
    int SampleRate { get; }

    /// <summary>Esantioane mono: buffer, numar de esantioane valide, rata.</summary>
    event Action<float[], int, int>? SamplesAvailable;

    /// <summary>Sursa s-a oprit: null = sfarsit normal (sfarsitul fisierului sau Stop), altfel eroarea.</summary>
    event Action<Exception?>? Stopped;

    void Start();

    void Stop();
}

/// <summary>Conversia cadrelor intercalate in mono, dupa <see cref="AudioChannel"/>.</summary>
internal static class Downmix
{
    public static int ToMono(ReadOnlySpan<float> interleaved, int channels, AudioChannel mode, Span<float> mono)
    {
        if (channels <= 1)
        {
            interleaved.CopyTo(mono);
            return interleaved.Length;
        }
        int frames = interleaved.Length / channels;
        int ch = mode == AudioChannel.Right ? 1 : 0;
        for (int f = 0; f < frames; f++)
        {
            int o = f * channels;
            mono[f] = mode == AudioChannel.Mix
                ? (interleaved[o] + interleaved[o + 1]) * 0.5f
                : interleaved[o + ch];
        }
        return frames;
    }
}
