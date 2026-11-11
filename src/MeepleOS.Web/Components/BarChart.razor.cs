using System.Globalization;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components;

public partial class BarChart
{
    [Parameter] public List<ChartItem> Items { get; set; } = [];

    private static string ReadableLabel(string label)
    {
        if (label.Length != 7 || label[4] != '-'
            || !int.TryParse(label[..4], out var year)
            || !int.TryParse(label[5..], out var month)
            || month is < 1 or > 12)
            return label;

        return new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("nl-NL"));
    }
}
