namespace GameVault.Core;

public sealed record BackupAlert(Guid GameId,string GameName,string Message,DateTimeOffset CreatedAt);
// Accessed by the UI dispatcher; no shell calls or timers are owned here.
public sealed class BackupAlertPolicy
{
    private sealed class Episode(BackupAlert alert,long sequence)
    {public BackupAlert Alert=alert;public long Sequence=sequence;public bool Announced;public bool Acknowledged;}
    private readonly Dictionary<Guid,Episode> episodes=[];
    private long sequence;private DateTimeOffset? lastNotification;
    public BackupAlert[] Alerts=>episodes.Values.Where(e=>!e.Acknowledged).OrderByDescending(e=>e.Sequence).Select(e=>e.Alert).ToArray();
    public void Record(BackupResult result,string gameName,DateTimeOffset now)
    {
        if(result.Phase is BackupPhase.Complete or BackupPhase.Unchanged){episodes.Remove(result.GameId);return;}
        if(result.Phase!=BackupPhase.Failed)return;
        var alert=new BackupAlert(result.GameId,gameName,result.Message,now);
        if(episodes.TryGetValue(result.GameId,out var previous)){if(previous.Alert.Message!=alert.Message){previous.Announced=false;previous.Acknowledged=false;}previous.Alert=alert;previous.Sequence=++sequence;return;}
        if(episodes.Count>=128)episodes.Remove(episodes.MinBy(e=>e.Value.Sequence).Key);
        episodes.Add(result.GameId,new(alert,++sequence));
    }
    public BackupAlert? TakeNotification(DateTimeOffset now,bool enabled,bool anySessionActive)
    {
        if(!enabled||anySessionActive)return null;
        if(lastNotification is DateTimeOffset last&&now>=last&&now-last<TimeSpan.FromMinutes(2))return null;
        var next=episodes.Values.Where(e=>!e.Announced&&!e.Acknowledged).OrderByDescending(e=>e.Sequence).FirstOrDefault();
        if(next is null)return null;next.Announced=true;lastNotification=now;return next.Alert;
    }
    public void Acknowledge(Guid id){if(episodes.TryGetValue(id,out var item)){item.Acknowledged=true;item.Announced=true;}}
}