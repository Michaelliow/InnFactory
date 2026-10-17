using System.Text;
using InnBot;

// InnFactory 的 Telegram 群聊助手。
// 用法：
//   dotnet run                 正常运行（Ctrl+C 退出）
//   dotnet run -- --selftest   离线自测（不联网、不需要密钥）
Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--selftest")) return SelfTest.Run();

var cfg = BotConfig.Load(out var cfgPath);
Console.WriteLine($"配置文件：{cfgPath ?? "（没找到 config.json，只用环境变量）"}");
if (string.IsNullOrWhiteSpace(cfg.TelegramBotToken))
{
    Console.WriteLine("缺少 Telegram 机器人 token：把 config.example.json 复制成 config.json 并填写 telegramBotToken（找 @BotFather 创建机器人获得）。");
    return 1;
}
if (string.IsNullOrWhiteSpace(cfg.AnthropicApiKey))
{
    Console.WriteLine("缺少 Claude API key：在 config.json 填 anthropicApiKey，或设置环境变量 ANTHROPIC_API_KEY（在 console.anthropic.com 创建）。");
    return 1;
}

var system = ClaudeResponder.BuildSystemPrompt(cfg, out var loaded, out var missing);
foreach (var f in loaded) Console.WriteLine($"项目文档：{f}");
foreach (var f in missing) Console.WriteLine($"[警告] 找不到项目文档：{f}");

var tg = new TelegramApi(cfg.TelegramBotToken);
var claude = new ClaudeResponder(cfg, system);
var memory = new ChatMemory(cfg.HistoryMessages);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

long botId;
string botName;
try { (botId, botName) = await tg.GetMe(cts.Token); }
catch (Exception e)
{
    Console.WriteLine($"连不上 Telegram：{e.Message}\n检查 token 是否正确、这台电脑能否访问 api.telegram.org（可能需要代理）。");
    return 1;
}
Console.WriteLine($"机器人 @{botName} 已上线，模型 {cfg.Model}（effort {cfg.Effort}），按 Ctrl+C 退出。");
if (cfg.SetupMode)
    Console.WriteLine("[设置模式] allowedChatIds 为空：机器人只会回复 chat id。把要用的群 id 填进 config.json 后重启。");

long offset = 0;
while (!cts.IsCancellationRequested)
{
    System.Text.Json.Nodes.JsonArray updates;
    try { updates = await tg.GetUpdates(offset, cts.Token); }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
    catch (Exception e)
    {
        Console.WriteLine($"[Telegram] 拉取消息失败：{e.Message}，5 秒后重试");
        try { await Task.Delay(5000, cts.Token); } catch (OperationCanceledException) { break; }
        continue;
    }

    foreach (var u in updates)
    {
        if (u == null) continue;
        offset = Math.Max(offset, (long)u["update_id"]! + 1);
        var m = IncomingMessage.FromUpdate(u);
        if (m == null) continue;
        try { await Handle(m); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
        catch (Exception e) { Console.WriteLine($"[错误] 处理消息失败：{e.Message}"); }
    }
}
Console.WriteLine("已退出。");
return 0;

async Task Handle(IncomingMessage m)
{
    var trigger = ChatLogic.Classify(m, botName, botId, out var question);
    string where = m.IsPrivate ? $"私聊 {m.FromName}" : $"群「{m.ChatTitle}」";

    if (!cfg.IsAllowed(m.ChatId))
    {
        // 设置模式下告诉对方 chat id；正式运行时陌生的群 / 人一律不理（防止消耗 API 额度）
        if (cfg.SetupMode && trigger != Trigger.None)
        {
            Console.WriteLine($"[设置模式] {where} 的 chat id = {m.ChatId}");
            await tg.Reply(m.ChatId, m.MessageId, $"这个聊天的 chat id 是 {m.ChatId}\n把它填进机器人 config.json 的 allowedChatIds，重启机器人后就能用了。", cts.Token);
        }
        else if (trigger != Trigger.None) Console.WriteLine($"[忽略] 未授权的 {where}（chat id {m.ChatId}）");
        return;
    }

    // 记录群聊（作为之后回答的上下文）
    if (trigger != Trigger.Reset) memory.Add(m.ChatId, new ChatLine(m.Time, m.FromName, m.Text));

    switch (trigger)
    {
        case Trigger.None: return;
        case Trigger.Help:
            await tg.Reply(m.ChatId, m.MessageId,
                $"我是 InnFactory 的开发助手。@{botName} 加上你的问题，或者直接回复我的消息，我就会结合项目文档和最近的群聊回答。\n" +
                "/ask 问题 — 提问\n/reset — 让我忘掉之前的群聊记录\n/id — 显示这个聊天的 id", cts.Token);
            return;
        case Trigger.Id:
            await tg.Reply(m.ChatId, m.MessageId, $"chat id：{m.ChatId}", cts.Token);
            return;
        case Trigger.Reset:
            memory.Reset(m.ChatId);
            await tg.Reply(m.ChatId, m.MessageId, "好的，我已经忘掉之前的群聊记录了。", cts.Token);
            return;
    }

    Console.WriteLine($"[提问] {where} · {m.FromName}：{question}");
    await tg.Typing(m.ChatId, cts.Token);
    // 上下文里不重复放这条问题本身
    var history = memory.Get(m.ChatId).SkipLast(1);
    var prompt = ChatLogic.BuildPrompt(history, m.FromName, question);

    // Claude 想得久时每 4 秒续一次“正在输入”
    using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
    var typingLoop = Task.Run(async () =>
    {
        try { while (true) { await Task.Delay(4000, typingCts.Token); await tg.Typing(m.ChatId, typingCts.Token); } }
        catch { }
    });
    string answer;
    try { answer = await claude.Ask(prompt, cts.Token); }
    finally { typingCts.Cancel(); await typingLoop; }

    await tg.Reply(m.ChatId, m.MessageId, answer, cts.Token);
    memory.Add(m.ChatId, new ChatLine(DateTimeOffset.Now, "助手（你）", answer));
}
