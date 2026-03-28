using Microsoft.EntityFrameworkCore;
using ICH.Domain.Entities;
using ICH.Domain.Interfaces;

namespace ICH.Infrastructure.Data.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _context;

    public SessionRepository(ApplicationDbContext context) => _context = context;

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.Sessions.FindAsync([id], ct);

    public async Task<Session?> GetByIdWithTranscriptsAsync(Guid id, CancellationToken ct = default) =>
        await _context.Sessions
            .Include(s => s.TranscriptEntries.OrderBy(t => t.Timestamp))
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Session>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _context.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default) =>
        await _context.Sessions
            .Where(s => s.Status == SessionStatus.Active)
            .ToListAsync(ct);

    public async Task<Session> CreateAsync(Session session, CancellationToken ct = default)
    {
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync(ct);
        return session;
    }

    public async Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        _context.Sessions.Update(session);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _context.Sessions.FindAsync([id], ct);
        if (session != null)
        {
            _context.Sessions.Remove(session);
            await _context.SaveChangesAsync(ct);
        }
    }
}

public class TranscriptRepository : ITranscriptRepository
{
    private readonly ApplicationDbContext _context;

    public TranscriptRepository(ApplicationDbContext context) => _context = context;

    public async Task<TranscriptEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.TranscriptEntries.FindAsync([id], ct);

    public async Task<IReadOnlyList<TranscriptEntry>> GetBySessionIdAsync(Guid sessionId, CancellationToken ct = default) =>
        await _context.TranscriptEntries
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct);

    public async Task<TranscriptEntry> CreateAsync(TranscriptEntry entry, CancellationToken ct = default)
    {
        _context.TranscriptEntries.Add(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task CreateManyAsync(IEnumerable<TranscriptEntry> entries, CancellationToken ct = default)
    {
        _context.TranscriptEntries.AddRange(entries);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteBySessionIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var entries = await _context.TranscriptEntries
            .Where(t => t.SessionId == sessionId)
            .ToListAsync(ct);

        _context.TranscriptEntries.RemoveRange(entries);
        await _context.SaveChangesAsync(ct);
    }
}

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context) => _context = context;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.Users.FindAsync([id], ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private ISessionRepository? _sessions;
    private ITranscriptRepository? _transcripts;
    private IUserRepository? _users;

    public UnitOfWork(ApplicationDbContext context) => _context = context;

    public ISessionRepository Sessions => _sessions ??= new SessionRepository(_context);
    public ITranscriptRepository Transcripts => _transcripts ??= new TranscriptRepository(_context);
    public IUserRepository Users => _users ??= new UserRepository(_context);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _context.SaveChangesAsync(ct);
}
