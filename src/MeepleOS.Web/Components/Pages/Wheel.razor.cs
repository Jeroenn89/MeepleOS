using MeepleOS.BusinessLayer.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace MeepleOS.Web.Components.Pages;

public partial class Wheel : IDisposable
{
    private static readonly string[] SegmentColors =
    [
        "var(--wheel-1)", "var(--wheel-2)", "var(--wheel-3)", "var(--wheel-4)",
        "var(--wheel-5)", "var(--wheel-6)", "var(--wheel-7)", "var(--wheel-8)"
    ];

    private List<BoardgameResult>? games;
    private List<BoardgameResult> wheelGames = [];
    private BoardgameResult? selectedGame;
    private bool showResultPopup;
    private bool focusResultPopup;
    private ElementReference popupCloseButton;
    private int playerCount = 4;
    private int maxMinutes = 90;
    private int wheelVersion;
    private double rotationDegrees;
    private bool spinning;
    private readonly CancellationTokenSource spinCancellation = new();

    private List<BoardgameResult> EligibleGames => (games ?? [])
        .Where(game => playerCount >= 1 && maxMinutes >= 1
            && game.MinPlayers <= playerCount
            && (!game.MaxPlayers.HasValue || game.MaxPlayers.Value >= playerCount)
            && game.EstimatedMinutes <= maxMinutes)
        .ToList();

    private string SlicePath(int index)
    {
        var start = WheelPoint(index * 360.0 / wheelGames.Count, 390);
        var end = WheelPoint((index + 1) * 360.0 / wheelGames.Count, 390);
        return FormattableString.Invariant($"M 400 400 L {start.X:0.###} {start.Y:0.###} A 390 390 0 0 1 {end.X:0.###} {end.Y:0.###} Z");
    }

    private (double X, double Y) LabelPosition(int index) =>
        WheelPoint((index + .5) * 360.0 / wheelGames.Count, 360);

    private string LabelAnchor(int index) => (index + .5) * 360.0 / wheelGames.Count <= 180 ? "end" : "start";

    private int LabelFontSize => wheelGames.Count switch
    {
        <= 30 => 16,
        <= 60 => 13,
        <= 100 => 11,
        _ => 9
    };

    private string LabelRotation(int index)
    {
        var angle = (index + .5) * 360.0 / wheelGames.Count;
        var rotation = angle <= 180 ? angle - 90 : angle - 270;
        var point = LabelPosition(index);
        return FormattableString.Invariant($"rotate({rotation:0.###} {point.X:0.###} {point.Y:0.###})");
    }

    private static (double X, double Y) WheelPoint(double angle, double radius)
    {
        var radians = angle * Math.PI / 180;
        return (400 + radius * Math.Sin(radians), 400 - radius * Math.Cos(radians));
    }

    private static string SvgNumber(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string WheelLabel(string name) => name.Length <= 35 ? name : name[..34] + "…";

    protected override async Task OnInitializedAsync()
    {
        games = await GameService.GetAllAsync();
        RebuildWheel();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!focusResultPopup) return;
        focusResultPopup = false;
        await popupCloseButton.FocusAsync();
    }

    private void RebuildWheel()
    {
        if (spinning) return;
        wheelGames = EligibleGames.OrderBy(_ => Random.Shared.Next()).ToList();
        selectedGame = null;
        showResultPopup = false;
        focusResultPopup = false;
        rotationDegrees = 0;
        wheelVersion++;
    }

    private async Task Spin()
    {
        if (spinning || wheelGames.Count == 0) return;

        spinning = true;
        selectedGame = null;
        showResultPopup = false;
        focusResultPopup = false;
        StateHasChanged();
        try
        {
            await Task.Delay(40, spinCancellation.Token);

            var index = Random.Shared.Next(wheelGames.Count);
            var sectorAngle = 360.0 / wheelGames.Count;
            var target = (360.0 - (index + .5) * sectorAngle) % 360.0;
            var current = rotationDegrees % 360.0;
            var offset = (target - current + 360.0) % 360.0;
            rotationDegrees += 5 * 360.0 + offset;
            StateHasChanged();

            await Task.Delay(5100, spinCancellation.Token);
            selectedGame = wheelGames[index];
            spinning = false;
            showResultPopup = true;
            focusResultPopup = true;
        }
        catch (OperationCanceledException)
        {
            // Als de pagina wordt verlaten tijdens het draaien, wordt de spin geannuleerd.
        }
    }

    private void CloseResultPopup()
    {
        showResultPopup = false;
        focusResultPopup = false;
    }

    private void OnPopupKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape") CloseResultPopup();
    }

    public void Dispose()
    {
        spinCancellation.Cancel();
        spinCancellation.Dispose();
    }
}
