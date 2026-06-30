namespace FlowTerminal.Notes;

/// <summary>Persistence for bookmarked replay moments.</summary>
public interface IBookmarkRepository
{
    Task AddAsync(Bookmark bookmark, CancellationToken cancellationToken);

    Task<IReadOnlyList<Bookmark>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken);
}

/// <summary>Persistence for manual (unverified) trade annotations.</summary>
public interface IAnnotationRepository
{
    Task AddAsync(ManualAnnotation annotation, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManualAnnotation>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken);
}

/// <summary>Extended note querying with date/contract filters (in addition to root/tag).</summary>
public interface INotesQueryRepository
{
    Task<IReadOnlyList<SessionNote>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken);
}
