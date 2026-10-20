using Npgsql;

namespace Tourenplaner.CSharp.App.Services;

internal static class PostgreSqlCapacityError
{
    public static bool IsCapacityError(Exception error) =>
        error is PostgresException { SqlState: PostgresErrorCodes.TooManyConnections } ||
        error.InnerException is not null && IsCapacityError(error.InnerException);

    public static string SaveMessage(bool wasCommitted) => wasCommitted
        ? "Die Tour wurde erfolgreich gespeichert. Die anschließende Aktualisierung konnte wegen ausgelasteter Datenbankverbindungen nicht abgeschlossen werden. Bitte die Tourübersicht aktualisieren und die Tour nicht erneut anlegen."
        : "Die Tour konnte wegen ausgelasteter Datenbankverbindungen nicht gespeichert werden. Bitte kurz warten und erneut speichern. Die geplanten Stopps bleiben erhalten.";
}
