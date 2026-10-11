namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A clock that only moves when the test says so. Only the timestamp is faked, timers are not
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
}
