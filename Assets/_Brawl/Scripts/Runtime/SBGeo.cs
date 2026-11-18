using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>程序建模的拼装器：往里加方块、圆柱 / 棱柱、车削体（截面绕竖轴转一圈）、八角环带、四边形，最后合成一张网格（一次绘制）。
    /// 规则的结构件（立柱、护栏、灯具、台阶、看台、座椅）都用它做：边缘精确、没有破面，比生成模型干净。</summary>
    public class SBGeo
    {
        readonly List<Vector3> v = new(); readonly List<Vector3> n = new(); readonly List<Vector2> uv = new(); readonly List<int> t = new();
        public Matrix4x4 M = Matrix4x4.identity;   // 之后加进来的东西都先经过这个变换

        int V(Vector3 p, Vector3 nn, Vector2 u) { v.Add(M.MultiplyPoint3x4(p)); n.Add(M.MultiplyVector(nn).normalized); uv.Add(u); return v.Count - 1; }
        void Tri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }
        /// <summary>按法线自动定朝向（Unity 里正面 = Cross(b-a, c-a) 指向的那边）。</summary>
        void TriN(int a, int b, int c)
        {
            var nn = n[a] + n[b] + n[c];
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), nn) < 0) Tri(a, c, b); else Tri(a, b, c);
        }

        /// <summary>四边形 a b c d（逆时针看是正面），法线 nn。</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn, Vector2 u0 = default, Vector2 u1 = default)
        {
            if (u1 == default) u1 = Vector2.one;
            int i0 = V(a, nn, new(u0.x, u0.y)), i1 = V(b, nn, new(u1.x, u0.y)), i2 = V(c, nn, new(u1.x, u1.y)), i3 = V(d, nn, new(u0.x, u1.y));
            TriN(i0, i1, i2); TriN(i0, i2, i3);
        }

        /// <summary>单个三角形（法线 nn 一侧为正面）。</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 nn)
        {
            int i0 = V(a, nn, new(0, 1)), i1 = V(b, nn, new(1, 1)), i2 = V(c, nn, new(.5f, 0));
            TriN(i0, i1, i2);
        }

        /// <summary>方块：center、尺寸、旋转。</summary>
        public void Box(Vector3 c, Vector3 s, Quaternion r = default, float uvScale = 0f)
        {
            if (r == default) r = Quaternion.identity;
            var h = s * 0.5f;
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            for (int k = 0; k < 3; k++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    var nn = ax[k] * sg; var u = ax[(k + 1) % 3]; var w = ax[(k + 2) % 3];
                    if (sg < 0) (u, w) = (w, u);
                    var hu = Vector3.Scale(u, h); var hw = Vector3.Scale(w, h); var hn = Vector3.Scale(nn, h);
                    float su = uvScale > 0 ? (hu.magnitude * 2f) / uvScale : 1f, sw = uvScale > 0 ? (hw.magnitude * 2f) / uvScale : 1f;
                    Quad(c + r * (hn - hu - hw), c + r * (hn + hu - hw), c + r * (hn + hu + hw), c + r * (hn - hu + hw), r * nn, Vector2.zero, new Vector2(su, sw));
                }
        }

        /// <summary>车削体：profile = (半径, 高度) 从下到上，绕 y 轴转 seg 段（seg = 8 就是八角形）。capTop / capBottom = 封顶 / 封底。</summary>
        public void Lathe(Vector3 c, IList<Vector2> profile, int seg, float phase = 0f, bool capTop = true, bool capBottom = true, float uTile = 1f)
        {
            int rows = profile.Count;
            // 每段独立顶点（八角形要硬边）；圆的（seg ≥ 16）共用法线显得光滑
            bool smooth = seg >= 16;
            float vlen = 0; var vacc = new float[rows];
            for (int i = 1; i < rows; i++) { vlen += (profile[i] - profile[i - 1]).magnitude; vacc[i] = vlen; }
            for (int s = 0; s < seg; s++)
            {
                float a0 = phase + s * Mathf.PI * 2f / seg, a1 = phase + (s + 1) * Mathf.PI * 2f / seg;
                var d0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)); var d1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                var dm = (d0 + d1).normalized;
                for (int i = 0; i < rows - 1; i++)
                {
                    Vector2 p = profile[i], q = profile[i + 1];
                    var slope = new Vector2(q.y - p.y, -(q.x - p.x)).normalized;   // 截面法线（向外为正）
                    Vector3 N0 = smooth ? (d0 * slope.x + Vector3.up * slope.y) : (dm * slope.x + Vector3.up * slope.y);
                    Vector3 N1 = smooth ? (d1 * slope.x + Vector3.up * slope.y) : N0;
                    float u0 = s / (float)seg * uTile, u1 = (s + 1) / (float)seg * uTile;
                    int i0 = V(c + d0 * p.x + Vector3.up * p.y, N0, new(u0, vacc[i] / Mathf.Max(vlen, 1e-5f)));
                    int i1 = V(c + d1 * p.x + Vector3.up * p.y, N1, new(u1, vacc[i] / Mathf.Max(vlen, 1e-5f)));
                    int i2 = V(c + d1 * q.x + Vector3.up * q.y, N1, new(u1, vacc[i + 1] / Mathf.Max(vlen, 1e-5f)));
                    int i3 = V(c + d0 * q.x + Vector3.up * q.y, N0, new(u0, vacc[i + 1] / Mathf.Max(vlen, 1e-5f)));
                    TriN(i0, i1, i2); TriN(i0, i2, i3);
                }
                if (capTop && profile[rows - 1].x > 1e-5f)
                {
                    var tp = profile[rows - 1];
                    int a = V(c + Vector3.up * tp.y, Vector3.up, new(.5f, .5f)), b = V(c + d0 * tp.x + Vector3.up * tp.y, Vector3.up, new(.5f, 1)), e = V(c + d1 * tp.x + Vector3.up * tp.y, Vector3.up, new(1, .5f));
                    TriN(a, b, e);
                }
                if (capBottom && profile[0].x > 1e-5f)
                {
                    var bp = profile[0];
                    int a = V(c + Vector3.up * bp.y, Vector3.down, new(.5f, .5f)), b = V(c + d0 * bp.x + Vector3.up * bp.y, Vector3.down, new(.5f, 1)), e = V(c + d1 * bp.x + Vector3.up * bp.y, Vector3.down, new(1, .5f));
                    TriN(a, e, b);
                }
            }
        }

        /// <summary>圆柱 / 棱柱：从 a 到 b，半径 r，seg 段。</summary>
        public void Cyl(Vector3 a, Vector3 b, float r, int seg = 12, bool caps = true)
        {
            var save = M;
            var d = b - a; float len = d.magnitude; if (len < 1e-6f) return;
            M = save * Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one);
            Lathe(Vector3.zero, new[] { new Vector2(r, 0), new Vector2(r, len) }, seg, 0f, caps, caps);
            M = save;
        }

        /// <summary>八角环带：中心线在外接圆半径 R 的八角形上（顶点在 22.5° + k·45°），径向宽 w、从 y0 到 y1，转角斜接（不重叠不漏缝）。
        /// from / to = 只做一部分边（按边的序号，to 不含）。u 沿周长按 uScale 米一格。</summary>
        public void OctRing(float R, float w, float y0, float y1, int from = 0, int to = 8, float uScale = 0.05f, bool inner = true, bool outer = true, bool top = true, bool bottom = true, bool ends = false)
        {
            float k = 1f / Mathf.Cos(Mathf.PI / 8f);
            float Ri = R - w / 2f * k, Ro = R + w / 2f * k;
            float u = 0;
            for (int i = from; i < to; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f;
                var d0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)); var d1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)); var nm = new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am));
                float len = (d1 - d0).magnitude * R, u1 = u + len / uScale;
                Vector3 I0 = d0 * Ri, I1 = d1 * Ri, O0 = d0 * Ro, O1 = d1 * Ro, Y0 = Vector3.up * y0, Y1 = Vector3.up * y1;
                float hv = (y1 - y0) / uScale, wv = w / uScale;
                if (outer) Quad(O0 + Y0, O1 + Y0, O1 + Y1, O0 + Y1, nm, new(u, 0), new(u1, hv));
                if (inner) Quad(I1 + Y0, I0 + Y0, I0 + Y1, I1 + Y1, -nm, new(u1, 0), new(u, hv));
                if (top) Quad(O0 + Y1, O1 + Y1, I1 + Y1, I0 + Y1, Vector3.up, new(u, 0), new(u1, wv));
                if (bottom) Quad(I0 + Y0, I1 + Y0, O1 + Y0, O0 + Y0, Vector3.down, new(u, 0), new(u1, wv));
                if (ends && i == from) { var nn = Vector3.Cross(Vector3.up, d0).normalized; Quad(I0 + Y0, I0 + Y1, O0 + Y1, O0 + Y0, -Vector3.Cross(d0, Vector3.up), new(0, 0), new(wv, hv)); }
                if (ends && i == to - 1) Quad(O1 + Y0, O1 + Y1, I1 + Y1, I1 + Y0, Vector3.Cross(d1, Vector3.up), new(0, 0), new(wv, hv));
                u = u1;
            }
        }

        static float Sp(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);

        /// <summary>超椭球（圆角的“鼓包”）：中心 c、三轴半径 h、旋转 r；n = 2 是椭球，越大越方。法线用隐函数梯度，光滑无硬边。</summary>
        public void Blob(Vector3 c, Vector3 h, Quaternion r, float n = 2.4f, int segU = 14, int segV = 22)
        {
            float e = 2f / n; int b = v.Count;
            for (int i = 0; i <= segU; i++)
            {
                float u = -Mathf.PI / 2f + Mathf.PI * i / segU; float cu = Mathf.Cos(u), su = Mathf.Sin(u);
                for (int j = 0; j <= segV; j++)
                {
                    float w = -Mathf.PI + 2f * Mathf.PI * j / segV;
                    var p = new Vector3(h.x * Sp(cu, e) * Sp(Mathf.Cos(w), e), h.y * Sp(su, e), h.z * Sp(cu, e) * Sp(Mathf.Sin(w), e));
                    var g = new Vector3(Sp(p.x / h.x, n - 1f) / h.x, Sp(p.y / h.y, n - 1f) / h.y, Sp(p.z / h.z, n - 1f) / h.z);
                    if (g.sqrMagnitude < 1e-12f) g = new Vector3(0, Mathf.Sign(su), 0);
                    V(c + r * p, r * g.normalized, new Vector2(j / (float)segV, i / (float)segU));
                }
            }
            for (int i = 0; i < segU; i++)
                for (int j = 0; j < segV; j++)
                {
                    int a0 = b + i * (segV + 1) + j, a1 = a0 + 1, c0 = a0 + segV + 1, c1 = c0 + 1;
                    TriN(a0, a1, c1); TriN(a0, c1, c0);
                }
        }

        /// <summary>沿折线的细管（缝线滚边 / 系带）：pts 是点，r 半径，closed = 首尾相连。</summary>
        public void Tube(IList<Vector3> pts, float r, int sides = 6, bool closed = false)
        {
            int m = pts.Count; if (m < 2) return;
            int b = v.Count; var prevN = Vector3.zero;
            for (int i = 0; i < m; i++)
            {
                Vector3 t = closed ? pts[(i + 1) % m] - pts[(i - 1 + m) % m] : pts[Mathf.Min(i + 1, m - 1)] - pts[Mathf.Max(i - 1, 0)];
                t.Normalize();
                var nn = i == 0 ? Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right).normalized : Vector3.ProjectOnPlane(prevN, t).normalized;
                prevN = nn; var bb = Vector3.Cross(t, nn);
                for (int k = 0; k <= sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides; var d = nn * Mathf.Cos(a) + bb * Mathf.Sin(a);
                    V(pts[i] + d * r, d, new Vector2(k / (float)sides, i / (float)(m - 1)));
                }
            }
            int rows = closed ? m : m - 1;
            for (int i = 0; i < rows; i++)
                for (int k = 0; k < sides; k++)
                {
                    int i1 = (i + 1) % m;
                    int a0 = b + i * (sides + 1) + k, a1 = a0 + 1, c0 = b + i1 * (sides + 1) + k, c1 = c0 + 1;
                    TriN(a0, a1, c1); TriN(a0, c1, c0);
                }
        }

        public bool Empty => v.Count == 0;

        public Mesh Mesh()
        {
            var m = new Mesh { indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>直接做成物体挂在 parent 下。</summary>
        public GameObject Make(Transform parent, string name, Material mat)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = Mesh();
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }
    }
}
