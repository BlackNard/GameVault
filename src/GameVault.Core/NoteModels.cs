namespace GameVault.Core;

public sealed record NoteDocument(int SchemaVersion, Guid GameId, Guid? SessionId, string Title,
    string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SessionNote(Guid Id, PlaySession? Session, bool CanEdit, string Context,
    StoredDocument<NoteDocument> Note);
public sealed record GameNotes(StoredDocument<GameProfile> Profile, SessionNote[] Sessions,
    StoredDocument<NoteDocument> Review, bool CanReview, string[] Warnings);