namespace Tourenplaner.CSharp.Application.Common;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string entityName, string entityId)
        : base($"{entityName} {entityId} wurde zwischenzeitlich von einem anderen Benutzer geaendert oder geloescht.")
    {
        EntityName = entityName;
        EntityId = entityId;
    }

    public string EntityName { get; }

    public string EntityId { get; }
}
