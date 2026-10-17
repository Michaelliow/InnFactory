using System.Text.Json.Nodes;

namespace InnBot;

/// <summary>离线自测：不联网、不需要密钥，检查消息判断、上下文拼接、长消息拆分、配置读取、项目文档加载</summary>
public static class SelfTest
{
    static int fails;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
        if (!ok) fails++;
    }

    static IncomingMessage Msg(string text, string chatType = "supergroup", long? replyTo = null, string from = "小明")
    {
        var node = new JsonObject
        {
            ["update_id"] = 1,
            ["message"] = new JsonObject
            {
                ["message_id"] = 42, ["date"] = 1760000000, ["text"] = text,
                ["chat"] = new JsonObject { ["id"] = -100123, ["type"] = chatType, ["title"] = "客栈开发组" },
                ["from"] = new JsonObject { ["id"] = 7, ["first_name"] = from },
            },
        };
        if (replyTo != null) node["message"]!["reply_to_message"] = new JsonObject { ["from"] = new JsonObject { ["id"] = replyTo } };
        return IncomingMessage.FromUpdate(JsonNode.Parse(node.ToJsonString())!)!;   // 和真实输入一样从文本解析
    }

    public static int Run()
    {
        const string bot = "InnHelperBot";
        const long botId = 999;
        Console.WriteLine("== 消息解析");
        var m = Msg("@InnHelperBot 鸡舍多少钱？");
        Check(m.ChatId == -100123 && m.FromName == "小明" && m.MessageId == 42 && !m.IsPrivate, "解析群消息字段");
        Check(IncomingMessage.FromUpdate(new JsonObject { ["update_id"] = 2 }) == null, "非消息更新被忽略");

        Console.WriteLine("== 什么时候回应");
        Check(ChatLogic.Classify(m, bot, botId, out var q) == Trigger.Ask && q == "鸡舍多少钱？", "群里 @机器人 → 回答，问题去掉 @");
        Check(ChatLogic.Classify(Msg("@innhelperbot 你好"), bot, botId, out _) == Trigger.Ask, "@ 不区分大小写");
        Check(ChatLogic.Classify(Msg("今天谁做美术？"), bot, botId, out _) == Trigger.None, "群里普通聊天 → 不回答（只记录）");
        Check(ChatLogic.Classify(Msg("那传送带呢", replyTo: botId), bot, botId, out q) == Trigger.Ask && q == "那传送带呢", "回复机器人的消息 → 回答");
        Check(ChatLogic.Classify(Msg("那传送带呢", replyTo: 5), bot, botId, out _) == Trigger.None, "回复别人的消息 → 不回答");
        Check(ChatLogic.Classify(Msg("联机怎么开房间", "private"), bot, botId, out _) == Trigger.Ask, "私聊 → 回答");
        Check(ChatLogic.Classify(Msg("/ask 菜谱有几道"), bot, botId, out q) == Trigger.Ask && q == "菜谱有几道", "/ask 命令");
        Check(ChatLogic.Classify(Msg("/ask@InnHelperBot 菜谱有几道"), bot, botId, out q) == Trigger.Ask && q == "菜谱有几道", "/ask@机器人 命令");
        Check(ChatLogic.Classify(Msg("/ask@OtherBot 菜谱"), bot, botId, out _) == Trigger.None, "发给别的机器人的命令不理");
        Check(ChatLogic.Classify(Msg("/help"), bot, botId, out _) == Trigger.Help, "/help");
        Check(ChatLogic.Classify(Msg("/start", "private"), bot, botId, out _) == Trigger.Help, "/start（私聊第一次打开）");
        Check(ChatLogic.Classify(Msg("/id"), bot, botId, out _) == Trigger.Id, "/id");
        Check(ChatLogic.Classify(Msg("/reset"), bot, botId, out _) == Trigger.Reset, "/reset");
        Check(ChatLogic.Classify(Msg("@InnHelperBot"), bot, botId, out _) == Trigger.Help, "只 @ 不问 → 显示帮助");

        Console.WriteLine("== 群聊记忆");
        var mem = new ChatMemory(3);
        for (int i = 0; i < 5; i++) mem.Add(1, new ChatLine(DateTimeOffset.Now, "A", "消息" + i));
        mem.Add(2, new ChatLine(DateTimeOffset.Now, "B", "另一个群"));
        var h = mem.Get(1);
        Check(h.Count == 3 && h[0].Text == "消息2" && h[2].Text == "消息4", "只保留最近 N 条");
        Check(mem.Get(2).Count == 1, "不同群分开记");
        mem.Add(1, new ChatLine(DateTimeOffset.Now, "A", new string('长', 5000)));
        Check(mem.Get(1)[^1].Text.Length < 2100, "超长消息截断");
        mem.Reset(1);
        Check(mem.Get(1).Count == 0, "/reset 清空");

        Console.WriteLine("== 发给 Claude 的内容");
        var prompt = ChatLogic.BuildPrompt(new[] { new ChatLine(DateTimeOffset.Now, "小红", "我觉得鸡舍太贵") }, "小明", "鸡舍价格合理吗？");
        Check(prompt.Contains("<recent_chat>") && prompt.Contains("小红: 我觉得鸡舍太贵") && prompt.EndsWith("鸡舍价格合理吗？"), "群聊记录 + 提问");
        Check(!ChatLogic.BuildPrompt(Array.Empty<ChatLine>(), "小明", "hi").Contains("<recent_chat>"), "没有记录时不加空标签");

        Console.WriteLine("== 长消息拆分");
        var longText = string.Join("\n", Enumerable.Range(0, 400).Select(i => $"第 {i} 行：客栈经营自动化传送带"));
        var parts = ChatLogic.Split(longText);
        Check(parts.Count >= 2 && parts.All(p => p.Length <= ChatLogic.TelegramLimit), $"拆成 {parts.Count} 条，每条 ≤ {ChatLogic.TelegramLimit} 字");
        Check(string.Join("\n", parts) == longText.Trim(), "拆分后内容不丢");
        Check(ChatLogic.Split(new string('字', 9000)).All(p => p.Length <= ChatLogic.TelegramLimit), "没有换行的长文本也能拆");

        Console.WriteLine("== 配置");
        var cfg = BotConfig.Parse("""
            { // 注释
              "telegramBotToken": "x", "allowedChatIds": [-100123, 55], "model": "claude-sonnet-5-5",
            }
            """);
        Check(cfg.IsAllowed(-100123) && cfg.IsAllowed(55) && !cfg.IsAllowed(1) && !cfg.SetupMode, "白名单");
        Check(cfg.Effort == "low" && cfg.HistoryMessages == 80 && cfg.MaxTokens == 16000, "默认值");
        Check(BotConfig.Parse("{}").SetupMode, "白名单为空 → 设置模式（不调用 Claude）");

        Console.WriteLine("== 项目文档");
        var real = BotConfig.Load(out _);
        var sys = ClaudeResponder.BuildSystemPrompt(real, out var loaded, out var missing);
        Check(loaded.Count == real.ProjectDocs.Count && missing.Count == 0, $"加载项目文档 {loaded.Count}/{real.ProjectDocs.Count}{(missing.Count > 0 ? "，缺：" + string.Join("、", missing) : "")}");
        Check(sys.Contains("InnFactory") && sys.Length > 2000, $"系统提示 {sys.Length} 字");

        Console.WriteLine("== Claude 请求参数");
        var json = ClaudeResponder.BuildParams(new BotConfig(), "SYS", "Q").ToString().Replace(" ", "").Replace("\n", "").Replace("\r", "");
        Check(json.Contains("\"model\":\"claude-sonnet-5-5\""), "模型 claude-sonnet-5-5");
        Check(json.Contains("\"fallbacks\":\"default\""), "fallbacks = \"default\"");
        Check(json.Contains("\"effort\":\"low\""), "effort = low");
        Check(json.Contains("\"cache_control\":{\"type\":\"ephemeral\"}"), "系统提示开启缓存");
        if (fails > 0) Console.WriteLine("  请求体：" + json[..Math.Min(600, json.Length)]);

        Console.WriteLine(fails == 0 ? "RESULT: PASS" : $"RESULT: {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }
}
