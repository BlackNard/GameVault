using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string LibraryTagsFile=>Path.Combine(paths.Data,"library-tags.json");
    public Task<StoredDocument<LibraryTagsDocument>> LoadLibraryTagsAsync()=>Run(()=>documents.Read<LibraryTagsDocument>(LibraryTagsFile,LibraryTagsDocument.IsValid));
    public Task SaveLibraryTagsAsync(LibraryTagsDocument tags)=>Run(()=>
    {
        if(!LibraryTagsDocument.IsValid(tags))throw new ArgumentException("The tags are not valid.",nameof(tags));
        EnsureRoots();
        JsonDocuments.RequireWritable(documents.Read<LibraryTagsDocument>(LibraryTagsFile,LibraryTagsDocument.IsValid));
        documents.Write(LibraryTagsFile,tags,LibraryTagsDocument.IsValid);
    });
}
