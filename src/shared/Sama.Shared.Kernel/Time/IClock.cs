namespace Sama.Shared.Kernel.Time;

/// <summary>
/// Injectable clock. Token expiry, rate-limit windows and saga timeouts all read time;
/// going through this makes those testable without sleeping.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
