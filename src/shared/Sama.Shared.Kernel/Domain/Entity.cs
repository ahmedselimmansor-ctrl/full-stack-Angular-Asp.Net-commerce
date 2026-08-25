namespace Sama.Shared.Kernel.Domain;

/// <summary>Entity identity: two instances are the same entity when their ids match.</summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    /// <summary>Parameterless ctor for EF Core materialisation.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected set; }

    public bool Equals(Entity<TId>? other)
        => other is not null && other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
