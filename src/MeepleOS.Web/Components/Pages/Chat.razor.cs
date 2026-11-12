using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace MeepleOS.Web.Components.Pages;

public partial class Chat
{
    private static readonly Regex DestructiveCommand = new(
        @"\b(verwijder|wis|wijzig|hernoem|pas aan)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private List<(bool User, string Text)> messages = [];
    private string prompt = "";
    private string? pending;
    private bool busy;

    private async Task Send()
    {
        var command = prompt.Trim();
        if (command.Length == 0 || busy)
            return;

        prompt = "";
        messages.Add((true, command));

        if (DestructiveCommand.IsMatch(command))
        {
            pending = command;
            messages.Add((false, "Deze opdracht kan gegevens definitief wijzigen. Bevestig de tekst hieronder."));
            return;
        }

        await Execute(command);
    }

    private async Task Confirm()
    {
        if (pending is null || busy)
            return;

        var command = pending;
        pending = null;
        await Execute(command);
    }

    private void Cancel()
    {
        pending = null;
        messages.Add((false, "Opdracht geannuleerd; niets opgeslagen."));
    }
    private async Task Execute(string command)
    {
        busy = true;
        StateHasChanged();
        await Task.Yield();
        try
        {
            messages.Add((false, await ChatService.AskAsync(command)));
        }
        catch (Exception)
        {
            messages.Add((false, "Er is iets misgegaan. Probeer het opnieuw."));
        }
        finally
        {
            busy = false;
        }
    }
}
