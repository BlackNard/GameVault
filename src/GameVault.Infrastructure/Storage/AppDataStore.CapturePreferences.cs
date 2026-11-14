using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string CapturePreferencesFile=>Path.Combine(paths.Data,"capture-preferences.json");
    public Task<StoredDocument<CapturePreferences>> LoadCapturePreferencesAsync()=>Run(()=>documents.Read<CapturePreferences>(CapturePreferencesFile,ValidCapturePreferences));
    public Task SaveCapturePreferencesAsync(CapturePreferences preferences)=>Run(()=>
    {
        preferences.Validate();EnsureRoots();
        JsonDocuments.RequireWritable(documents.Read<CapturePreferences>(CapturePreferencesFile,ValidCapturePreferences));
        documents.Write(CapturePreferencesFile,preferences,ValidCapturePreferences);
    });
    private static bool ValidCapturePreferences(CapturePreferences value)
    {
        try{value.Validate();return true;}catch(ArgumentException){return false;}
    }
}
