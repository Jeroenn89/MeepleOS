using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace MeepleOS.Web.Components.Pages;

public partial class Chat
{
    private static readonly Regex DestructiveCommand = new(
        @"\b(verwijder|wis|wijzig|hernoem|pas aan)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourceLink = new(@"\[(?<label>[^\]\r\n]{1,100})\]\((?<url>https://[^\s)]+)\)",
        RegexOptions.Compiled);

    private static MarkupString RenderMessage(string message)
    {
        var html = new StringBuilder();
        var offset = 0;
        foreach (Match match in SourceLink.Matches(message))
        {
            html.Append(HtmlEncoder.Default.Encode(message[offset..match.Index]).Replace("\r\n", "<br>").Replace("\n", "<br>"));
            var url = match.Groups["url"].Value;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                html.Append("<a href=\"").Append(HtmlEncoder.Default.Encode(url))
                    .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">")
                    .Append(HtmlEncoder.Default.Encode(match.Groups["label"].Value)).Append("</a>");
            else
                html.Append(HtmlEncoder.Default.Encode(match.Value));
            offset = match.Index + match.Length;
        }
        html.Append(HtmlEncoder.Default.Encode(message[offset..]).Replace("\r\n", "<br>").Replace("\n", "<br>"));
        return new MarkupString(html.ToString());
    }

    private List<(bool User, string Text)> messages = [];
    private string prompt = "";
    private string? pending;
    private bool busy;
    private ElementReference chatWindow;
    private bool scrollPending;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!scrollPending)
            return;

        scrollPending = false;
        await JS.InvokeVoidAsync("meepleChat.scrollToBottom", chatWindow);
    }

    private async Task Send()
    {
        var command = prompt.Trim();
        if (command.Length == 0 || busy)
            return;

        prompt = "";
        messages.Add((true, command));
        scrollPending = true;

        if (DestructiveCommand.IsMatch(command))
        {
            pending = command;
            messages.Add((false, "Deze opdracht kan gegevens definitief wijzigen. Bevestig de tekst hieronder."));
            scrollPending = true;
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
        scrollPending = true;
    }
    private async Task Execute(string command)
    {
        busy = true;
        StateHasChanged();
        await Task.Yield();
        try
        {
            messages.Add((false, await ChatService.AskAsync(command)));
            scrollPending = true;
        }
        catch (Exception)
        {
            messages.Add((false, "Er is iets misgegaan. Probeer het opnieuw."));
            scrollPending = true;
        }
        finally
        {
            busy = false;
        }
    }
}
