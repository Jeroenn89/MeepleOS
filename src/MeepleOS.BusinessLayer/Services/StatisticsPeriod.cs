using System.Globalization;

namespace MeepleOS.BusinessLayer.Services;

public sealed record StatisticsPeriod(DateOnly? From, DateOnly? To)
{
    public static StatisticsPeriod Create(int? year = null, int? month = null, int? week = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || week is < 1 or > 53)
            throw new ArgumentException("Geef een geldig jaar, maand en weeknummer op.");
        if (month.HasValue && week.HasValue)
            throw new ArgumentException("Kies een maand of een week, niet allebei.");
        if ((month.HasValue || week.HasValue) && !year.HasValue)
            throw new ArgumentException("Een maand of week vereist ook een jaar.");
        if ((year.HasValue || month.HasValue || week.HasValue) && (from.HasValue || to.HasValue))
            throw new ArgumentException("Combineer een kalenderperiode niet met begin- of einddatum.");
        if (from == default(DateOnly) || to == default(DateOnly) || (from.HasValue && to.HasValue && from > to))
            throw new ArgumentException("De datumperiode is ongeldig.");

        if (week.HasValue)
        {
            if (week > ISOWeek.GetWeeksInYear(year!.Value))
                throw new ArgumentException("Deze ISO-week bestaat niet in het opgegeven jaar.");
            var first = DateOnly.FromDateTime(ISOWeek.ToDateTime(year.Value, week.Value, DayOfWeek.Monday));
            var daysRemaining = DateOnly.MaxValue.DayNumber - first.DayNumber;
            return new(first, first.AddDays(Math.Min(6, daysRemaining)));
        }
        if (month.HasValue)
            return new(new DateOnly(year!.Value, month.Value, 1),
                new DateOnly(year.Value, month.Value, DateTime.DaysInMonth(year.Value, month.Value)));
        if (year.HasValue)
            return new(new DateOnly(year.Value, 1, 1), new DateOnly(year.Value, 12, 31));
        return new(from, to);
    }
}
