using System.Text.Json;
using MeepleOS.AiClient;
using MeepleOS.Web.Components.Pages;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;
using System.Reflection;

if (args.Contains("--counter-tests"))
{
    await CounterChecks.RunAsync();
    return;
}

if (args.Contains("--display-tests"))
{
    var render = typeof(Chat).GetMethod("RenderMessage", BindingFlags.NonPublic | BindingFlags.Static)!;
    var shortMessage = (MarkupString)render.Invoke(null, ["Hoi"])!;
    var sourcedMessage = (MarkupString)render.Invoke(null,
        ["Botanicus gevonden.\nSpeelduur: 45 minuten.\nBron: [BoardGameGeek](https://boardgamegeek.com/boardgame/1)"])!;
    Check(shortMessage.Value == "Hoi", "Kort bericht blijft direct leesbaar");
    var html = System.Net.WebUtility.HtmlDecode(sourcedMessage.Value);
    Check(html.Contains("Botanicus gevonden.\nSpeelduur: 45 minuten.\nBron: ")
        && html.Contains("<a href=\"https://boardgamegeek.com/boardgame/1\"")
        && html.Contains(">BoardGameGeek</a>"),
        "Regeleinden en klikbare bronlink blijven behouden");
    Console.WriteLine("Chatweergave: kort bericht, meerdere regels en bronlink geslaagd.");
    return;
}

var today = new DateOnly(2026, 11, 16);
const string regenwormen = "Ik wil het bordspel Regenwormen toevoegen.";
const string botanicus = "Ik wil het bordspel Botanicus toevoegen.";

var tools = new EmptyCatalogTools();
var firstPlanner = new FakeGemini(new Plan
{
    Actions = [AddGame("Regenwormen")],
    Question = "Wat is de speelduur van Regenwormen?"
});
var failedTavily = new FakeTavily();
var first = new CommandProcessor(tools, firstPlanner, gameSearch: failedTavily);
var firstAnswer = await first.RunAsync(regenwormen, today);
Check(first.PendingCommand == regenwormen && firstAnswer.Message.Contains("Regenwormen")
    && failedTavily.Games.SequenceEqual(["Regenwormen"]), "Regenwormen blijft actief na ontbrekende spelgegevens");

// Een nieuwe paginainstantie krijgt de context uit de scoped ChatService opnieuw mee.
var nextPlanner = new FakeGemini(new Plan
{
    Actions = [AddGame("Botanicus")],
    Question = "Wat is de speelduur van Botanicus?"
});
var nextTavily = new FakeTavily();
var next = new CommandProcessor(tools, nextPlanner, pendingCommand: first.PendingCommand,
    pendingQuestion: first.PendingQuestion, pendingSearchName: first.PendingSearchName,
    gameSearch: nextTavily);
await next.RunAsync(botanicus, today);
Check(nextTavily.Games.SequenceEqual(["Botanicus"])
    && nextPlanner.Commands.Single() == botanicus
    && next.PendingCommand == botanicus, "Botanicus vervangt Regenwormen vóór de zoekactie");

foreach (var answer in new[] { "30 minuten", "zoek het op" })
{
    var followupPlanner = new FakeGemini(new Plan
    {
        Actions = [AddGame("Regenwormen")],
        Question = "Nog een gegeven nodig."
    });
    var followupTavily = new FakeTavily();
    var followup = new CommandProcessor(tools, followupPlanner, pendingCommand: first.PendingCommand,
        pendingQuestion: first.PendingQuestion, pendingSearchName: first.PendingSearchName,
        gameSearch: followupTavily);
    await followup.RunAsync(answer, today);
    Check(followupPlanner.Commands.Single().Contains("Regenwormen")
        && followupPlanner.Commands.Single().Contains($"Aanvullende gegevens: {answer}")
        && followupTavily.Games.SequenceEqual(["Regenwormen"]), $"Vervolgantwoord '{answer}' blijft bij Regenwormen");
}

// Ook na een echte zoekfout zonder PendingQuestion mag een kort vervolg de opdracht aanvullen.
var error = new CommandProcessor(tools, new FakeGemini(new Plan { Actions = [AddGame("Regenwormen")] }),
    gameSearch: new FakeTavily(throwError: true));
await error.RunAsync("Voeg het bordspel Regenwormen toe", today);
Check(error.PendingCommand is not null && error.PendingQuestion is null, "Zoekfout bewaart de opdracht");
var retryPlanner = new FakeGemini(new Plan
{
    Actions = [AddGame("Regenwormen")],
    Question = "Nog een gegeven nodig."
});
await new CommandProcessor(tools, retryPlanner, pendingCommand: error.PendingCommand,
    gameSearch: new FakeTavily()).RunAsync("zoek het op", today);
Check(retryPlanner.Commands.Single().Contains("Regenwormen")
    && retryPlanner.Commands.Single().Contains("Aanvullende gegevens: zoek het op"),
    "Zoek het op werkt na een zoekfout");

var conversation = new ChatConversation();
var oldRequest = conversation.Begin(regenwormen);
var newPageConversation = conversation; // De scoped service leeft door bij paginawissel.
var newRequest = newPageConversation.Begin(botanicus);
newPageConversation.Complete(newRequest, "Botanicus toegevoegd.");
conversation.Complete(oldRequest, "Welke gegevens heb je voor Regenwormen?");
Check(conversation.Exchanges.Count == 2
    && conversation.Exchanges[0].Response == "Welke gegevens heb je voor Regenwormen?"
    && conversation.Exchanges[1].Response == "Botanicus toegevoegd.",
    "Vertraagd antwoord blijft bij het oorspronkelijke bericht");

Console.WriteLine("Chatcontext: nieuwe opdracht, twee vervolgantwoorden, zoekfout en vertraagde antwoorden geslaagd.");

static ActionPlan AddGame(string name) => new() { Kind = "add_boardgame", Name = name };
static void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"Test mislukt: {label}");
}

sealed class FakeGemini(params Plan[] plans) : IPlanner
{
    private int index;
    public List<string> Commands { get; } = [];
    public Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default)
    {
        Commands.Add(command);
        return Task.FromResult(plans[index++]);
    }
}

sealed class FakeTavily(bool throwError = false) : IGameSearch
{
    public List<string> Games { get; } = [];
    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
    {
        Games.Add(game);
        if (throwError) throw new GameSearchUnavailableException("Zoeken mislukt.");
        return Task.FromResult<IReadOnlyList<GameSearchHit>>([]);
    }
}

sealed class EmptyCatalogTools : IMeepleTools
{
    public Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
        => Task.FromResult(JsonSerializer.SerializeToElement<object>(tool == "get_my_account"
            ? new { username = "test", playerId = (int?)null, playerName = (string?)null }
            : Array.Empty<object>()));
}
