using UnityEngine;

namespace StringBrawl
{
    /// <summary>会飘的布旗：一张 cols×rows 的细分网格，挂在旗杆一侧（本地 x = 0 是靠杆那边、y = 0 是顶边往下）。
    /// 每帧按行波变形：离旗杆越远摆得越大，波从杆往外走；下摆再加一点前后荡。只做一面网格，材质关背面剔除，两面都看得见。</summary>
    public class SBFlag : MonoBehaviour
    {
        public float w = 0.07f, h = 0.12f, amp = 0.012f, speed = 3.2f;
        public bool topHung;   // true = 整幅挂在横杆下面（顶边固定，越往下摆得越大，波从上往下走）
        const int Cols = 12, Rows = 16;
        Mesh mesh; Vector3[] baseV, cur; float ph;

        public void Build(Material mat)
        {
            ph = Random.value * 10f;
            var v = new Vector3[(Cols + 1) * (Rows + 1)]; var uv = new Vector2[v.Length]; var tris = new int[Cols * Rows * 6];   // 只做一面，材质关掉背面剔除（两面共用顶点会让法线抵消、出花斑）
            for (int j = 0; j <= Rows; j++)
                for (int i = 0; i <= Cols; i++)
                {
                    int k = j * (Cols + 1) + i;
                    v[k] = new Vector3(w * i / Cols, -h * j / Rows, 0); uv[k] = new Vector2(i / (float)Cols, 1f - j / (float)Rows);
                }
            int t = 0;
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    int a = j * (Cols + 1) + i, b = a + 1, c = a + Cols + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = b; tris[t++] = d; tris[t++] = c;   // 正面

                }
            baseV = v; cur = (Vector3[])v.Clone();
            mesh = new Mesh(); mesh.vertices = v; mesh.uv = uv; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.MarkDynamic();
            mesh.bounds = new Bounds(new Vector3(w / 2, -h / 2, 0), new Vector3(w * 1.2f, h * 1.2f, amp * 6f));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void Update()
        {
            if (mesh == null) return;
            float t = Time.time * speed + ph;
            for (int k = 0; k < baseV.Length; k++)
            {
                var p = baseV[k]; float fx = p.x / w, fy = -p.y / h;
                if (topHung)
                {
                    float wv = (Mathf.Sin(fy * 4.2f - t + fx * 1.3f) * 0.75f + Mathf.Sin(fy * 7f - t * 1.6f + fx * 3f) * 0.25f) * amp * fy;
                    float sway = Mathf.Sin(t * 0.45f) * amp * 0.6f * fy;
                    // 旗面挂在旗杆前面约 6 毫米：往后（+z，朝旗杆）摆的幅度平滑地压在 3 毫米以内，永远碰不到旗杆；往前摆不限
                    float z = wv + sway; const float zBack = 0.003f; if (z > 0f) z = zBack * (1f - Mathf.Exp(-z / zBack));
                    cur[k] = new Vector3(p.x + Mathf.Sin(t * 0.5f + fy * 2f) * amp * 0.25f * fy * (fx - 0.5f), p.y + Mathf.Abs(wv) * 0.12f, z);
                    continue;
                }
                float wave = Mathf.Sin(fx * 5.5f - t) * amp * fx + Mathf.Sin(fx * 9f - t * 1.7f + fy * 2f) * amp * 0.35f * fx;
                float sag = fx * fx * h * 0.035f;                                     // 远端稍微往下垂
                cur[k] = new Vector3(p.x - Mathf.Abs(wave) * 0.15f, p.y - sag, wave + Mathf.Sin(t * 0.7f + fy) * amp * 0.25f * fy);
            }
            mesh.vertices = cur; mesh.RecalculateNormals();
        }
    }
}
