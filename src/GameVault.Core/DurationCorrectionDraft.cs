using System.Globalization;
namespace GameVault.Core;
public sealed class DurationCorrectionDraft(long originalSeconds)
{
    public long OriginalSeconds { get; } = originalSeconds;
    public string Hours { get; set; } = (originalSeconds/3600).ToString(CultureInfo.InvariantCulture);
    public string Minutes { get; set; } = (originalSeconds/60%60).ToString(CultureInfo.InvariantCulture);
    public string Seconds { get; set; } = (originalSeconds%60).ToString(CultureInfo.InvariantCulture);
    public string Reason { get; set; } = "";
    public bool IsDirty => !TryDuration(out var value)||value!=OriginalSeconds||Reason.Length>0;
    private bool TryDuration(out long value)
    {
        value=0;
        if(!long.TryParse(Hours,NumberStyles.None,CultureInfo.InvariantCulture,out var hours)||
           !int.TryParse(Minutes,NumberStyles.None,CultureInfo.InvariantCulture,out var minutes)||
           !int.TryParse(Seconds,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds)||hours<0||minutes is <0 or >59||seconds is <0 or >59) return false;
        try { value=checked(hours*3600+minutes*60L+seconds);return true; } catch(OverflowException){return false;}
    }
    public long Validate()
    {
        if(!TryDuration(out var value)||string.IsNullOrWhiteSpace(Reason)||Reason.Length>2000)
            throw new ArgumentException("Enter whole hours, minutes/seconds from 0 to 59, and a reason (up to 2,000 characters). Duration must fit a 64-bit number of seconds.");
        return value;
    }
}