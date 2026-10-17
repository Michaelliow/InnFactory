# InnFactory Telegram 群聊助手

在 Telegram 群里 @它 或回复它，它会结合项目文档（`CLAUDE.md`、`Docs/GDD.md`）和群里最近的聊天回答问题。
用 Claude Sonnet 5.5，跑在你自己的电脑上（C# / .NET 10），电脑开着、程序开着机器人才在线。

## 第一次设置（约 10 分钟）

1. **创建机器人**：Telegram 里找 `@BotFather` → 发 `/newbot` → 起名字和用户名（必须以 `bot` 结尾）→ 得到 token。
2. **让它能看到群聊**（否则它只看得到 @它 的消息，没法参考上下文）：
   `@BotFather` → `/setprivacy` → 选你的机器人 → `Disable`。改完要**把机器人移出群再重新拉进来**才生效。
3. **Claude API key**：到 https://console.anthropic.com 创建 API key 并充值（按用量计费，和 Claude 订阅是分开的）。
4. **填配置**：把 `config.example.json` 复制成 `config.json`，填 `telegramBotToken` 和 `anthropicApiKey`，`allowedChatIds` 先留空。
5. 双击 `启动机器人.bat`。把机器人拉进群，在群里发一句 `@你的机器人 你好`，它会回复这个群的 chat id。
6. 把 chat id（负数）填进 `config.json` 的 `allowedChatIds`，关掉窗口重新双击启动。完成。

## 群里怎么用

- `@机器人 鸡舍造价 80 是不是太便宜了？` — 提问
- 直接**回复**机器人的消息 — 追问
- `/ask 问题` — 也可以提问
- `/reset` — 让它忘掉之前的群聊记录
- `/id` — 显示 chat id
- 私聊机器人也行（私聊的 chat id 同样要加进 `allowedChatIds`）

## 说明

- 群聊记录只在内存里（最近 80 条），**不会写到文件里**，重启就清空。
- 它**不能改代码**；需要改工程时，它会把要改什么写清楚，你再交给 Claude Code。
- 项目文档每次启动时读一次；文档更新后重启机器人即可。
- 文档内容开启了提示缓存，频繁提问时文档部分按缓存价计费。每次回答在窗口里会打印用了多少 token。
- 只有 `allowedChatIds` 里的群 / 人能用，陌生人加了机器人也调用不了 Claude。
- 访问 `api.telegram.org` 或 Claude API 需要代理的话，Windows 的系统代理设置会自动生效。

## 开发

```
dotnet run -- --selftest   离线自测（不联网、不需要密钥）
dotnet run                 运行
```

代码：`Program.cs`（主循环）、`ChatLogic.cs`（何时回应、上下文拼接、长消息拆分）、`ClaudeResponder.cs`（Claude 请求）、
`TelegramApi.cs`（Telegram Bot API）、`BotConfig.cs`（配置）、`SelfTest.cs`。
