using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string LibraryViewsFile=>Path.Combine(paths.Data,"library-views.json");
    public Task<StoredDocument<LibraryViewsDocument>> LoadLibraryViewsAsync()=>Run(()=>documents.Read<LibraryViewsDocument>(LibraryViewsFile,LibraryViewsDocument.IsValid));
    public Task SaveLibraryViewsAsync(LibraryViewsDocument views)=>Run(()=>
    {
        if(!LibraryViewsDocument.IsValid(views))throw new ArgumentException("The saved views are not valid.",nameof(views));
        EnsureRoots();
        JsonDocuments.RequireWritable(documents.Read<LibraryViewsDocument>(LibraryViewsFile,LibraryViewsDocument.IsValid));
        documents.Write(LibraryViewsFile,views,LibraryViewsDocument.IsValid);
    });
}
