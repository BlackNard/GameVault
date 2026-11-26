using GameVault.Core;
using Forms=System.Windows.Forms;
namespace GameVault.App;

// The shell adapter is created only by the real main window, never by headless tests.
internal sealed class TrayIconHost:IDisposable
{
    private readonly Forms.NotifyIcon icon=new();
    private readonly Forms.ContextMenuStrip menu=new();
    private readonly Forms.ToolStripMenuItem alerts=new("Backup alerts");
    private readonly System.Drawing.Icon image=AppIcon.LoadTrayIcon();
    private readonly Action<Guid> review;
    private Guid? balloonGameId;private bool disposed;private string signature="";
    public bool Available=>!disposed&&icon.Visible;
    public TrayIconHost(Action open,Action quit,Action<Guid> review)
    {
        this.review=review;
        try
        {
            var show=new Forms.ToolStripMenuItem("Open GameVault");show.Click+=(_,_)=>open();
            var exit=new Forms.ToolStripMenuItem("Quit GameVault");exit.Click+=(_,_)=>quit();menu.Items.Add(show);menu.Items.Add(alerts);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(exit);
            icon.Icon=image;icon.Text="GameVault";icon.ContextMenuStrip=menu;icon.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)open();};
            icon.BalloonTipClicked+=(_,_)=>{if(balloonGameId is Guid id)review(id);};icon.Visible=true;Update(0,[]);
        }
        catch{Dispose();throw;}
    }
    public void Update(int active,BackupAlert[] pending)
    {
        if(disposed)return;icon.Text=active>0?$"GameVault — {active} active session(s)":"GameVault — tracking and backups ready";
        var next=string.Join("|",pending.Select(a=>a.GameId.ToString("N")+a.GameName));if(signature==next&&alerts.Text!="Backup alerts")return;signature=next;
        foreach(Forms.ToolStripItem item in alerts.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()){alerts.DropDownItems.Remove(item);item.Dispose();}
        alerts.Text=$"Backup alerts ({pending.Length})";alerts.Enabled=pending.Length>0;
        foreach(var alert in pending.Take(20))
        {
            var item=new Forms.ToolStripMenuItem(Clean(alert.GameName,65));item.Click+=(_,_)=>review(alert.GameId);alerts.DropDownItems.Add(item);
        }
        if(pending.Length>20){var more=new Forms.ToolStripMenuItem("Review another alert…");more.Click+=(_,_)=>review(pending[20].GameId);alerts.DropDownItems.Add(more);}
    }
    public void Notify(BackupAlert alert)
    {
        if(!Available)return;balloonGameId=alert.GameId;
        var confirm=GameVault.Core.SaveConfirmation.IsNeeded(alert.Message);
        icon.ShowBalloonTip(8000,confirm?"GameVault: "+GameVault.Core.SaveConfirmation.Title:"GameVault backup needs attention",
            Clean(alert.GameName,80)+(confirm?": click to confirm its save folder so it can be backed up.":": saves were not backed up. Click to review save locations or retry in GameVault."),Forms.ToolTipIcon.Warning);
    }
    private static string Clean(string value,int limit)=>new string(value.Replace('&',' ').Where(c=>!char.IsControl(c)).Take(limit).ToArray());
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{icon.Visible=false;icon.Dispose();}finally{menu.Dispose();image.Dispose();}
    }
}