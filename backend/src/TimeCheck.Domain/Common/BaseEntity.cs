namespace TimeCheck.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }
}

/// <summary>Entities that record who created/updated them (set by the persistence layer).</summary>
public interface IAuditedEntity
{
    Guid? CreatedBy { get; }
    Guid? UpdatedBy { get; }
}

/// <summary>Every business row belongs to one church (tenant).</summary>
public interface IChurchScoped
{
    Guid ChurchId { get; }
}

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
