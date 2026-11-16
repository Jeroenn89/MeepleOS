using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace MeepleOS.Web.Components.Pages;

public partial class Chat : IDisposable
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
            html.Append(HtmlEncoder.Default.Encode(message[offset..match.Index]));
            var url = match.Groups["url"].Value;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                html.Append("<a href=\"").Append(HtmlEncoder.Default.Encode(url))
                    .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">")
                    .Append(HtmlEncoder.Default.Encode(match.Groups["label"].Value)).Append("</a>");
            else
                html.Append(HtmlEncoder.Default.Encode(match.Value));
            offset = match.Index + match.Length;
        }
        html.Append(HtmlEncoder.Default.Encode(message[offset..]));
        return new MarkupString(html.ToString());
    }

    private string prompt = "";
    private ChatExchange? pending;
    private bool busy;
    private ElementReference chatWindow;
    private bool scrollPending;

    protected override void OnInitialized()
        => ChatService.Conversation.Changed += ConversationChanged;

    private void ConversationChanged()
        => _ = InvokeAsync(() =>
        {
            scrollPending = true;
            StateHasChanged();
        });

    public void Dispose()
        => ChatService.Conversation.Changed -= ConversationChanged;

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
        var exchange = ChatService.Conversation.Begin(command);
        scrollPending = true;

        if (DestructiveCommand.IsMatch(command))
        {
            pending = exchange;
            ChatService.Conversation.Complete(exchange, "Deze opdracht kan gegevens definitief wijzigen. Bevestig de tekst hieronder.");
            scrollPending = true;
            return;
        }

        await Execute(exchange);
    }

    private async Task Confirm()
    {
        if (pending is null || busy)
            return;

        var exchange = pending;
        pending = null;
        await Execute(exchange);
    }

    private void Cancel()
    {
        if (pending is null)
            return;

        var exchange = pending;
        pending = null;
        ChatService.Conversation.Complete(exchange, "Opdracht geannuleerd; niets opgeslagen.");
        scrollPending = true;
    }
    private async Task Execute(ChatExchange exchange)
    {
        busy = true;
        StateHasChanged();
        await Task.Yield();
        try
        {
            await ChatService.AskAsync(exchange);
            scrollPending = true;
        }
        catch (Exception)
        {
            ChatService.Conversation.Complete(exchange, "Er is iets misgegaan. Probeer het opnieuw.");
            scrollPending = true;
        }
        finally
        {
            busy = false;
        }
    }
}
