using System.Collections.Concurrent;
using AdminPanel.API.Services;
namespace AdminPanel.Tests;

public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    public void Advance(TimeSpan time) => _offset += time;
}
public sealed record TestResetEmail(string Email, string Token, string Culture);
public sealed class CaptureResetEmail : IPasswordResetEmail
{
    public ConcurrentQueue<TestResetEmail> Messages { get; } = new();
    public Task Send(string email, string token, string culture, CancellationToken ct)
    {
        Messages.Enqueue(new(email, token, culture));
        return Task.CompletedTask;
    }
}

