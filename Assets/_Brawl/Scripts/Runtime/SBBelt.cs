using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>冠军金腰带。用两根提线从上面吊下来（戏台上所有东西都是吊着的），悬在冠军举起的拳套前面——不和木偶接触，所以不会穿模。
    /// 腰带本身是一整条做好的弧形皮带（不是一段段方块拼的）：
    ///   · 挂在两个吊点上，中间自然下垂成一道弧，两头从吊点外侧垂下去；
    ///   · 皮带中间宽、两头收窄，有厚度，上下各一道金色滚边、里侧各一道缝线，两端是金属包头和按扣；
    ///   · 正中是大金牌（Tripo P2 生成的立体浮雕：红珐琅底上一只戴皇冠的金拳套），两侧各一块小金牌（云纹拳套）。
    /// 金属度不开太高：MR 里没有环境反射，纯金属会发黑。</summary>
    public static class SBBelt
    {
        /// <summary>搭一条腰带。a = 两个吊点之间距离的一半；stringLen = 提线多长。
        /// 返回的根在“两根提线顶端的中点”：整条腰带绕这个点轻轻摆（钟摆），皮带的吊点在它正下方 stringLen 处；牌面朝 -z。</summary>
        public static Transform Build(Transform parent, Material lit, float a, float stringLen)
        {
            var root = new GameObject("金腰带").transform; root.SetParent(parent, false);
            var body = new GameObject("带身").transform; body.SetParent(root, false); body.localPosition = new Vector3(0, -stringLen, 0);

            Material Mat(Color c, float metal, float smooth)
            {
                var m = new Material(lit) { color = c }; m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); return m;
            }
            var gold = Mat(new Color(1.2f, .86f, .32f), 0.45f, 0.68f);
            var goldDeep = Mat(new Color(.95f, .62f, .2f), 0.45f, 0.5f);
            var leather = Mat(new Color(.6f, .065f, .055f), 0f, 0.36f);
            var thread = Mat(new Color(.95f, .72f, .36f), 0.1f, 0.3f);
            var rope = Mat(new Color(.92f, .87f, .74f), 0f, 0.2f);

            // —— 中线：吊点之间是下垂的抛物线；吊点外侧是垂下去的两头 ——
            const float th = 0.0036f; float sag = a * 0.3f, tail = Mathf.Max(0.04f, a * 0.55f);
            var P = new List<Vector2>(); var W = new List<float>();
            const int nTail = 9, nMid = 30;
            Vector2 Tail(float t, float sx) => new Vector2(sx * (a + tail * (0.5f * t - 0.14f * t * t)), -tail * (0.55f * t + 0.6f * t * t));   // t：0 = 吊点，1 = 带尾
            for (int i = nTail; i >= 1; i--) { float t = i / (float)nTail; P.Add(Tail(t, -1)); W.Add(Mathf.Lerp(0.021f, 0.015f, t)); }
            for (int i = 0; i <= nMid; i++) { float u = i / (float)nMid * 2f - 1f; P.Add(new Vector2(u * a, -sag * (1f - u * u))); W.Add(Mathf.Lerp(0.027f, 0.021f, Mathf.Abs(u))); }
            for (int i = 1; i <= nTail; i++) { float t = i / (float)nTail; P.Add(Tail(t, 1)); W.Add(Mathf.Lerp(0.021f, 0.015f, t)); }
            int n = P.Count;
            var Tn = new Vector2[n]; var Nn = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var d = (P[Mathf.Min(i + 1, n - 1)] - P[Mathf.Max(i - 1, 0)]).normalized; Tn[i] = d; Nn[i] = new Vector2(-d.y, d.x);
            }

            // —— 皮带：一条有厚度的带子（正面、背面、上沿、下沿）——
            {
                var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); float len = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) len += Vector2.Distance(P[i], P[i - 1]);
                    Vector2 top = P[i] + Nn[i] * W[i] * 0.5f, bot = P[i] - Nn[i] * W[i] * 0.5f; float u = len / 0.03f;
                    // 每个截面 8 个顶点：正面上下、背面上下、上沿前后、下沿前后（各面法线不同）
                    vs.Add(new Vector3(top.x, top.y, -th / 2)); ns.Add(Vector3.back); uv.Add(new Vector2(u, 1));
                    vs.Add(new Vector3(bot.x, bot.y, -th / 2)); ns.Add(Vector3.back); uv.Add(new Vector2(u, 0));
                    vs.Add(new Vector3(top.x, top.y, th / 2)); ns.Add(Vector3.forward); uv.Add(new Vector2(u, 1));
                    vs.Add(new Vector3(bot.x, bot.y, th / 2)); ns.Add(Vector3.forward); uv.Add(new Vector2(u, 0));
                    var nn = new Vector3(Nn[i].x, Nn[i].y, 0);
                    vs.Add(new Vector3(top.x, top.y, -th / 2)); ns.Add(nn); uv.Add(new Vector2(u, 1));
                    vs.Add(new Vector3(top.x, top.y, th / 2)); ns.Add(nn); uv.Add(new Vector2(u, 0.9f));
                    vs.Add(new Vector3(bot.x, bot.y, -th / 2)); ns.Add(-nn); uv.Add(new Vector2(u, 0));
                    vs.Add(new Vector3(bot.x, bot.y, th / 2)); ns.Add(-nn); uv.Add(new Vector2(u, 0.1f));
                    if (i == 0) continue;
                    int b0 = (i - 1) * 8, b1 = i * 8;
                    // 每个面正反都画一遍三角形（带子很薄，绕向写反一面就会缺面；两面都画最稳妥）
                    void Q(int p0, int p1, int p2, int p3) { tr.Add(p0); tr.Add(p1); tr.Add(p2); tr.Add(p0); tr.Add(p2); tr.Add(p3); tr.Add(p0); tr.Add(p2); tr.Add(p1); tr.Add(p0); tr.Add(p3); tr.Add(p2); }
                    Q(b0 + 0, b1 + 0, b1 + 1, b0 + 1);     // 正面（朝 -z）
                    Q(b0 + 2, b0 + 3, b1 + 3, b1 + 2);     // 背面
                    Q(b0 + 4, b0 + 5, b1 + 5, b1 + 4);     // 上沿
                    Q(b0 + 6, b1 + 6, b1 + 7, b0 + 7);     // 下沿
                }
                var m = new Mesh(); m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uv); m.SetTriangles(tr, 0); m.RecalculateBounds();
                var g = new GameObject("皮带"); g.transform.SetParent(body, false); g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = leather;
            }
            // —— 金色滚边（上下各一道细圆管）、缝线（滚边里侧各一道更细的线）——
            {
                var pipe = new SBGeo(); var st = new SBGeo();
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var e = new List<Vector3>(); var s2 = new List<Vector3>();
                    for (int i = 0; i < n; i++)
                    {
                        var p = P[i] + Nn[i] * W[i] * 0.5f * sgn; e.Add(new Vector3(p.x, p.y, -th * 0.2f));
                        var q = P[i] + Nn[i] * (W[i] * 0.5f - 0.0036f) * sgn; s2.Add(new Vector3(q.x, q.y, -th / 2 - 0.00015f));
                    }
                    pipe.Tube(e, 0.0013f, 7); st.Tube(s2, 0.00032f, 4);
                }
                pipe.Make(body, "金滚边", gold); st.Make(body, "缝线", thread);
            }
            // —— 两端的金属包头 + 两颗按扣 ——
            {
                var tips = new SBGeo();
                foreach (int end in new[] { 0, n - 1 })
                {
                    float ang = Mathf.Atan2(Tn[end].y, Tn[end].x) * Mathf.Rad2Deg; var rot = Quaternion.Euler(0, 0, ang);
                    var c = new Vector3(P[end].x, P[end].y, 0);
                    tips.Blob(c, new Vector3(0.0052f, W[end] * 0.56f, th * 0.95f), rot, 3.2f, 8, 12);
                    for (int k = 1; k <= 2; k++) { int i = end == 0 ? k * 2 : n - 1 - k * 2; tips.Blob(new Vector3(P[i].x, P[i].y, -th / 2 - 0.0005f), Vector3.one * 0.0017f, Quaternion.identity, 2f, 6, 8); }
                }
                tips.Make(body, "包头按扣", gold);
            }
            // —— 两根提线：从吊点往上；吊点处一个小铜夹 ——
            {
                var rp = new SBGeo(); var clip = new SBGeo();
                foreach (float sx in new[] { -1f, 1f })
                {
                    var hp = new Vector3(sx * a, 0.0135f, 0);
                    rp.Cyl(hp, hp + Vector3.up * (stringLen - 0.0135f), 0.00055f, 6, false);
                    clip.Box(new Vector3(sx * a, 0.006f, 0), new Vector3(0.0065f, 0.017f, th + 0.003f));
                    clip.Blob(new Vector3(sx * a, 0.0145f, 0), Vector3.one * 0.0022f, Quaternion.identity, 2f, 6, 8);
                }
                rp.Make(body, "提线", rope); clip.Make(body, "铜夹", goldDeep);
            }

            // —— 金牌：Tripo P2 生成的立体浮雕（先出设计图 → 白模 → 贴图 → Blender 切掉背面支架）；没有就不放 ——
            Transform Plate(string part, string name, Vector2 at, float angDeg, float dia, float maxThick)
            {
                if (!SBParts.Has(part)) return null;
                var pp = SBParts.Get(part); if (pp == null) return null;
                var b = pp.lo.bounds; var sz = b.size; int ax = sz.x < sz.y ? (sz.x < sz.z ? 0 : 2) : (sz.y < sz.z ? 1 : 2);   // 最薄的方向 = 厚度
                // 哪面是正面：背面是整块平的（最靠后那一薄层铺满整个牌子），正面最突出的只有浮雕的尖；两边差不多时退回“浮雕那面顶点多”
                int o1 = (ax + 1) % 3, o2 = (ax + 2) % 3; float band = sz[ax] * 0.08f, extPos = 0f, extNeg = 0f; int plus = 0, cnt = 0;
                foreach (var v in pp.lo.vertices)
                {
                    cnt++; if (v[ax] > b.center[ax]) plus++;
                    float rr = new Vector2(v[o1] - b.center[o1], v[o2] - b.center[o2]).magnitude;
                    if (v[ax] > b.max[ax] - band) extPos = Mathf.Max(extPos, rr);
                    if (v[ax] < b.min[ax] + band) extNeg = Mathf.Max(extNeg, rr);
                }
                bool pos1 = Mathf.Abs(extPos - extNeg) > 0.15f * Mathf.Max(extPos, extNeg) ? extPos < extNeg : plus * 2 > cnt;
                var rot = ax == 0 ? Quaternion.Euler(0, pos1 ? 90f : -90f, 0) : ax == 2 ? Quaternion.Euler(0, pos1 ? 180f : 0f, 0) : Quaternion.Euler(pos1 ? -90f : 90f, 0, 0);   // 正面转到朝 -z
                float thick = sz[ax] * dia / Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
                var pl = new GameObject(name).transform; pl.SetParent(body, false); pl.localPosition = new Vector3(at.x, at.y, -th / 2 - 0.0014f); pl.localRotation = Quaternion.Euler(0, 0, angDeg);
                // 模型本身很厚（两厘米上下），原样放上去像一块东西悬在皮带前面：沿厚度方向压扁到 maxThick，变成一块贴在皮带上的牌子
                var flat = new GameObject("压扁").transform; flat.SetParent(pl, false); flat.localScale = new Vector3(1f, 1f, Mathf.Min(1f, maxThick / Mathf.Max(thick, 1e-5f)));
                var hold = new GameObject("朝向").transform; hold.SetParent(flat, false); hold.localRotation = rot; hold.localPosition = Vector3.back * (thick * 0.5f);   // 牌子背面在皮带正面前一点点
                SBParts.Make(part, hold, Vector3.zero, Vector3.one * dia, lit, new Color(1.3f, 1.22f, 1.08f), true, 0f, 0.35f, 0.62f);
                // 素金底板：垫在牌子和皮带之间（盖住牌子背面——Blender 切口封的底没有像样的贴图），比牌子略小，正面看不到它
                var bk = new GameObject("底板轴").transform; bk.SetParent(pl, false); bk.localPosition = new Vector3(0, 0, 0.0012f); bk.localRotation = Quaternion.Euler(-90f, 0, 0);
                float bR = dia * 0.5f / Mathf.Cos(Mathf.PI / 8f) * 0.97f;
                var bg = new SBGeo(); bg.Lathe(Vector3.zero, new[] { new Vector2(bR, -0.0011f), new Vector2(bR, 0f) }, 8, Mathf.PI / 8f, true, true); bg.Make(bk, "底板", goldDeep);
                return pl;
            }
            int mid = nTail + nMid / 2;
            const float coreDia = 0.058f;
            Plate("belt_core", "大金牌", P[mid], 0f, coreDia, 0.009f);
            foreach (float sx in new[] { -1f, 1f })
            {
                // 侧牌必须整块落在皮带上：紧挨着大金牌（皮带在那儿还宽、还平），大小不超过那一处皮带的宽度。
                // 原来放在离中间 62% 的地方，那里皮带已经往上拐、也变窄了，牌子有一块悬在皮带外面
                float corner = coreDia * 0.5f / Mathf.Cos(Mathf.PI / 8f), dia = 0.024f; int i = mid;
                for (int pass = 0; pass < 3; pass++)
                {
                    // 离大金牌留出约 2.4 厘米的皮带（用户两次要求往外挪）；最多放到吊点以内 68% 处（再往外皮带就拐上去了），也不能挤到大金牌上
                    float near = corner + dia * 0.54f + 0.002f, wantX = Mathf.Max(near, Mathf.Min(corner + dia * 0.54f + 0.024f, a * 0.68f)); i = mid; float best = 9f;
                    for (int k = nTail; k <= nTail + nMid; k++) { float d = Mathf.Abs(P[k].x * sx - wantX); if (d < best) { best = d; i = k; } }
                    dia = Mathf.Min(dia, W[i] * 0.86f);
                }
                Plate("belt_side", sx < 0 ? "侧牌左" : "侧牌右", P[i], Mathf.Atan2(Tn[i].y, Tn[i].x) * Mathf.Rad2Deg, dia, 0.006f);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }
    }
}
