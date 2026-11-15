namespace RttyDecoder.Wpf.Contest;

public sealed class DecoderContestHost(RttyDecoderViewModel decoder) : IContestHost
{
    /// <summary>
    /// Frecventa semnalului (Mark-ul pe RF), nu VFO-ul: in RTTY-L / RTTY-U sunt la fel dupa QSY, dar in
    /// AFSK (DATA-U / USB) VFO-ul e cu tonul audio (~1,5-2 kHz) mai jos. Rotunjita la 10 Hz, ca AFC-ul
    /// sa nu schimbe formularul la fiecare cativa Hz.
    /// </summary>
    public long? FrequencyHz => decoder.StampFrequencyHz is { } hz ? (long)Math.Round(hz / 10) * 10 : null;
    public bool IsBusy => decoder.ContestTxBusy;
    public string MyCall => decoder.MyCall;
    public bool TryTransmit(string text, bool allowAutoQsy) => decoder.TrySendContestText(text, allowAutoQsy);
    public void Stop() => decoder.StopTx();
}
