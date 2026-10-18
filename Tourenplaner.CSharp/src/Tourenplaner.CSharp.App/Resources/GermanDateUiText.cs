namespace Tourenplaner.CSharp.App.Resources;

/// <summary>
/// Central German UI text for date input. These values are display hints only;
/// technical parsing and persistence continue to use .NET format strings.
/// </summary>
public static class GermanDateUiText
{
    public const string FormatHint = "TT.MM.JJJJ";
    public const string FieldLabel = "Datum (" + FormatHint + ")";
    public const string AccessibleName = "Datum, Format Tag Punkt Monat Punkt Jahr";
    public const string HelpText = "Datum im Format " + FormatHint + " eingeben oder im Kalender auswählen.";

    public const string InvalidTourDate = "Bitte geben Sie ein gültiges Datum im Format " + FormatHint + " ein.";
    public const string InvalidOrderDate = "Bitte geben Sie ein gültiges Auftragsdatum im Format " + FormatHint + " ein.";
    public const string InvalidDeliveryDate = "Bitte geben Sie ein gültiges Lieferdatum im Format " + FormatHint + " ein.";
    public const string InvalidAbsenceDateRange = "Bitte geben Sie für die Abwesenheit ein gültiges Start- und Enddatum im Format " + FormatHint + " ein.";
    public const string InvalidOutageDateRange = "Bitte geben Sie für den Ausfall ein gültiges Start- und Enddatum im Format " + FormatHint + " ein.";

    public static string InvalidUnavailabilityEntryDate(int entryNumber) =>
        $"Eintrag {entryNumber}: Bitte geben Sie gültige Datumswerte im Format {FormatHint} ein.";
}
