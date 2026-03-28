namespace ICH.Domain.Interfaces;

using ICH.Domain.Entities;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Session?> GetByIdWithTranscriptsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default);
    Task<Session> CreateAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ITranscriptRepository
{
    Task<TranscriptEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptEntry>> GetBySessionIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<TranscriptEntry> CreateAsync(TranscriptEntry entry, CancellationToken ct = default);
    Task CreateManyAsync(IEnumerable<TranscriptEntry> entries, CancellationToken ct = default);
    Task DeleteBySessionIdAsync(Guid sessionId, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

public interface IBlobStorageService
{
    Task<string> UploadAudioAsync(Guid sessionId, Stream audioStream, string contentType, CancellationToken ct = default);
    Task<Stream?> DownloadAudioAsync(Guid sessionId, CancellationToken ct = default);
    Task DeleteAudioAsync(Guid sessionId, CancellationToken ct = default);
    Task<string> GetAudioUrlAsync(Guid sessionId, TimeSpan expiry, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    ISessionRepository Sessions { get; }
    ITranscriptRepository Transcripts { get; }
    IUserRepository Users { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
