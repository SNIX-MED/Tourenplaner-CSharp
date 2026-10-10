namespace Tourenplaner.CSharp.App.Services;

public sealed class PostgreSqlHistoryConflictException : InvalidOperationException
{
    public PostgreSqlHistoryConflictException(string message) : base(message)
    {
    }
}
