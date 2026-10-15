using System.Globalization;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.Application.Services;

/// <summary>Inclusive delivery window. A null boundary means that side is open.</summary>
public sealed record DeliveryPeriod(DateOnly? Start, DateOnly? End);

/// <summary>
/// Recognizes explicit German delivery windows without guessing dates from unrelated text.
/// Omitted years use the reference year (ISO week-year for calendar weeks).
/// Month thirds mean days 1-10, 11-20 and 21-last. No stored order dates are changed.
/// </summary>
public static class DeliveryPeriodParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private const string Year = @"(?:\d{4}|\d{2})";
    private const string WeekYear = @"(?:\s*[/\.]\s*|\s+)(?<y{0}>" + Year + @")";
    private const string CalendarToken = @"(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[./]\s*\d{1,2}(?:[./]\s*\d{4}|[./]\s*\d{2})?\.?|\d{1,2}\.?|(?:(?:anfang|mitte|ende)\s+)?(?:\d{1,2}\.?\s+)?[a-zäöü]+\.?(?:\s+\d{4}|\s+\d{2})?)";

    public static DeliveryPeriod? Parse(string? text, DateOnly referenceDate)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
            return null;

        try
        {
            var value = Regex.Replace(text.Trim().ToLowerInvariant().Replace('–', '-').Replace('—', '-'), @"\s+", " ");
            value = Regex.Replace(value, @"^(?:ca\.?|circa|voraussichtlich)\s+", "");
            var openStart = false;
            var openEnd = false;
            if (value.StartsWith("ab ") || value.StartsWith("ab kw") || value.StartsWith("abkw"))
            {
                value = value[2..].Trim();
                openEnd = true;
            }
            else if (value.StartsWith("bis "))
            {
                value = value[4..].Trim();
                openStart = true;
            }
            else if (value.StartsWith("von "))
                value = value[4..].Trim();
            else if (value.StartsWith("zwischen "))
                value = value[9..].Trim();

            var period = ParseWeeks(value, referenceDate) ?? ParseCalendar(value, referenceDate.Year);
            if (period is null)
                return null;
            return openEnd ? new DeliveryPeriod(period.Start, null)
                : openStart ? new DeliveryPeriod(null, period.End) : period;
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    private static Match Match(string text, string pattern) =>
        Regex.Match(text, pattern, RegexOptions.CultureInvariant, MatchTimeout);

    private static int? Number(Match match, string name) =>
        match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : null;

    private static int? ReadYear(Match match, string name)
    {
        var year = Number(match, name);
        return year.HasValue && match.Groups[name].Length == 2 ? year + 2000 : year;
    }

    private static DeliveryPeriod? ParseWeeks(string text, DateOnly referenceDate)
    {
        var pattern = @"^(?:kw|kalenderwoche)\s*\.?(?<w1>\d{1,2})(?:" + WeekYear.Replace("{0}", "1") + @")?" +
                      @"(?:\s*(?:bis|und|-)\s*(?:(?:kw|kalenderwoche)\s*\.?)?(?<w2>\d{1,2})(?:" + WeekYear.Replace("{0}", "2") + @")?)?$";
        var match = Match(text, pattern);
        if (!match.Success) return null;
        var week1 = Number(match, "w1")!.Value;
        var week2 = Number(match, "w2") ?? week1;
        var explicitYear1 = ReadYear(match, "y1");
        var explicitYear2 = ReadYear(match, "y2");
        var year1 = explicitYear1 ?? explicitYear2 ?? ISOWeek.GetYear(referenceDate.ToDateTime(TimeOnly.MinValue));
        var year2 = explicitYear2 ?? year1;
        if (week2 < week1)
        {
            if (explicitYear1 is null && explicitYear2 is not null) year1--;
            else if (explicitYear2 is null) year2++;
        }
        if (week1 < 1 || week1 > ISOWeek.GetWeeksInYear(year1) || week2 < 1 || week2 > ISOWeek.GetWeeksInYear(year2))
            return null;
        var start = DateOnly.FromDateTime(ISOWeek.ToDateTime(year1, week1, DayOfWeek.Monday));
        var end = DateOnly.FromDateTime(ISOWeek.ToDateTime(year2, week2, DayOfWeek.Sunday));
        return start <= end ? new DeliveryPeriod(start, end) : null;
    }

    private sealed record CalendarPart(int? Day, int? Month, int? Year, string Phase = "");

    private static DeliveryPeriod? ParseCalendar(string text, int referenceYear)
    {
        var single = ParsePart(text);
        if (single is { Month: not null })
            return Resolve(single, single, referenceYear);

        var match = Match(text, @"^(?<left>" + CalendarToken + @")\s*(?:bis|und|-)\s*(?<right>" + CalendarToken + @")$");
        if (!match.Success) return null;
        var left = ParsePart(match.Groups["left"].Value.Trim());
        var right = ParsePart(match.Groups["right"].Value.Trim());
        if (left is null || right is null || (left.Month is null && right.Month is null)) return null;
        return Resolve(left, right, referenceYear);
    }

    private static CalendarPart? ParsePart(string text)
    {
        if (text is "anfang" or "mitte" or "ende") return new CalendarPart(null, null, null, text);
        var iso = Match(text, @"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$");
        if (iso.Success) return new CalendarPart(Number(iso, "d"), Number(iso, "m"), ReadYear(iso, "y"));
        var numeric = Match(text, @"^(?<d>\d{1,2})(?<sep>[./])\s*(?<m>\d{1,2})(?:\k<sep>\s*(?<y>" + Year + @"))?\.?$");
        if (numeric.Success) return new CalendarPart(Number(numeric, "d"), Number(numeric, "m"), ReadYear(numeric, "y"));
        var dayOnly = Match(text, @"^(?<d>\d{1,2})\.?$");
        if (dayOnly.Success) return new CalendarPart(Number(dayOnly, "d"), null, null);
        var named = Match(text, @"^(?:(?<phase>anfang|mitte|ende)\s+)?(?:(?<d>\d{1,2})\.?\s+)?(?<month>[a-zäöü]+)\.?(?:\s+(?<y>" + Year + @"))?$");
        if (!named.Success) return null;
        var month = named.Groups["month"].Value switch
        {
            "januar" or "jan" => 1,
            "februar" or "feb" => 2,
            "märz" or "maerz" or "mär" or "mrz" => 3,
            "april" or "apr" => 4,
            "mai" => 5,
            "juni" or "jun" => 6,
            "juli" or "jul" => 7,
            "august" or "aug" => 8,
            "september" or "sept" or "sep" => 9,
            "oktober" or "okt" => 10,
            "november" or "nov" => 11,
            "dezember" or "dez" => 12,
            _ => 0
        };
        if (month == 0 || (named.Groups["phase"].Success && named.Groups["d"].Success)) return null;
        return new CalendarPart(Number(named, "d"), month, ReadYear(named, "y"), named.Groups["phase"].Value);
    }

    private static DeliveryPeriod? Resolve(CalendarPart left, CalendarPart right, int referenceYear)
    {
        var month1 = left.Month ?? right.Month!.Value;
        var month2 = right.Month ?? month1;
        var year1 = left.Year ?? right.Year ?? referenceYear;
        var year2 = right.Year ?? year1;
        if (month2 < month1)
        {
            if (left.Year is null && right.Year is not null) year1--;
            else if (right.Year is null) year2++;
        }
        var day1 = left.Day ?? (left.Phase == "mitte" ? 11 : left.Phase == "ende" ? 21 : 1);
        var day2 = right.Day ?? (right.Phase == "anfang" ? 10 : right.Phase == "mitte" ? 20 : DateTime.DaysInMonth(year2, month2));
        var start = new DateOnly(year1, month1, day1);
        var end = new DateOnly(year2, month2, day2);
        return start <= end ? new DeliveryPeriod(start, end) : null;
    }
}
