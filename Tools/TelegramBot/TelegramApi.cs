using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace InnBot;

/// <summary>Telegram Bot API 的最小封装（长轮询 getUpdates，不需要公网地址 / webhook）</summary>
public sealed class TelegramApi
{
    readonly HttpClient http;
    readonly string baseUrl;

    public TelegramApi(string token)
    {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(70) };
        baseUrl = $"https://api.telegram.org/bot{token}/";
    }

    async Task<JsonNode?> Call(string method, object? body, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync(baseUrl + method, body ?? new { }, ct);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (json?["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"Telegram {method} 失败：{(string?)json?["description"] ?? resp.StatusCode.ToString()}");
        return json["result"];
    }

    public async Task<(long id, string username)> GetMe(CancellationToken ct)
    {
        var r = await Call("getMe", null, ct);
        return ((long)r!["id"]!, (string)r["username"]!);
    }

    public async Task<JsonArray> GetUpdates(long offset, CancellationToken ct) =>
        (await Call("getUpdates", new { offset, timeout = 50, allowed_updates = new[] { "message" } }, ct)) as JsonArray ?? new JsonArray();

    public Task Typing(long chatId, CancellationToken ct) => Call("sendChatAction", new { chat_id = chatId, action = "typing" }, ct);

    /// <summary>发纯文本（不用 Markdown 解析，避免 Claude 输出的符号导致发送失败），长消息自动拆分</summary>
    public async Task Reply(long chatId, int replyTo, string text, CancellationToken ct)
    {
        bool first = true;
        foreach (var part in ChatLogic.Split(text))
        {
            object body = first
                ? new { chat_id = chatId, text = part, reply_parameters = new { message_id = replyTo, allow_sending_without_reply = true } }
                : new { chat_id = chatId, text = part };
            await Call("sendMessage", body, ct);
            first = false;
        }
    }
}
