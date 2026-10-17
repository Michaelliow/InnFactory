using System.Collections;
using UnityEngine;

namespace Inn
{
    /// <summary>NPC 移动基类：直线走向目标（原型不做寻路）</summary>
    public abstract class Walker : MonoBehaviour
    {
        public float speed = 2.5f;
        protected Transform model;
        float bob;

        protected InnGame Game => InnGame.I;

        protected void Step(Vector3 dir, float dist)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.position += dir.normalized * dist;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12 * Time.deltaTime);
            bob += dist * 6;
            if (model) model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(bob)) * 0.08f, 0);
        }

        public IEnumerator MoveTo(Vector3 target, float stop = 0.1f)
        {
            target.y = 0;
            while (true)
            {
                var d = target - transform.position; d.y = 0;
                if (d.magnitude <= stop) break;
                Step(d, Mathf.Min(d.magnitude, speed * Time.deltaTime));
                yield return null;
            }
            if (model) model.localPosition = Vector3.zero;
        }

        /// <summary>走到建筑旁边（离占地矩形 reach 米以内）</summary>
        public IEnumerator MoveTo(Building b, float reach = 0.8f)
        {
            while (b != null && b.DistanceTo(transform.position) > reach)
            {
                var p = b.ClosestPoint(transform.position);
                var d = p - transform.position; d.y = 0;
                Step(d, Mathf.Min(d.magnitude, speed * Time.deltaTime));
                yield return null;
            }
            if (b != null)
            {
                var face = b.Center - transform.position; face.y = 0;
                if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
            }
            if (model) model.localPosition = Vector3.zero;
        }
    }
}
