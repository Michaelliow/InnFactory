using UnityEngine;

namespace Inn
{
    /// <summary>2.5D 等距镜头：正交投影，俯角 30°、水平 45°，平滑跟随玩家；滚轮缩放</summary>
    public class IsoCamera : MonoBehaviour
    {
        public float pitch = 30, yaw = 45, distance = 40;
        public float size = 6.5f, minSize = 3.5f, maxSize = 12f;
        public Transform target;
        public Vector3? overrideFocus;   // 截图 / 测试用
        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        void LateUpdate()
        {
            if (target == null && InnGame.I != null && InnGame.I.player != null) target = InnGame.I.player.transform;
            if (!Input.GetKey(KeyCode.LeftShift) && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
                size = Mathf.Clamp(size - Input.mouseScrollDelta.y * 0.6f, minSize, maxSize);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime));
            var focus = overrideFocus ?? (target ? target.position : Vector3.zero);
            var rot = Quaternion.Euler(pitch, yaw, 0);
            var want = focus + Vector3.up * 0.5f - rot * Vector3.forward * distance;
            transform.rotation = rot;
            transform.position = overrideFocus.HasValue ? want : Vector3.Lerp(transform.position, want, 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime));
        }

        /// <summary>立即对准（不插值）</summary>
        public void Snap(Vector3 focus, float orthoSize)
        {
            overrideFocus = focus;
            size = orthoSize;
            if (cam == null) cam = GetComponent<Camera>();
            cam.orthographicSize = orthoSize;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.rotation = rot;
            transform.position = focus + Vector3.up * 0.5f - rot * Vector3.forward * distance;
        }
    }
}
