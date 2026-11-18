using UnityEngine;

namespace StringBrawl
{
    /// <summary>木偶脚下的软影子（压暗台面的一个圆斑）：跟着木偶走，跳起来变小变淡、倒地时拉长一点。</summary>
    public class SBBlob : MonoBehaviour
    {
        public SBPuppet p;
        public Material mat;
        Transform q; Material m;

        void Start()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(go.GetComponent<Collider>());
            go.name = "影子"; q = go.transform; q.SetParent(transform, false);
            m = new Material(mat); go.GetComponent<Renderer>().material = m;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LateUpdate()
        {
            if (!p || !p.Ready || !q) return;
            var r = p.Root.position;
            float up = Mathf.Clamp01(p.Lift / (p.H * 0.8f));
            float w = p.H * p.Kit.torsoSize.x * 1.9f * (1f - up * 0.4f);
            q.position = new Vector3(r.x, p.floorY + 0.0015f, r.z);
            q.rotation = Quaternion.Euler(90, p.Root.eulerAngles.y, 0);
            q.localScale = new Vector3(w, w * (p.KO ? 1.6f : 1.1f), 1);
            m.SetFloat("_Strength", 0.55f * (1f - up * 0.6f));
        }

        void OnDestroy() { if (q) Destroy(q.gameObject); }
    }
}
