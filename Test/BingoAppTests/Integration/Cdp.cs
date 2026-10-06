using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoAppTests.Integration;

public sealed class Cdp
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    private readonly int _port;

    public Cdp(int port) => _port = port;

    public IReadOnlyList<string> Titles()
    {
        using JsonDocument pages = JsonDocument.Parse(Http.GetStringAsync("http://localhost:" + _port + "/json/list").Result);
        return pages.RootElement.EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "page")
            .Select(p => p.GetProperty("title").GetString())
            .ToList();
    }

    public string Evaluate(string title, string expression)
    {
        string url = Phone.WaitFor(() => UrlOf(title), TimeSpan.FromSeconds(30))
            ?? throw new AssertFailedException("there is no page \"" + title + "\"; there are: " + string.Join(", ", Titles()));

        using ClientWebSocket socket = new ClientWebSocket();
        using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        socket.ConnectAsync(new Uri(url), limit.Token).Wait();

        string request = JsonSerializer.Serialize(new
        {
            id = 1,
            method = "Runtime.evaluate",
            @params = new { expression, returnByValue = true, awaitPromise = true },
        });
        socket.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, limit.Token).Wait();

        while (true)
        {
            using JsonDocument reply = JsonDocument.Parse(Receive(socket, limit.Token));
            if (!reply.RootElement.TryGetProperty("id", out JsonElement id) || id.GetInt32() != 1) continue;

            JsonElement result = reply.RootElement.GetProperty("result");
            if (result.TryGetProperty("exceptionDetails", out JsonElement failure))
                throw new AssertFailedException("page \"" + title + "\" threw: " + failure.GetRawText());

            JsonElement value = result.GetProperty("result");
            if (!value.TryGetProperty("value", out JsonElement v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        }
    }

    public void Send(string title, string message) =>
        Evaluate(title, "window.HybridWebView.SendRawMessage(" + JsonSerializer.Serialize(message) + "); 'ok'");

    private string UrlOf(string title)
    {
        try
        {
            using JsonDocument pages = JsonDocument.Parse(Http.GetStringAsync("http://localhost:" + _port + "/json/list").Result);
            return pages.RootElement.EnumerateArray()
                .Where(p => p.GetProperty("title").GetString() == title)
                .Select(p => p.GetProperty("webSocketDebuggerUrl").GetString())
                .LastOrDefault();
        }
        catch (AggregateException) { return null; }
    }

    private static string Receive(ClientWebSocket socket, CancellationToken limit)
    {
        byte[] chunk = new byte[64 * 1024];
        using MemoryStream all = new MemoryStream();
        WebSocketReceiveResult part;

        do
        {
            part = socket.ReceiveAsync(chunk, limit).Result;
            all.Write(chunk, 0, part.Count);
        }
        while (!part.EndOfMessage);

        return Encoding.UTF8.GetString(all.ToArray());
    }
}
