using System;

namespace Inn.Net
{
    /// <summary>
    /// 联机传输层接口。现在的实现是局域网直连 <see cref="LanTransport"/>（TCP + UDP 广播找房间）；
    /// 以后上 Steam 时新写一个 SteamTransport（Steam 大厅 + P2P）实现同样的接口即可，游戏逻辑不用改。
    /// 约定：连接 id 由传输层分配；主机端每个客户端一个 id，客户端只有一个连接（主机）。消息是完整的字节包（可靠、有序）。
    /// </summary>
    public interface ITransport : IDisposable
    {
        bool IsHost { get; }
        bool Connected { get; }        // 客户端：已连上主机；主机：在监听
        int ClientCount { get; }
        string Error { get; }
        string Address { get; }        // 给别人加入用的地址（局域网 = IP:端口；Steam = 大厅 id）

        bool TryJoined(out int conn);
        bool TryLeft(out int conn);
        bool TryReceive(out int conn, out byte[] data);
        void Send(int conn, byte[] msg);
        void Broadcast(byte[] msg);
        void Kick(int conn);
    }
}
