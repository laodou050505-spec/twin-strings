using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>生成的木偶部件（Tripo P2 → Blender 清理 → FBX + 贴图，放在 Resources/Parts/&lt;角色_部件&gt;/）：
    ///   mesh0.fbx 高精度（头 / 身子用最高档，四肢 / 拳套用中档）、mesh.fbx 低一档；albedo.png 颜色；normal.png 亮度图（导入时转法线贴图）。
    /// 读进来后先“摆正”：把 FBX 子节点的变换烘进网格、转到木偶的正面朝向、居中；四肢和拳套用主成分分析（PCA）把最长的轴转成竖直，
    /// 拳套再判断哪头粗——粗的（拳头）朝下。高低两档用同一套变换，保证切换时不跳。
    /// 摆进骨架时挂一个 LOD 组：离眼睛近用高精度，远了换低精度（选人时 6 只同屏也不掉帧）。
    /// 某个部件还没做好就返回 null，调用方退回几何体。</summary>
    public static class SBParts
    {
        public class P { public Mesh hi, lo; public Texture2D tex, nrm; }
        static readonly Dictionary<string, P> cache = new();
        public static float FrontYaw = 90f;
        public static float GloveYaw = -90f;   // 模型手背朝 +x（四面拍过确认），转到 +z   // 新拳套绕竖轴转多少，手背才朝前（看渲染定的）   // P2 导出的模型正面朝 ±x，木偶的前方是 +z

        public static bool Has(string name) => Get(name) != null;
        /// <summary>调试：这次运行里到底请求过哪些部件（没有文件的标“(无)”），用来核对哪些部件目录还在用。</summary>
        public static string DebugLoaded() { var l = new List<string>(); foreach (var kv in cache) l.Add(kv.Key + (kv.Value != null ? "" : "(无)")); l.Sort(); return string.Join(" ", l); }

        static Mesh Raw(string path, out Matrix4x4 m)
        {
            m = Matrix4x4.identity;
            var go = Resources.Load<GameObject>(path);
            var mf = go ? go.GetComponentInChildren<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return null;
            m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            return mf.sharedMesh;
        }

        public static P Get(string name)
        {
            if (cache.TryGetValue(name, out var c)) return c;
            P p = null;
            var lo = Raw("Parts/" + name + "/mesh", out var mLo);
            var hi = Raw("Parts/" + name + "/mesh0", out var mHi);
            var tex = Resources.Load<Texture2D>("Parts/" + name + "/albedo");
            if (lo && tex)
            {
                // 用低精度那份算“摆正”的变换，再同样作用到两份上
                var fy = Quaternion.Euler(0, FrontYaw, 0);
                var v = lo.vertices; for (int i = 0; i < v.Length; i++) v[i] = fy * mLo.MultiplyPoint3x4(v[i]);
                var c0 = Vector3.zero; var q = Quaternion.identity;
                if (name == "glove")
                {
                    // 拳套（实拍三视图确认）：模型里护腕朝 +x、拳头朝 -x、掌心朝 +z、手背朝 -z；
                    // 要的是护腕朝上接前臂、拳头朝下、手背朝前（出拳时手背朝上、掌心朝下）= 绕 (1,1,0) 转 180°
                    c0 = Mean(v); q = Quaternion.AngleAxis(180f, new Vector3(1, 1, 0));
                }
                else if (name == "glove2")
                {
                    // 新拳套：最长轴竖直；粗的一头（拳头）朝下、细的一头（护腕）朝上；再绕竖轴转 GloveYaw 让手背朝前
                    c0 = Mean(v); var e = Principal(v, c0);
                    float up = 0, dn = 0; int nu = 0, nd = 0;
                    foreach (var p0 in v) { var d = p0 - c0; float t = Vector3.Dot(d, e); float rr = (d - e * t).magnitude; if (t > 0) { up += rr; nu++; } else { dn += rr; nd++; } }
                    if (nu > 0 && nd > 0 && up / nu > dn / nd) e = -e;   // 粗的一头朝下
                    q = Quaternion.AngleAxis(GloveYaw, Vector3.up) * Quaternion.FromToRotation(e, Vector3.up);
                }
                else if (name.EndsWith("_limb"))
                {
                    c0 = Mean(v); q = Quaternion.FromToRotation(Principal(v, c0), Vector3.up);
                }
                if (name.EndsWith("_foot"))
                {
                    // 脚：反复做三件事直到稳定——
                    //   ① 朝向：只看脚底那一层（最低 30% 高度）的脚印，脚印最长的方向 = 前后（不受脚踝那截往上伸的影响）；
                    //      脚踝（高处的顶点）在哪头，那头是脚跟，脚尖朝 +z；
                    //   ② 前后摆平：前段和后段的最低点一样高（不踮脚）；③ 左右摆平：内外两侧最低点一样高（不歪）
                    c0 = Mean(v); q = Quaternion.identity;
                    for (int it = 0; it < 8; it++)
                    {
                        var w = new Vector3[v.Length]; float ymin = float.MaxValue, ymax = float.MinValue;
                        for (int i = 0; i < v.Length; i++) { w[i] = q * (v[i] - c0); ymin = Mathf.Min(ymin, w[i].y); ymax = Mathf.Max(ymax, w[i].y); }
                        float ySole = Mathf.Lerp(ymin, ymax, 0.3f), yHigh = Mathf.Lerp(ymin, ymax, 0.65f);
                        var sc = Vector3.zero; int sn = 0; foreach (var p0 in w) if (p0.y < ySole) { sc += p0; sn++; }
                        if (sn < 8) break; sc /= sn;
                        float xx = 0, xz = 0, zz = 0; foreach (var p0 in w) if (p0.y < ySole) { var d = p0 - sc; xx += d.x * d.x; xz += d.x * d.z; zz += d.z * d.z; }
                        float ang = 0.5f * Mathf.Atan2(2 * xz, xx - zz);
                        var yawQ = Quaternion.FromToRotation(new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), Vector3.forward);
                        float hz = 0; int hn = 0; foreach (var p0 in w) if (p0.y > yHigh) { hz += (yawQ * (p0 - sc)).z; hn++; }
                        if (hn > 0 && hz / hn > 0) yawQ = Quaternion.Euler(0, 180, 0) * yawQ;
                        q = yawQ * q;
                        for (int i = 0; i < v.Length; i++) w[i] = q * (v[i] - c0);
                        float zmin = float.MaxValue, zmax = float.MinValue, xmin = float.MaxValue, xmax = float.MinValue;
                        foreach (var p0 in w) if (p0.y < ySole + (ymax - ymin)) { zmin = Mathf.Min(zmin, p0.z); zmax = Mathf.Max(zmax, p0.z); xmin = Mathf.Min(xmin, p0.x); xmax = Mathf.Max(xmax, p0.x); }
                        float zl = Mathf.Lerp(zmin, zmax, 0.3f), zh = Mathf.Lerp(zmin, zmax, 0.7f), xl = Mathf.Lerp(xmin, xmax, 0.3f), xh = Mathf.Lerp(xmin, xmax, 0.7f);
                        float yF = float.MaxValue, zF = 0, yB = float.MaxValue, zB = 0, yL = float.MaxValue, xL = 0, yR = float.MaxValue, xR = 0;
                        foreach (var p0 in w)
                        {
                            if (p0.z > zh && p0.y < yF) { yF = p0.y; zF = p0.z; }
                            if (p0.z < zl && p0.y < yB) { yB = p0.y; zB = p0.z; }
                            if (p0.x < xl && p0.y < yL) { yL = p0.y; xL = p0.x; }
                            if (p0.x > xh && p0.y < yR) { yR = p0.y; xR = p0.x; }
                        }
                        float pitch = yF < float.MaxValue && yB < float.MaxValue ? Mathf.Atan2(yF - yB, zF - zB) * Mathf.Rad2Deg : 0f;
                        float roll = yL < float.MaxValue && yR < float.MaxValue ? Mathf.Atan2(yR - yL, xR - xL) * Mathf.Rad2Deg : 0f;
                        q = Quaternion.Euler(pitch, 0, -roll) * q;
                        if (Mathf.Abs(pitch) < 0.3f && Mathf.Abs(roll) < 0.3f && it > 0) break;
                    }
                }
                else if (name == "lantern")
                {
                    // 灯笼：模型是斜的。吊环和流苏都沿竖轴往外伸 → 最“长”的主轴 = 竖直轴；流苏那头伸得更长 → 朝下
                    c0 = Mean(v);
                    var e = Principal(v, c0);
                    float up = 0, dn = 0; foreach (var p0 in v) { float t = Vector3.Dot(p0 - c0, e); up = Mathf.Max(up, t); dn = Mathf.Max(dn, -t); }
                    if (up > dn) e = -e;   // 伸得长的一头（流苏）朝下
                    q = Quaternion.FromToRotation(e, Vector3.up);
                }
                else if (name == "ctrl_bar")
                {
                    // 十字木架：最薄的方向转成竖直（平放）
                    var bb = new Bounds(v[0], Vector3.zero); foreach (var p0 in v) bb.Encapsulate(p0);
                    var e = bb.size;
                    if (e.x < e.y && e.x < e.z) q = Quaternion.Euler(0, 0, 90); else if (e.z < e.y && e.z < e.x) q = Quaternion.Euler(90, 0, 0);
                }
                var b = new Bounds(q * (v[0] - c0), Vector3.zero); foreach (var p0 in v) b.Encapsulate(q * (p0 - c0));
                var shift = b.center;
                Mesh Bake(Mesh src, Matrix4x4 m0)
                {
                    var mm = Object.Instantiate(src);
                    var vv = mm.vertices; var nn = mm.normals; bool hasN = nn.Length == vv.Length;
                    for (int i = 0; i < vv.Length; i++)
                    {
                        vv[i] = q * (fy * m0.MultiplyPoint3x4(vv[i]) - c0) - shift;
                        if (hasN) nn[i] = (q * (fy * m0.MultiplyVector(nn[i]))).normalized;
                    }
                    mm.vertices = vv; if (hasN) mm.normals = nn; else mm.RecalculateNormals();
                    mm.RecalculateTangents(); mm.RecalculateBounds();
                    return mm;
                }
                p = new P { lo = Bake(lo, mLo), hi = hi ? Bake(hi, mHi) : null, tex = tex, nrm = Resources.Load<Texture2D>("Parts/" + name + "/normal") };
                p.lo.name = name + "_lo"; if (p.hi) p.hi.name = name + "_hi";
            }
            cache[name] = p;
            return p;
        }

        static readonly Dictionary<(string, int), Vector2> widthCache = new();
        /// <summary>量模型在某个相对高度（0 = 底，1 = 顶）附近一圈的实际半宽 x、半厚 z（模型原始单位，未缩放）。拼装时用它把肩、髋、脖子放在模型表面上。</summary>
        public static Vector2 HalfWidthAt(string name, float yNorm, float band = 0.06f)
        {
            var key = (name, Mathf.RoundToInt(yNorm * 100));
            if (widthCache.TryGetValue(key, out var w)) return w;
            var p = Get(name); if (p == null) return Vector2.zero;
            var b = p.lo.bounds; float y0 = b.min.y + b.size.y * (yNorm - band), y1 = b.min.y + b.size.y * (yNorm + band);
            float mx = 0, mz = 0;
            foreach (var v in p.lo.vertices) if (v.y >= y0 && v.y <= y1) { mx = Mathf.Max(mx, Mathf.Abs(v.x - b.center.x)); mz = Mathf.Max(mz, Mathf.Abs(v.z - b.center.z)); }
            w = new Vector2(mx, mz); widthCache[key] = w; return w;
        }

        static readonly Dictionary<string, Vector3> topPtCache = new();
        /// <summary>模型最上面 8% 高度那一圈顶点的中心（摆正后的模型坐标）：脚踝口 / 鞋口在哪，脚就往哪对齐踝球。</summary>
        public static Vector3 TopPoint(string name)
        {
            if (topPtCache.TryGetValue(name, out var t)) return t;
            var p = Get(name); if (p == null) return Vector3.zero;
            var b = p.lo.bounds; float y0 = b.max.y - b.size.y * 0.08f; var sum = Vector3.zero; int n = 0;
            foreach (var v in p.lo.vertices) if (v.y >= y0) { sum += v; n++; }
            t = n > 0 ? sum / n : b.center; topPtCache[name] = t; return t;
        }

        // 封口鞋的接触面中心（模型坐标 x, z）：这两只鞋的鞋口是一圈压条 / 贴图画出来的圆环，里面被填平，俯视高度图量不出“开口”，
        // 所以按俯视渲染图量出圆环正中心写在这里（用 /tmp/sb_footzoom.sh 那种正上方放大图量；部件重新生成后要重量）。高度仍取该点自己的表面。
        static readonly Dictionary<string, Vector2> seatFix = new()
        {
            { "iron_foot", new Vector2(0.021f, -0.238f) },      // 圆皮垫正中
            { "tumbler_foot", new Vector2(0f, -0.195f) },       // 金边鞋口正中
        };
        /// <summary>这个脚模型生成出来是左脚形状（内侧直、外侧鼓在 -x）：拼装时原样当左脚，镜像一份当右脚。其余脚模型都是右脚形状。</summary>
        public static bool IsLeftFootModel(string name) => name == "iron_foot";

        static readonly Dictionary<string, (Vector3 p, bool shoe)> seatCache = new();
        /// <summary>脚 / 鞋和踝球的接触面中心（模型坐标）：
        ///   鞋 = 鞋口：俯视高度图里四周都被更高的鞋帮围住的“盆地”，取盆地中心，高度取鞋口边沿；
        ///   光脚 = 脚踝截面：最高那一圈（顶上 12% 高度）的中心，高度取截面顶。</summary>
        public static (Vector3 p, bool shoe) AnkleSeat(string name)
        {
            if (seatCache.TryGetValue(name, out var r)) return r;
            var pp = Get(name); if (pp == null) return (Vector3.zero, false);
            var b = pp.lo.bounds; const int N = 48;
            var H = new float[N, N]; for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) H[i, j] = float.NaN;
            int Ix(float x) => Mathf.Clamp((int)((x - b.min.x) / Mathf.Max(b.size.x, 1e-6f) * N), 0, N - 1);
            int Iz(float z) => Mathf.Clamp((int)((z - b.min.z) / Mathf.Max(b.size.z, 1e-6f) * N), 0, N - 1);
            // 在三角面上均匀取样填格子（光栅化）：大三角面中间也有高度，鞋口内部不会是空格
            var vs = pp.lo.vertices; var ts = pp.lo.triangles; float cell = Mathf.Min(b.size.x, b.size.z) / N;
            void Put(Vector3 v) { int i = Ix(v.x), j = Iz(v.z); if (float.IsNaN(H[i, j]) || v.y > H[i, j]) H[i, j] = v.y; }
            for (int t = 0; t < ts.Length; t += 3)
            {
                Vector3 A = vs[ts[t]], B = vs[ts[t + 1]], C = vs[ts[t + 2]];
                float L = Mathf.Max(new Vector2(B.x - A.x, B.z - A.z).magnitude, Mathf.Max(new Vector2(C.x - A.x, C.z - A.z).magnitude, new Vector2(C.x - B.x, C.z - B.z).magnitude));
                int sub = Mathf.Clamp(Mathf.CeilToInt(L / (cell * 0.5f)), 1, 64);
                for (int u = 0; u <= sub; u++) for (int w = 0; w <= sub - u; w++) { float fu = u / (float)sub, fw = w / (float)sub; Put(A + (B - A) * fu + (C - A) * fw); }
            }
            float X(int i) => b.min.x + (i + 0.5f) * b.size.x / N;
            float Z(int j) => b.min.z + (j + 0.5f) * b.size.z / N;
            // 盆地：往 ±x、±z 四个方向看，都有比自己高出一截的鞋帮
            float margin = b.size.y * 0.12f; var sum = Vector3.zero; float rimSum = 0; int n = 0, filled = 0;
            for (int i = 0; i < N; i++) for (int j = 0; j < N; j++)
            {
                if (float.IsNaN(H[i, j])) continue; filled++;
                float m0 = float.MinValue, m1 = float.MinValue, m2 = float.MinValue, m3 = float.MinValue;
                for (int k = i + 1; k < N; k++) if (!float.IsNaN(H[k, j])) m0 = Mathf.Max(m0, H[k, j]);
                for (int k = i - 1; k >= 0; k--) if (!float.IsNaN(H[k, j])) m1 = Mathf.Max(m1, H[k, j]);
                for (int k = j + 1; k < N; k++) if (!float.IsNaN(H[i, k])) m2 = Mathf.Max(m2, H[i, k]);
                for (int k = j - 1; k >= 0; k--) if (!float.IsNaN(H[i, k])) m3 = Mathf.Max(m3, H[i, k]);
                float wall = Mathf.Min(Mathf.Min(m0, m1), Mathf.Min(m2, m3));
                // 只算四周鞋帮都够高（整只高度的 35% 以上）的开口：光脚趾缝 / 脚背凹处不算
                if (wall - H[i, j] > margin && wall > b.min.y + b.size.y * 0.35f) { sum += new Vector3(X(i), 0, Z(j)); rimSum += wall; n++; }
            }
            if (n > filled * 0.03f && n >= 4) r = (new Vector3(sum.x / n, rimSum / n, sum.z / n), true);
            else
            {   // 光脚 / 封口的鞋：上部那一圈（高过 65% 的部分）的轮廓框中心 —— 光脚就是脚踝截面，封口鞋就是鞋帮围着的那圈
                float top = b.min.y; foreach (var h in H) if (!float.IsNaN(h)) top = Mathf.Max(top, h);
                float th = b.min.y + b.size.y * 0.65f; float x0 = 9, x1 = -9, z0 = 9, z1 = -9; bool any = false;
                for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) if (!float.IsNaN(H[i, j]) && H[i, j] > th) { any = true; x0 = Mathf.Min(x0, X(i)); x1 = Mathf.Max(x1, X(i)); z0 = Mathf.Min(z0, Z(j)); z1 = Mathf.Max(z1, Z(j)); }
                float cx = any ? (x0 + x1) * 0.5f : b.center.x, cz = any ? (z0 + z1) * 0.5f : b.center.z;
                if (seatFix.TryGetValue(name, out var fx)) { cx = fx.x; cz = fx.y; }   // 封口鞋：用量好的圆环中心
                // 高度取中心那一小块自己的表面（光脚 = 脚踝截面顶；鞋口被内底填平的鞋 = 鞋口内底），不取整只鞋的最高点
                float hc = float.MinValue; int ci = Ix(cx), cj = Iz(cz);
                for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++) { int ii = Mathf.Clamp(ci + di, 0, N - 1), jj = Mathf.Clamp(cj + dj, 0, N - 1); if (!float.IsNaN(H[ii, jj])) hc = Mathf.Max(hc, H[ii, jj]); }
                r = (new Vector3(cx, hc > float.MinValue ? hc : top, cz), false);
            }
            seatCache[name] = r; return r;
        }

        /// <summary>某个相对高度附近一圈顶点里最靠 +z（手背 / 正面）的表面：返回 (x, z) 模型坐标。</summary>
        public static Vector2 FrontAt(string name, float yNorm, float band = 0.05f)
        {
            var p = Get(name); if (p == null) return Vector2.zero;
            var b = p.lo.bounds; float y0 = b.min.y + b.size.y * (yNorm - band), y1 = b.min.y + b.size.y * (yNorm + band);
            float bz = float.MinValue, bx = 0;
            foreach (var v in p.lo.vertices) if (v.y >= y0 && v.y <= y1 && v.z > bz) { bz = v.z; bx = v.x; }
            return new Vector2(bx, bz == float.MinValue ? 0 : bz);
        }

        static readonly Dictionary<string, float> cuffCache = new();
        /// <summary>拳套护腕那一圈（高度 90%~97%）的外半径，取窄的那个方向（模型单位）：前臂的腕榫要比它细才塞得进去。</summary>
        public static float CuffRadius(string name)
        {
            if (cuffCache.TryGetValue(name, out var c)) return c;
            var p = Get(name); if (p == null) return 0f;
            var b = p.lo.bounds; float ya = b.min.y + b.size.y * 0.90f, yb = b.min.y + b.size.y * 0.97f; float x0 = 9, x1 = -9, z0 = 9, z1 = -9;
            foreach (var v in p.lo.vertices) if (v.y >= ya && v.y <= yb) { x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); z0 = Mathf.Min(z0, v.z); z1 = Mathf.Max(z1, v.z); }
            c = x1 > x0 ? Mathf.Min(x1 - x0, z1 - z0) * 0.5f : 0f; cuffCache[name] = c; return c;
        }

        static readonly Dictionary<string, float> holeCache = new();
        /// <summary>拳套护腕洞口的内半径（模型单位）：最顶上那一圈口沿里，离洞口中心最近的顶点有多远。前臂的腕榫要比它细，才不会切进口沿。</summary>
        public static float CuffInner(string name)
        {
            if (holeCache.TryGetValue(name, out var c)) return c;
            var p = Get(name); if (p == null) return 0f;
            var b = p.lo.bounds; var seat = AnkleSeat(name).p; float ya = b.min.y + b.size.y * 0.955f; c = float.MaxValue;
            foreach (var v in p.lo.vertices) if (v.y >= ya) c = Mathf.Min(c, new Vector2(v.x - seat.x, v.z - seat.z).magnitude);
            if (c == float.MaxValue) c = CuffRadius(name) * 0.55f;
            holeCache[name] = c; return c;
        }

        static readonly Dictionary<string, float> midTopCache = new();
        /// <summary>模型正中间（水平离中心 12% 宽度以内）那一列最高点的相对高度：鞋口 / 脚背正中，踝球就坐在这里。</summary>
        public static float MidTop(string name)
        {
            if (midTopCache.TryGetValue(name, out var t)) return t;
            var p = Get(name); if (p == null) return 1f;
            var b = p.lo.bounds; float lim = Mathf.Min(b.extents.x, b.extents.z) * 0.24f, top = b.min.y;
            foreach (var v in p.lo.vertices) if (new Vector2(v.x - b.center.x, v.z - b.center.z).magnitude < lim) top = Mathf.Max(top, v.y);
            t = (top - b.min.y) / Mathf.Max(b.size.y, 1e-5f); midTopCache[name] = t; return t;
        }

        static readonly Dictionary<string, float> topCache = new();
        /// <summary>模型顶端在中轴附近（水平离中心 25% 宽度以内）的最高点，返回相对高度 0..1。系线铜扣按它贴在头顶 / 身顶上。</summary>
        public static float TopAt(string name)
        {
            if (topCache.TryGetValue(name, out var t)) return t;
            var p = Get(name); if (p == null) return 1f;
            var b = p.lo.bounds; float lim = Mathf.Max(b.extents.x, b.extents.z) * 0.25f, top = b.min.y;
            foreach (var v in p.lo.vertices) if (new Vector2(v.x - b.center.x, v.z - b.center.z).magnitude < lim) top = Mathf.Max(top, v.y);
            t = (top - b.min.y) / Mathf.Max(b.size.y, 1e-5f); topCache[name] = t; return t;
        }

        /// <summary>按高度等比缩放放进去：模型底边放在 bottom（父节点本地 y），高度 = height。返回实际缩放倍数（0 = 没有这个部件）。</summary>
        public static float MakeByHeight(string name, Transform parent, float bottom, float height, Material lit, Color tint, float metal, float smooth, out Transform t, float yaw = 0f)
        {
            t = null; var p = Get(name); if (p == null) return 0f;
            float sc = height / Mathf.Max(p.lo.bounds.size.y, 1e-5f);
            var sz = p.lo.bounds.size * sc;
            t = Make(name, parent, new Vector3(0, bottom + height * 0.5f, 0), sz, lit, tint, true, yaw, metal, smooth);
            return sc;
        }

        static Vector3 Mean(Vector3[] v) { var s = Vector3.zero; foreach (var p in v) s += p; return s / Mathf.Max(1, v.Length); }

        /// <summary>主成分：顶点分布最“长”的方向（协方差矩阵的最大特征向量，幂迭代求）。</summary>
        static Vector3 Principal(Vector3[] v, Vector3 c)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p0 in v) { var d = p0 - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            var e = new Vector3(0.3f, 1f, 0.2f).normalized;
            for (int k = 0; k < 40; k++)
            {
                var ne = new Vector3(xx * e.x + xy * e.y + xz * e.z, xy * e.x + yy * e.y + yz * e.z, xz * e.x + yz * e.y + zz * e.z);
                if (ne.sqrMagnitude < 1e-12f) break; e = ne.normalized;
            }
            if (e.y < 0) e = -e;
            return e;
        }

        /// <summary>造一个部件塞进骨架：center = 本地位置，size = 这个位置的包围盒；uniform = 等比缩放（塞进盒子里），否则三轴各自拉伸到盒子。</summary>
        public static Transform Make(string name, Transform parent, Vector3 center, Vector3 size, Material lit, Color tint, bool uniform, float yaw = 0f, float metal = 0f, float smooth = 0.25f, bool mirror = false)
        {
            var p = Get(name); if (p == null) return null;
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.localPosition = center; g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var m = new Material(lit) { color = tint }; m.mainTexture = p.tex; m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (p.nrm) { m.SetTexture("_BumpMap", p.nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            var b = p.lo.bounds.size; b = new Vector3(Mathf.Max(b.x, 1e-5f), Mathf.Max(b.y, 1e-5f), Mathf.Max(b.z, 1e-5f));
            g.transform.localScale = uniform ? Vector3.one * Mathf.Min(size.x / b.x, Mathf.Min(size.y / b.y, size.z / b.z))
                                             : new Vector3(size.x / b.x, size.y / b.y, size.z / b.z);
            if (mirror) g.transform.localScale = Vector3.Scale(g.transform.localScale, new Vector3(-1, 1, 1));   // 左右镜像（左手 / 左肩）
            Renderer R(Mesh mesh, string n)
            {
                var c = new GameObject(n); c.transform.SetParent(g.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return r;
            }
            if (p.hi)
            {
                var rh = R(p.hi, "高"); var rl = R(p.lo, "低");
                var lg = g.AddComponent<LODGroup>();
                // 部件在视野里占的高度比例：近（≥ 2.2%）用高精度，再远用低精度
                lg.SetLODs(new[] { new LOD(0.022f, new[] { rh }), new LOD(0.0005f, new[] { rl }) });
                lg.RecalculateBounds();
            }
            else R(p.lo, "低");
            return g.transform;
        }
    }
}
