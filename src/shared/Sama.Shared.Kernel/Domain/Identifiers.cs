namespace Sama.Shared.Kernel.Domain;

/// <summary>
/// Identifier factory. Every entity id in Sama is a UUIDv7: random enough to be
/// unguessable in a URL, but time-ordered, so B-tree inserts stay at the right edge
/// of the index instead of scattering across it the way UUIDv4 does.
/// </summary>
public static class SamaId
{
    public static Guid New() => Guid.CreateVersion7();

    public static Guid New(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);
}
