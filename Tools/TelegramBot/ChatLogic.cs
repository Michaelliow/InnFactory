using System.Text;
using System.Text.Json.Nodes;

namespace InnBot;

/// <summary>一条收到的 Telegram 消息（只取机器人需要的字段）</summary>
public sealed record IncomingMessage(
    long ChatId, string ChatType, string ChatTitle, int MessageId, long FromId, string FromName,
    string Text, DateTimeOffset Time, long? ReplyToUserId, List<(string type, int offset, int length)> Entities)
{
    public bool IsPrivate => ChatType == "private";

    public static IncomingMessage? FromUpdate(JsonNode update)
    {
        var m = update["message"];
        if (m == null) return null;
        string? text = (string?)m["text"] ?? (string?)m["caption"];
        if (string.IsNullOrEmpty(text)) return null;
        var chat = m["chat"]!;
        var from = m["from"];
        string name = ((string?)from?["first_name"] ?? "") + ((string?)from?["last_name"] is { } ln ? " " + ln : "");
        if (string.IsNullOrWhiteSpace(name)) name = (string?)from?["username"] ?? "某人";
        var ents = new List<(string, int, int)>();
        foreach (var e in (m["entities"] as JsonArray ?? m["caption_entities"] as JsonArray ?? new JsonArray()))
            if (e != null) ents.Add(((string?)e["type"] ?? "", (int?)e["offset"] ?? 0, (int?)e["length"] ?? 0));
        return new IncomingMessage(
            (long)chat["id"]!, (string?)chat["type"] ?? "", (string?)chat["title"] ?? "",
            (int)m["message_id"]!, (long?)from?["id"] ?? 0, name.Trim(), text,
            DateTimeOffset.FromUnixTimeSeconds((long?)m["date"] ?? 0),
            (long?)m["reply_to_message"]?["from"]?["id"], ents);
    }
}

public enum Trigger { None, Ask, Help, Id, Reset }

/// <summary>纯逻辑（不联网），方便 --selftest 测试</summary>
public static class ChatLogic
{
    public const int TelegramLimit = 4000;   // Telegram 单条消息上限 4096，留点余量

    /// <summary>
    /// 判断要不要回应：私聊一律回答；群里只有 @机器人、回复机器人的消息、或 /ask 命令才回答。
    /// 返回去掉 @机器人 / 命令前缀后的问题文本。
    /// </summary>
    public static Trigger Classify(IncomingMessage m, string botUsername, long botId, out string question)
    {
        string text = m.Text.Trim();
        question = text;
        string at = "@" + botUsername;

        // 命令：/help /id /reset /ask，群里可能带 @机器人 后缀
        if (text.StartsWith('/'))
        {
            int sp = text.IndexOf(' ');
            string cmd = (sp < 0 ? text : text[..sp]).ToLowerInvariant();
            string rest = sp < 0 ? "" : text[(sp + 1)..].Trim();
            int atPos = cmd.IndexOf('@');
            if (atPos >= 0)
            {
                // 发给别的机器人的命令不理
                if (!cmd[(atPos + 1)..].Equals(botUsername, StringComparison.OrdinalIgnoreCase)) return Trigger.None;
                cmd = cmd[..atPos];
            }
            switch (cmd)
            {
                case "/start":
                case "/help": return Trigger.Help;
                case "/id": return Trigger.Id;
                case "/reset": return Trigger.Reset;
                case "/ask": question = rest; return rest.Length > 0 ? Trigger.Ask : Trigger.Help;
            }
        }

        bool mentioned = text.Contains(at, StringComparison.OrdinalIgnoreCase);
        bool replyToBot = m.ReplyToUserId == botId && botId != 0;
        if (!m.IsPrivate && !mentioned && !replyToBot) return Trigger.None;

        question = RemoveIgnoreCase(text, at).Trim();
        return question.Length == 0 ? Trigger.Help : Trigger.Ask;
    }

    static string RemoveIgnoreCase(string s, string token)
    {
        int i;
        while ((i = s.IndexOf(token, StringComparison.OrdinalIgnoreCase)) >= 0) s = s.Remove(i, token.Length);
        return s;
    }

    /// <summary>把最近的群聊记录拼成给 Claude 看的文字</summary>
    public static string BuildPrompt(IEnumerable<ChatLine> history, string askerName, string question)
    {
        var sb = new StringBuilder();
        var lines = history.ToList();
        if (lines.Count > 0)
        {
            sb.AppendLine("<recent_chat>");
            foreach (var l in lines) sb.AppendLine($"[{l.Time.ToLocalTime():MM-dd HH:mm}] {l.Name}: {l.Text}");
            sb.AppendLine("</recent_chat>");
            sb.AppendLine();
        }
        sb.AppendLine($"{askerName} 刚才问你：");
        sb.Append(question);
        return sb.ToString();
    }

    /// <summary>按 Telegram 长度上限切分，尽量在换行处断开</summary>
    public static List<string> Split(string text, int limit = TelegramLimit)
    {
        var parts = new List<string>();
        text = text.Trim();
        while (text.Length > limit)
        {
            int cut = text.LastIndexOf('\n', limit);
            if (cut < limit / 2) cut = limit;
            parts.Add(text[..cut].TrimEnd());
            text = text[cut..].TrimStart();
        }
        if (text.Length > 0) parts.Add(text);
        return parts;
    }
}

public sealed record ChatLine(DateTimeOffset Time, string Name, string Text);

/// <summary>每个群各自记住最近 N 条消息（只在内存里，机器人重启就清空，不写文件）</summary>
public sealed class ChatMemory
{
    readonly int cap;
    readonly Dictionary<long, LinkedList<ChatLine>> chats = new();

    public ChatMemory(int cap) { this.cap = Math.Max(0, cap); }

    public void Add(long chatId, ChatLine line)
    {
        if (cap == 0) return;
        if (!chats.TryGetValue(chatId, out var list)) chats[chatId] = list = new LinkedList<ChatLine>();
        // 单条消息太长时截断，避免一条长文把上下文撑爆
        if (line.Text.Length > 2000) line = line with { Text = line.Text[..2000] + "…（已截断）" };
        list.AddLast(line);
        while (list.Count > cap) list.RemoveFirst();
    }

    public IReadOnlyList<ChatLine> Get(long chatId) => chats.TryGetValue(chatId, out var l) ? l.ToList() : new List<ChatLine>();
    public void Reset(long chatId) => chats.Remove(chatId);
}
