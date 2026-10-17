using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Inn.Net
{
    /// <summary>
    /// 联机自动测试用的客户端机器人。打包后的游戏带 -nettest 日志路径 -join IP 启动时生效（正常游玩不会出现）：
    /// 连上主机 → 检查快照同步 → 买种子 → 走到耕地旁按 E 播种 → 在饭厅放一盆盆栽 → 写 BOT DONE，主机断开后自动退出。
    /// 每一步写一行 OK / FAIL 到日志，由编辑器里的 SmokeTest 汇总。
    /// </summary>
    public class NetBot : MonoBehaviour
    {
        string logPath;
        InnGame G => InnGame.I;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-nettest");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("NetBot");
            DontDestroyOnLoad(go);
            go.AddComponent<NetBot>().logPath = args[i + 1];
        }

        void Log(string line)
        {
            try { File.AppendAllText(logPath, line + "\n"); } catch { }
            Debug.Log("[NetBot] " + line);
        }

        void Check(bool ok, string what) => Log((ok ? "OK   " : "FAIL ") + what);

        IEnumerator Wait(System.Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        IEnumerator Start()
        {
            Application.targetFrameRate = 30;
            Application.runInBackground = true;
            try { File.WriteAllText(logPath, ""); } catch { }
            float started = Time.realtimeSinceStartup;
            StartCoroutine(Watchdog(started));
            yield return null;

            // 被拒（满员）或连上
            yield return Wait(() => G != null && (G.net.Rejected || G.net.snapshotsReceived >= 2), 30);
            if (G == null) { Log("FAIL 没有 InnGame"); Quit(); yield break; }
            G.net.Disconnected += _ => { Log("断开"); Quit(); };
            if (G.net.Rejected) { Log("REJECTED " + G.net.LastError); Quit(); yield break; }
            if (G.net.snapshotsReceived < 2) { Log("FAIL 连接主机超时：" + G.net.LastError); Quit(); yield break; }

            int me = G.net.LocalId;
            Log($"CONNECTED id={me}");
            Check(G.Replica, "进入客户端模式（不在本地模拟）");
            Check(G.buildings.Count >= 22 && G.chairs.Count >= 4, $"收到主机的建筑 {G.buildings.Count} 个");
            Check(G.players.Count >= 2 && G.players.Any(p => p.netId == 1), $"看到其他掌柜 {G.players.Count} 人（含主机）");
            Check(G.discovered.Count >= 3, $"菜谱同步 {G.discovered.Count} 道（主机发现过蛋炒饭）");

            // 本地移动
            var p0 = G.player.transform.position;
            InnInput.SimMove = new Vector2(1, 0);
            yield return new WaitForSecondsRealtime(0.4f);
            InnInput.SimMove = Vector2.zero;
            Check(Vector3.Distance(p0, G.player.transform.position) > 0.5f, "本地移动");

            // 买种子（钱从公共金库扣，种子进自己的背包）
            var seed = G.db.Item("seed_scallion");
            int before = G.player.bag.Count(seed);
            G.net.BuySeed(G.db.Crop("scallion"), 5);
            yield return Wait(() => G.player.bag.Count(seed) >= before + 5, 10);
            Check(G.player.bag.Count(seed) >= before + 5, $"买葱种子 ×5 进了自己的背包（{G.player.bag.Count(seed)}）");

            // 播种：每个机器人一块地
            var plots = G.plots.Where(p => p.cell.y == 5 || p.cell.y == 6).Where(p => p.cell.x >= 2 && p.cell.x <= 4).OrderBy(p => p.netId).ToList();
            var plot = plots.Count > 0 ? plots[(me - 2 + plots.Count) % plots.Count] : null;
            if (plot == null) { Check(false, "找不到耕地"); }
            else
            {
                // 站在地的外侧（第 5 行站下面，第 6 行站上面）
                var stand = new Vector3(plot.cell.x + 0.5f, 0, plot.cell.y == 5 ? plot.cell.y - 0.5f : plot.cell.y + 1.5f);
                G.player.transform.position = stand;
                G.player.transform.rotation = Quaternion.LookRotation(plot.Center - stand);
                G.player.Select(seed);
                yield return new WaitForSecondsRealtime(0.3f);   // 让主机收到新位置
                Check(G.player.target == plot, $"交互目标是耕地 {plot.cell}");
                InnInput.Press(KeyCode.E);
                yield return Wait(() => plot != null && plot.crop != null, 10);
                Check(plot != null && plot.crop != null && plot.crop.id == "scallion", $"按 E 播种 → 主机执行 → 快照同步回来（{plot.cell}）");
            }

            // 建造：饭厅放盆栽
            var cell = new Vector2Int(22 + me, 8);
            G.net.Place(G.db.Buildable("plant"), cell, 0);
            yield return Wait(() => G.grid.At(cell) != null, 10);
            Check(G.grid.At(cell) != null && G.grid.At(cell).def.id == "plant", $"客户端建造盆栽 {cell} 同步成功");

            yield return new WaitForSecondsRealtime(1f);
            Check(G.players.Count(p => !p.isLocal) >= 1, $"同屏掌柜 {G.players.Count} 人");
            Log("BOT DONE");
        }

        IEnumerator Watchdog(float started)
        {
            while (Time.realtimeSinceStartup - started < 180) yield return null;
            Log("TIMEOUT");
            Quit();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
