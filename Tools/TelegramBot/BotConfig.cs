using System.Text.Json;
using System.Text.Json.Serialization;

namespace InnBot;

/// <summary>
/// 机器人配置，读取 config.json（与程序同目录或当前目录）。
/// 密钥也可以用环境变量 TELEGRAM_BOT_TOKEN / ANTHROPIC_API_KEY 提供（环境变量优先）。
/// config.json 里有密钥，不要提交到 git、不要发到群里。
/// </summary>
public sealed class BotConfig
{
    [JsonPropertyName("telegramBotToken")] public string TelegramBotToken { get; set; } = "";
    [JsonPropertyName("anthropicApiKey")] public string AnthropicApiKey { get; set; } = "";
    /// <summary>允许使用机器人的群 / 私聊 id。为空时进入“设置模式”：只告诉你 chat id，不调用 Claude（防止陌生人消耗额度）。</summary>
    [JsonPropertyName("allowedChatIds")] public List<long> AllowedChatIds { get; set; } = new();
    [JsonPropertyName("model")] public string Model { get; set; } = "claude-sonnet-5-5";
    /// <summary>low / medium / high / xhigh / max。聊天用 low 就够，想答得更深可以调高（更慢、更贵）。</summary>
    [JsonPropertyName("effort")] public string Effort { get; set; } = "low";
    /// <summary>每个群记住最近多少条消息作为上下文</summary>
    [JsonPropertyName("historyMessages")] public int HistoryMessages { get; set; } = 80;
    [JsonPropertyName("maxTokens")] public int MaxTokens { get; set; } = 16000;
    /// <summary>作为背景知识的项目文档（相对 config.json 所在目录或绝对路径）</summary>
    [JsonPropertyName("projectDocs")] public List<string> ProjectDocs { get; set; } = new() { "../../CLAUDE.md", "../../Docs/GDD.md" };

    /// <summary>相对路径的基准：config.json 所在目录；没有 config.json 时用当前目录</summary>
    [JsonIgnore] public string BaseDir { get; private set; } = Directory.GetCurrentDirectory();

    public static BotConfig Load(out string? path)
    {
        path = new[] { Path.Combine(Directory.GetCurrentDirectory(), "config.json"), Path.Combine(AppContext.BaseDirectory, "config.json") }
            .FirstOrDefault(File.Exists);
        var cfg = path == null ? new BotConfig() : Parse(File.ReadAllText(path));
        if (path != null) cfg.BaseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var envTg = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (!string.IsNullOrWhiteSpace(envTg)) cfg.TelegramBotToken = envTg.Trim();
        var envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) cfg.AnthropicApiKey = envKey.Trim();
        return cfg;
    }

    public static BotConfig Parse(string json) =>
        JsonSerializer.Deserialize<BotConfig>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? new BotConfig();

    public string ResolvePath(string p) => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(BaseDir, p));

    public bool SetupMode => AllowedChatIds.Count == 0;
    public bool IsAllowed(long chatId) => AllowedChatIds.Contains(chatId);
}
