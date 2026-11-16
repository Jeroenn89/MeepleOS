using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components;

public partial class EstimatedPlayTime
{
    [Parameter] public long Minutes { get; set; }

    private bool showHours = true;
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private string FormattedTime => showHours
        ? $"{(Minutes / 60m).ToString("0.##", Dutch)} uur"
        : $"{Minutes.ToString("N0", Dutch)} min";
}
