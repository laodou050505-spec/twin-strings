using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>八角笼的一面铁丝网：真的有粗细的菱形网（两组 45° 斜线，每根是细方管，交叉处上下交织），可以弹。
    /// 弹性：网后面有一张看不见的“膜”（cols×rows 个节点的二维弹簧网格：相邻节点互相拉 + 每个节点被拉回原位 + 阻尼，四边钉死），
    /// 撞一下 = 在撞击点附近给节点一个速度，波纹会扩散、回弹几下再停；手指 / 木偶顶着网 = 网被顶出一个凹坑。
    /// 每根金属丝的顶点按它在网面上的位置，从膜上插值出沿法线方向的位移。膜静止时不更新网格，省性能。
    /// 本地坐标：x 横向（-w/2..w/2），y 竖直（0..h），+z = 朝外（远离笼子中心）。</summary>
    public class SBNet : MonoBehaviour
    {
        public float w, h, cell = 0.023f, wire = 0.00065f;
        const int Cols = 18, Rows = 8;
        readonly float[] z = new float[Cols * Rows], v = new float[Cols * Rows];
        Mesh mesh; Vector3[] baseV, curV; Vector2[] uv01;   // uv01 = 顶点在网面上的归一化位置
        float energy; bool dirty;
        [System.NonSerialized] public float lean;   // 护栏把网的上沿往外带了多少（米）：网面按“左右两头 0、中间最大、越往上越大”跟着鼓
        float leanShown;

        public void Build(float width, float height, Material mat)
        {
            w = width; h = height;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            float diag = cell, fr = 0.0011f;   // fr = 框管半径
            // 1) 铁丝：两组 45° 斜线（圆截面，交叉处一上一下互锁），端点绕框管打卷边
            for (int dir = 0; dir < 2; dir++)
            {
                float sgn = dir == 0 ? 1f : -1f;
                for (float c = -h - w / 2f; c <= w / 2f + h; c += diag)
                {
                    float x0 = c, x1 = c + sgn * h, dx = x1 - x0, ta = 0, tb = 1;
                    float t0 = (-w / 2f - x0) / dx, t1 = (w / 2f - x0) / dx; if (t0 > t1) (t0, t1) = (t1, t0);
                    ta = Mathf.Max(ta, t0); tb = Mathf.Min(tb, t1);
                    if (tb - ta < 0.02f) continue;
                    var a = new Vector3(x0 + dx * ta, h * ta, 0); var b = new Vector3(x0 + dx * tb, h * tb, 0);
                    Wire(verts, norms, tris, uvs, a, b, dir);
                    Knuckle(verts, norms, tris, uvs, a, fr); Knuckle(verts, norms, tris, uvs, b, fr);
                }
            }
            // 2) 框：四边一圈钢管（稍微靠后）
            var corners = new[] { new Vector3(-w / 2f, 0, 0.0008f), new Vector3(w / 2f, 0, 0.0008f), new Vector3(w / 2f, h, 0.0008f), new Vector3(-w / 2f, h, 0.0008f) };
            for (int k = 0; k < 4; k++) Pipe(verts, norms, tris, uvs, corners[k], corners[(k + 1) % 4], fr, 6);
            // 3) 扎线卡扣：上下框每隔 2.2 厘米、左右框每隔 2 厘米一个（套在网和框管上的扁环）
            for (float x = -w / 2f + 0.016f; x < w / 2f - 0.012f; x += 0.022f) { Clip(verts, norms, tris, uvs, new Vector3(x, 0, 0.0008f), Vector3.right, fr); Clip(verts, norms, tris, uvs, new Vector3(x, h, 0.0008f), Vector3.right, fr); }
            // 左右两边藏在立柱里，不做卡扣
            baseV = verts.ToArray(); curV = verts.ToArray(); uv01 = uvs.ToArray();
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds(); mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.forward * 0.08f);
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Vector2 U(Vector3 p) => new((p.x + w / 2f) / w, p.y / h);

        /// <summary>沿一条折线做圆管（sides 边形截面）。</summary>
        void Path(List<Vector3> V, List<Vector3> N, List<int> T, List<Vector2> UV, IList<Vector3> pts, float r, int sides)
        {
            int start = V.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                var d = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                var s1 = Vector3.Cross(d, Mathf.Abs(d.z) > 0.9f ? Vector3.up : Vector3.forward).normalized; var s2 = Vector3.Cross(d, s1);
                for (int k = 0; k < sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides; var n = s1 * Mathf.Cos(ang) + s2 * Mathf.Sin(ang);
                    V.Add(pts[i] + n * r); N.Add(n); UV.Add(U(pts[i]));
                }
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a0 = start + i * sides + k, a1 = start + i * sides + (k + 1) % sides, b0 = a0 + sides, b1 = a1 + sides;
                    T.Add(a0); T.Add(b0); T.Add(a1); T.Add(a1); T.Add(b0); T.Add(b1);
                }
        }

        void Pipe(List<Vector3> V, List<Vector3> N, List<int> T, List<Vector2> UV, Vector3 a, Vector3 b, float r, int sides) => Path(V, N, T, UV, new[] { a, b }, r, sides);

        // 一根铁丝：每 3.5 毫米一个点，交叉处一上一下（正弦起伏），圆截面 6 边
        void Wire(List<Vector3> V, List<Vector3> N, List<int> T, List<Vector2> UV, Vector3 a, Vector3 b, int dir)
        {
            float len = (b - a).magnitude; int seg = Mathf.Max(2, Mathf.CeilToInt(len / 0.0035f));
            float half = cell / Mathf.Sqrt(2f) * 0.5f;
            var pts = new Vector3[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg, along = len * t;
                pts[i] = a + (b - a) * t + Vector3.forward * (Mathf.Sin(along / half * Mathf.PI * 0.5f + dir * Mathf.PI) * wire * 1.4f);
            }
            Path(V, N, T, UV, pts, wire, 6);
        }

        // 卷边结：铁丝端头绕框管弯 3/4 圈
        void Knuckle(List<Vector3> V, List<Vector3> N, List<int> T, List<Vector2> UV, Vector3 e, float fr)
        {
            bool horiz = e.y < 0.0005f || e.y > h - 0.0005f;
            if (!horiz) return;   // 左右两边的端头藏在立柱里，不打卷边（免得从立柱表面戳出来）
            var axis = horiz ? Vector3.right : Vector3.up;
            var c = new Vector3(e.x, horiz ? (e.y < h / 2 ? 0f : h) : e.y, 0.0008f);
            if (!horiz) c.x = e.x < 0 ? -w / 2f : w / 2f;
            var s1 = horiz ? Vector3.up : Vector3.right; var s2 = Vector3.forward;
            if (horiz && e.y > h / 2) s1 = -s1; if (!horiz && e.x > 0) s1 = -s1;
            float rr = fr + wire * 1.6f; var pts = new Vector3[8];
            for (int k = 0; k < 8; k++) { float ang = -Mathf.PI * 0.5f + k / 7f * Mathf.PI * 1.5f; pts[k] = c - s1 * Mathf.Sin(ang) * rr + s2 * Mathf.Cos(ang) * rr + axis * (k - 3.5f) * 0.00012f; }
            Path(V, N, T, UV, pts, wire * 0.95f, 5);
        }

        // 扎线卡扣：一个压扁的小环，套在框管和网上
        void Clip(List<Vector3> V, List<Vector3> N, List<int> T, List<Vector2> UV, Vector3 c, Vector3 along, float fr)
        {
            var s1 = along == Vector3.right ? Vector3.up : Vector3.right; var s2 = Vector3.forward; float rr = fr + 0.0011f;
            var pts = new Vector3[11];
            for (int k = 0; k <= 10; k++) { float ang = k / 10f * Mathf.PI * 2f; pts[k] = c + s1 * Mathf.Cos(ang) * rr * 1.35f + s2 * Mathf.Sin(ang) * rr; }
            Path(V, N, T, UV, pts, 0.00045f, 4);
        }

        /// <summary>撞一下：local = 网面上的本地点，strength = 往外的冲量（米/秒，负数 = 往里）。</summary>
        public void Hit(Vector3 local, float strength, float radius = 0.03f)
        {
            for (int j = 1; j < Rows - 1; j++)
                for (int i = 1; i < Cols - 1; i++)
                {
                    var p = Node(i, j); float d = Vector2.Distance(p, new Vector2(local.x, local.y));
                    if (d > radius * 2f) continue;
                    v[j * Cols + i] += strength * Mathf.Exp(-d * d / (radius * radius));
                }
            energy = 1f;
        }

        /// <summary>顶住网（手指 / 木偶）：local.z 就是顶到的深度（+ 往外）；网在附近被顶到这个深度。</summary>
        public void Press(Vector3 local, float radius)
        {
            for (int j = 1; j < Rows - 1; j++)
                for (int i = 1; i < Cols - 1; i++)
                {
                    var p = Node(i, j); float d = Vector2.Distance(p, new Vector2(local.x, local.y));
                    if (d > radius * 2f) continue;
                    float want = local.z * Mathf.Exp(-d * d / (radius * radius)); int k = j * Cols + i;
                    if ((want > 0 && z[k] < want) || (want < 0 && z[k] > want)) { z[k] = Mathf.Lerp(z[k], want, 0.6f); v[k] *= 0.5f; }
                }
            energy = 1f;
        }

        Vector2 Node(int i, int j) => new(-w / 2f + w * i / (Cols - 1f), h * j / (Rows - 1f));

        public bool InFace(Vector3 local, float margin) => Mathf.Abs(local.x) < w / 2f - margin && local.y > margin && local.y < h - margin;

        void Update()
        {
            if (energy <= 0 && Mathf.Abs(lean - leanShown) < 1e-5f) return;
            leanShown = lean;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            // 二维弹簧网格（显式积分，分 4 小步保证稳定）
            float dx = w / (Cols - 1f), kc = 0.9f / (dx * dx) * 0.0006f, ks = 260f, damp = 7f;
            float e = 0;
            for (int sub = 0; sub < 4; sub++)
            {
                float h4 = dt / 4f;
                for (int j = 1; j < Rows - 1; j++)
                    for (int i = 1; i < Cols - 1; i++)
                    {
                        int k = j * Cols + i;
                        float lap = z[k - 1] + z[k + 1] + z[k - Cols] + z[k + Cols] - 4f * z[k];
                        v[k] += (lap * kc * 1000f - ks * z[k] - damp * v[k]) * h4;
                    }
                for (int k = 0; k < z.Length; k++) { z[k] += v[k] * h4; z[k] = Mathf.Clamp(z[k], -0.03f, 0.03f); }
            }
            for (int k = 0; k < z.Length; k++) e += Mathf.Abs(z[k]) + Mathf.Abs(v[k]) * 0.05f;
            // 顶点位移 = 膜上双线性插值
            for (int n = 0; n < baseV.Length; n++)
            {
                var u = uv01[n];
                float fx = Mathf.Clamp(u.x, 0, 1) * (Cols - 1), fy = Mathf.Clamp(u.y, 0, 1) * (Rows - 1);
                int i0 = Mathf.Min((int)fx, Cols - 2), j0 = Mathf.Min((int)fy, Rows - 2); float tx = fx - i0, ty = fy - j0;
                float zz = Mathf.Lerp(Mathf.Lerp(z[j0 * Cols + i0], z[j0 * Cols + i0 + 1], tx), Mathf.Lerp(z[(j0 + 1) * Cols + i0], z[(j0 + 1) * Cols + i0 + 1], tx), ty);
                curV[n] = baseV[n] + Vector3.forward * (zz + lean * Mathf.Sin(Mathf.Clamp01(u.x) * Mathf.PI) * Mathf.Clamp01(u.y));
            }
            mesh.vertices = curV;
            dirty = true;
            if (e < 0.0004f && Mathf.Abs(lean) < 1e-5f)   // 停下来了：归零、不再更新
            {
                System.Array.Clear(z, 0, z.Length); System.Array.Clear(v, 0, v.Length);
                mesh.vertices = baseV; energy = 0; dirty = false;
            }
        }
    }
}
