using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>键盘输入的统一入口。自动测试通过 Press / SimMove 模拟按键（走真实的按键处理代码）。</summary>
    public static class InnInput
    {
        static readonly HashSet<KeyCode> queued = new HashSet<KeyCode>();
        static readonly HashSet<KeyCode> active = new HashSet<KeyCode>();
        public static Vector2 SimMove;

        /// <summary>模拟按下一次（下一帧生效，持续一帧）</summary>
        public static void Press(KeyCode k) => queued.Add(k);

        /// <summary>每帧最先调用（InnGame 的执行顺序最早）</summary>
        public static void BeginFrame()
        {
            active.Clear();
            foreach (var k in queued) active.Add(k);
            queued.Clear();
        }

        public static bool Down(KeyCode k) => (!Typing && Input.GetKeyDown(k)) || active.Contains(k);

        /// <summary>界面上的文本框有焦点（HUD 每帧写入）</summary>
        public static bool Typing;

        public static Vector2 Move
        {
            get
            {
                var v = SimMove;
                if (Typing) return v;
                if (Input.GetKey(KeyCode.W)) v.y += 1;
                if (Input.GetKey(KeyCode.S)) v.y -= 1;
                if (Input.GetKey(KeyCode.D)) v.x += 1;
                if (Input.GetKey(KeyCode.A)) v.x -= 1;
                return Vector2.ClampMagnitude(v, 1);
            }
        }
    }
}
