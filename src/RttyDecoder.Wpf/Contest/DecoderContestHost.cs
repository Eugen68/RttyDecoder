namespace RttyDecoder.Wpf.Contest;

public sealed class DecoderContestHost(RttyDecoderViewModel decoder) : IContestHost
{
    public long? FrequencyHz => decoder.RigConnected ? decoder.RigState.FrequencyHz : null;
    public bool IsBusy => decoder.ContestTxBusy;
    public string MyCall => decoder.MyCall;
    public bool TryTransmit(string text) => decoder.TrySendContestText(text);
    public void Stop() => decoder.StopTx();
}
