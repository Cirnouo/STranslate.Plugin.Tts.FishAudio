namespace FishAudioProbe;

internal sealed class WsSequence
{
    internal bool CanSendTail { get; private set; }
    internal void ObserveAudio(int length)
    {
        if (length > 0) CanSendTail = true;
    }
}
