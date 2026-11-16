using System.Globalization;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components;

public partial class PlayLineChart
{
    [Parameter] public IReadOnlyList<LineChartPoint> Points { get; set; } = [];
    [Parameter] public string Description { get; set; } = "Aantal gespeelde potjes door de tijd";

    private readonly string gradientId = $"chart-fill-{Guid.NewGuid():N}";
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly (string Key, string Label, int Days)[] ZoomOptions =
    [
        ("48h", "48u", 2), ("1w", "1w", 7), ("1m", "1m", 31),
        ("3m", "3m", 92), ("6m", "6m", 183), ("1y", "1j", 366), ("max", "max", 0)
    ];
    private string zoom = "max";
    private DateOnly from;
    private DateOnly to;
    private bool initialized;

    private DateOnly MinDate => Points.Min(point => point.Date);
    private DateOnly MaxDate => Points.Max(point => point.Date);
    private double SelectionLeft => MaxDate.DayNumber == MinDate.DayNumber ? 0
        : 100.0 * (from.DayNumber - MinDate.DayNumber) / (MaxDate.DayNumber - MinDate.DayNumber);
    private double SelectionWidth => MaxDate.DayNumber == MinDate.DayNumber ? 100
        : Math.Max(1, 100.0 * (to.DayNumber - from.DayNumber) / (MaxDate.DayNumber - MinDate.DayNumber));

    protected override void OnParametersSet()
    {
        if (Points.Count == 0) return;
        if (!initialized)
        {
            from = MinDate;
            to = MaxDate;
            initialized = true;
        }
        else
        {
            from = from < MinDate ? MinDate : from;
            to = to > MaxDate ? MaxDate : to;
            if (from > to) from = to;
        }
    }

    private void SetZoom(string key)
    {
        zoom = key;
        to = MaxDate;
        var days = ZoomOptions.First(option => option.Key == key).Days;
        from = days == 0 ? MinDate : DateOnly.FromDayNumber(Math.Max(MinDate.DayNumber, to.DayNumber - days + 1));
    }

    private void ChangeFrom(ChangeEventArgs args)
    {
        if (DateOnly.TryParse(args.Value?.ToString(), out var date))
        {
            from = date < MinDate ? MinDate : date > to ? to : date;
            zoom = "custom";
        }
    }

    private void ChangeTo(ChangeEventArgs args)
    {
        if (DateOnly.TryParse(args.Value?.ToString(), out var date))
        {
            to = date > MaxDate ? MaxDate : date < from ? from : date;
            zoom = "custom";
        }
    }

    private List<(DateOnly Date, int Value)> Buckets
    {
        get
        {
            var span = to.DayNumber - from.DayNumber;
            var step = span <= 100 ? 1 : span <= 400 ? 7 : 30;
            var values = Points.Where(point => point.Date >= from && point.Date <= to)
                .GroupBy(point => (point.Date.DayNumber - from.DayNumber) / step)
                .ToDictionary(group => group.Key, group => group.Sum(point => point.Value));
            return Enumerable.Range(0, span / step + 1)
                .Select(index => (DateOnly.FromDayNumber(from.DayNumber + index * step),
                    values.GetValueOrDefault(index)))
                .ToList();
        }
    }

    private List<(double X, double Y, string Label, int Value)> VisibleSeries
    {
        get
        {
            var buckets = Buckets;
            var maximum = ChartMaximum;
            return buckets.Select((bucket, index) =>
                (X: buckets.Count == 1 ? 507.0 : 48.0 + 918.0 * index / (buckets.Count - 1),
                 Y: 260.0 - 210.0 * bucket.Value / maximum,
                 Label: bucket.Date.ToString("d MMM yyyy", Dutch), bucket.Value)).ToList();
        }
    }

    private int ChartStep => Math.Max(1, (int)Math.Ceiling(Buckets.Max(bucket => bucket.Value) / 4.0));
    private int ChartMaximum => ChartStep * 4;
    private string LinePath => Path(VisibleSeries.Select(point => (point.X, point.Y)));
    private string AreaPath => $"{LinePath} L 966 260 L 48 260 Z";
    private List<(double Y, int Value)> GridLines
    {
        get
        {
            return Enumerable.Range(0, 5)
                .Select(index => (Y: 260.0 - index * 52.5, Value: ChartStep * index))
                .ToList();
        }
    }
    private List<(double X, string Label)> AxisLabels => Enumerable.Range(0, from == to ? 1 : 5)
        .Select(index =>
        {
            var date = DateOnly.FromDayNumber(from.DayNumber + (to.DayNumber - from.DayNumber) * index / 4);
            var format = to.DayNumber - from.DayNumber <= 31 ? "d MMM" : "MMM yyyy";
            return (X: from == to ? 507.0 : 48.0 + index * 229.5, Label: date.ToString(format, Dutch));
        }).ToList();

    private string OverviewPath
    {
        get
        {
            var first = MinDate.DayNumber;
            var span = Math.Max(1, MaxDate.DayNumber - first);
            var monthly = Points.GroupBy(point => (point.Date.DayNumber - first) / Math.Max(1, span / 80))
                .Select(group => (X: 1000.0 * (group.First().Date.DayNumber - first) / span,
                    Value: group.Sum(point => point.Value))).ToList();
            var peak = Math.Max(1, monthly.Max(point => point.Value));
            return Path(monthly.Select(point => (point.X, 60.0 - 46.0 * point.Value / peak)));
        }
    }

    private static string Path(IEnumerable<(double X, double Y)> points) => string.Join(" ",
        points.Select((point, index) => $"{(index == 0 ? "M" : "L")} {point.X.ToString("0.##", CultureInfo.InvariantCulture)} {point.Y.ToString("0.##", CultureInfo.InvariantCulture)}"));

    private static string Percent(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
