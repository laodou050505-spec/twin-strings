using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>木偶的“人偶结构”：不穿裤子 / 长衣，身子分成 胸 + 腰球 + 胯块，四肢一节一节用球关节串起来（像木制美术人偶）。
    /// 这里是每个角色的外观风格 + 程序建模的零件（四肢、胯块、程序躯干）。
    /// 四肢 = 车削体：中段饱满，两头收细成圆头，收细的那头插进关节球里，球在接缝处露出一圈 → 一眼看出是一节一节串起来的。
    /// 关节球：肩 / 髋（连躯干的）大一号，肘 / 膝 / 踝小一号，都比四肢细，只是“点到”。</summary>
    public static class SBDoll
    {
        public class Style
        {
            public string tile;                 // Resources/Tiles 下的无缝贴图（没有就纯色）
            public Color tint = Color.white;    // 四肢颜色（乘到贴图上）
            public float metal, smooth = 0.35f;
            public Color band = new(.42f, .3f, .18f); public float bandMetal, bandSmooth = 0.3f;
            public float[] bands = new float[0];  // 四肢上的箍（0..1 沿长度）
            public float bandW = 0.10f;           // 箍宽（占四肢长度）
            public bool nodes;                     // 竹节
            public bool bracer;                    // 铁护腕 / 护胫（中段一片深色铁板 + 铜边 + 铆钉）
            public Color joint = Color.white; public float jointMetal, jointSmooth = 0.5f;   // 关节球颜色、质感（每个木偶不一样）
            public string chest;                   // 生成的胸部件名（null = 程序躯干）
            public string body;                    // 整体身子（秤砣 / 不倒翁：胸和胯是一整块）
            public string procChest;               // 程序躯干：bamboo / post
            public Color pelvis = new(.5f, .4f, .3f); public string pelvisTile; public float pelvisMetal, pelvisSmooth = 0.3f;
            public Color sash = Color.clear;       // 胯块上沿的一圈腰带 / 绑绳颜色
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static readonly Dictionary<string, Style> Styles = new()
        {
            ["weight"] = new Style { tile = "tile_bronze", tint = C("#c8b89a"), metal = 0.35f, smooth = 0.45f, band = C("#c9973f"), bandMetal = 0.6f, bandSmooth = 0.55f,
                                     bands = new[] { 0.26f, 0.74f }, bandW = 0.06f, joint = C("#d4a24a"), jointMetal = 0.6f, jointSmooth = 0.6f, body = "weight_core" },   // 黄铜球
            ["scarecrow"] = new Style { tile = "tile_straw2", tint = C("#f0dca6"), smooth = 0.12f, band = C("#6b4a2a"), bands = new[] { 0.26f, 0.74f }, bandW = 0.05f,
                                     joint = C("#7a5232"), jointSmooth = 0.25f, chest = "scarecrow_chest", pelvis = C("#d6b56e"), pelvisTile = "tile_straw2", sash = C("#7a5a34") },
            ["kite"] = new Style { tile = "tile_paper", tint = C("#fbf6ea"), smooth = 0.2f, band = C("#c63a32"), bands = new[] { 0.5f }, bandW = 0.05f,
                                     joint = C("#c63a32"), jointSmooth = 0.7f, chest = "kite_chest", body = "kite_chest", pelvis = C("#f4eee0"), pelvisTile = "tile_paper", sash = C("#c63a32") },
            ["bamboo"] = new Style { tile = "tile_bamboo", tint = C("#d6e6a8"), smooth = 0.42f, band = C("#e2d48a"), nodes = true,
                                     joint = C("#d9c27a"), jointSmooth = 0.5f, procChest = "bamboo", pelvis = C("#c5d98e"), pelvisTile = "tile_bamboo", sash = C("#c9a96a") },
            ["iron"] = new Style { tile = "tile_wood", tint = C("#e8d2b0"), smooth = 0.35f, band = C("#2f3036"), bandMetal = 0.45f, bandSmooth = 0.5f, bracer = true,
                                     joint = C("#2c2d33"), jointMetal = 0.5f, jointSmooth = 0.55f, chest = "iron_chest", pelvis = C("#3a3b42"), pelvisMetal = 0.4f, pelvisSmooth = 0.5f, sash = C("#8a2a22") },
            ["tumbler"] = new Style { tile = "tile_wood", tint = C("#f2dcc0"), smooth = 0.45f, band = C("#c23b2f"), bands = new[] { 0.72f }, bandW = 0.08f,
                                     joint = C("#f3ece0"), jointSmooth = 0.75f, body = "tumbler_body" },
            ["dummy"] = new Style { tile = "tile_wood", tint = C("#d9b48a"), smooth = 0.3f, band = C("#7a5a34"), bands = new[] { 0.26f, 0.74f }, bandW = 0.05f,
                                     joint = C("#7d8087"), jointMetal = 0.45f, jointSmooth = 0.5f, procChest = "post", pelvis = C("#c79a68"), pelvisTile = "tile_wood", sash = C("#5b3a22") },
        };
        public static Style Get(string id) => Styles.TryGetValue(id, out var s) ? s : Styles["dummy"];

        static readonly Dictionary<string, Texture2D> tiles = new();
        public static Texture2D Tile(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            if (!tiles.TryGetValue(n, out var t)) { t = Resources.Load<Texture2D>("Tiles/" + n); tiles[n] = t; }
            return t;
        }
        public static Material Mat(Material lit, Color c, string tile, float metal, float smooth, Vector2 scale)
        {
            var m = new Material(lit) { color = c };
            var t = Tile(tile); if (t) { m.mainTexture = t; m.mainTextureScale = scale; }
            m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            return m;
        }

        /// <summary>一节四肢的截面线（半径, 高度），从 0 到 len：两头圆头收细（端部半径 = 0.6r），中段饱满微鼓。</summary>
        /// <summary>四肢的侧面轮廓线（x = 半径，y = 从下端往上的高度）。
        /// 普通：两头收成圆头、中段略鼓。带腕榫（plugR &gt; 0，只给前臂用）：下端（手腕那头）在 plugTop 以下一直是细的 plugR，
        /// 能整截插进拳套护腕里；往上再顺滑地变粗到肘那头 —— 前臂像真手臂一样“肘粗腕细”。</summary>
        static List<Vector2> LimbProfile(float len, float r, float plugR = 0f, float plugTop = 0f)
        {
            var p = new List<Vector2>();
            float re = r * 0.6f, hd = Mathf.Min(r * 0.55f, len * 0.2f), ramp = Mathf.Min(len * 0.16f, r * 1.2f);
            float y0 = hd + ramp, y1 = len - hd - ramp;
            if (plugR > 0f && plugR < r)
            {
                float rp = plugR, hd2 = Mathf.Min(rp * 0.5f, len * 0.08f);
                plugTop = Mathf.Clamp(plugTop, hd2, len - hd - r * 0.3f);
                float taper = Mathf.Max(r * 1.4f, (y1 - plugTop) * 0.9f);
                float Plug(float y) => y <= plugTop ? rp : Mathf.Lerp(rp, r * 1.03f, Mathf.SmoothStep(0f, 1f, (y - plugTop) / taper));
                float Top(float y) => y <= y1 ? r * 1.03f : Mathf.Lerp(r, re, Mathf.SmoothStep(0f, 1f, (y - y1) / ramp));
                for (int k = 0; k <= 5; k++) { float a = k / 5f * Mathf.PI * 0.5f; p.Add(new Vector2(rp * Mathf.Sin(a), hd2 * (1f - Mathf.Cos(a)))); }
                p.Add(new Vector2(rp, plugTop));
                for (int k = 1; k <= 14; k++) { float y = Mathf.Lerp(plugTop, len - hd, k / 14f); p.Add(new Vector2(Mathf.Min(Plug(y), Top(y)), y)); }
                float rEnd = Mathf.Min(Plug(len - hd), re);
                for (int k = 1; k <= 5; k++) { float a = k / 5f * Mathf.PI * 0.5f; p.Add(new Vector2(rEnd * Mathf.Cos(a), len - hd + hd * Mathf.Sin(a))); }
                return p;
            }
            for (int k = 0; k <= 5; k++) { float a = k / 5f * Mathf.PI * 0.5f; p.Add(new Vector2(re * Mathf.Sin(a), hd * (1f - Mathf.Cos(a)))); }
            for (int k = 1; k <= 4; k++) { float t = k / 4f; p.Add(new Vector2(Mathf.Lerp(re, r, Mathf.SmoothStep(0, 1, t)), hd + ramp * t)); }
            for (int k = 1; k <= 5; k++) { float t = k / 6f; p.Add(new Vector2(r * (1f + 0.04f * Mathf.Sin(t * Mathf.PI)), Mathf.Lerp(y0, y1, t))); }
            for (int k = 0; k <= 4; k++) { float t = k / 4f; p.Add(new Vector2(Mathf.Lerp(r, re, Mathf.SmoothStep(0, 1, t)), y1 + ramp * t)); }
            for (int k = 1; k <= 5; k++) { float a = k / 5f * Mathf.PI * 0.5f; p.Add(new Vector2(re * Mathf.Cos(a), len - hd + hd * Mathf.Sin(a))); }
            return p;
        }

        /// <summary>造一节四肢挂到 joint 下：从 y0 往下到 y1（y1 &lt; y0）。主体 + 装饰（箍 / 竹节 / 铁护腕）。
        /// plugR / plugTop：前臂手腕端的腕榫（半径、从下端往上多高以内都是细的），装饰只放在腕榫以上露出来的那段。</summary>
        public static void Limb(Transform joint, string name, float y0, float y1, float w, Style st, Material lit, float plugR = 0f, float plugTop = 0f)
        {
            float len = y0 - y1, r = w * 0.5f; if (len <= 1e-5f) return;
            var g = new SBGeo { M = Matrix4x4.TRS(new Vector3(0, y1, 0), Quaternion.identity, Vector3.one) };
            g.Lathe(Vector3.zero, LimbProfile(len, r, plugR, plugTop), 16, 0f, false, false);
            var main = g.Make(joint, name, Mat(lit, st.tint, st.tile, st.metal, st.smooth, new Vector2(1f, Mathf.Max(1f, len / (w * 2.2f)))));
            var deco = new SBGeo { M = g.M }; var rivets = new SBGeo { M = g.M };
            var prof = LimbProfile(len, r, plugR, plugTop);
            float vis0 = plugR > 0f && plugR < r ? Mathf.Clamp(plugTop + r * 1.1f, 0f, len * 0.6f) : 0f;   // 箍 / 护臂从护腕口上方一截才开始，不和拳套叠在一起   // 露在外面的那段从哪开始
            float Y(float f01) => vis0 + (len - vis0) * f01;
            float RAt(float y) { for (int i = 1; i < prof.Count; i++) if (prof[i].y >= y) { var a = prof[i - 1]; var c = prof[i]; return Mathf.Lerp(a.x, c.x, Mathf.InverseLerp(a.y, c.y, y)); } return prof[prof.Count - 1].x; }
            foreach (var b in st.bands)
            {
                float yc = Y(b), hw = len * st.bandW * 0.5f;
                deco.Lathe(Vector3.zero, new[] { new Vector2(RAt(yc - hw) * 0.99f, yc - hw), new Vector2(RAt(yc - hw * 0.6f) * 1.06f, yc - hw * 0.6f), new Vector2(RAt(yc + hw * 0.6f) * 1.07f, yc + hw * 0.6f), new Vector2(RAt(yc + hw) * 0.99f, yc + hw) }, 16, 0f, false, false);
            }
            if (st.nodes)   // 竹节：中间一道凸起的节，节上下各一道细线
                foreach (float b in new[] { 0.5f })
                {
                    float yc = Y(b);
                    deco.Lathe(Vector3.zero, new[] { new Vector2(RAt(yc - r * 0.25f) * 0.99f, yc - r * 0.25f), new Vector2(RAt(yc - r * 0.05f) * 1.09f, yc - r * 0.05f), new Vector2(RAt(yc + r * 0.05f) * 1.09f, yc + r * 0.05f), new Vector2(RAt(yc + r * 0.25f) * 0.99f, yc + r * 0.25f) }, 16, 0f, false, false);
                }
            if (st.bracer)  // 铁护腕：占中段 50%，略粗；上下两道铜边 + 前后左右铆钉
            {
                float a = Y(0.27f), b = Y(0.73f), m = (a + b) * 0.5f;
                deco.Lathe(Vector3.zero, new[] { new Vector2(RAt(a) * 0.98f, a), new Vector2(RAt(a + r * 0.12f) * 1.1f, a + r * 0.12f), new Vector2(RAt(m) * 1.122f, m), new Vector2(RAt(b - r * 0.12f) * 1.1f, b - r * 0.12f), new Vector2(RAt(b) * 0.98f, b) }, 16, 0f, false, false);
                foreach (float yy in new[] { a + r * 0.18f, b - r * 0.18f })
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = k * Mathf.PI / 3f;
                        float rr = RAt(yy) * 1.1f * 1.02f;
                        rivets.Blob(new Vector3(Mathf.Sin(ang) * rr, yy, Mathf.Cos(ang) * rr), Vector3.one * r * 0.11f, Quaternion.identity, 2f, 5, 8);
                    }
            }
            if (!deco.Empty) deco.Make(joint, name + "_箍", Mat(lit, st.band, null, st.bandMetal, st.bandSmooth, Vector2.one));
            if (!rivets.Empty) rivets.Make(joint, name + "_铆钉", Mat(lit, new Color(.85f, .66f, .32f), null, 0.6f, 0.55f, Vector2.one));
        }

        /// <summary>胯块：圆角方块（超椭球），上沿一圈腰带 / 绑绳。center = 胯块中心，size = 全尺寸。</summary>
        public static void Pelvis(Transform parent, Vector3 center, Vector3 size, Style st, Material lit)
        {
            var g = new SBGeo(); g.Blob(center, size * 0.5f, Quaternion.identity, 3.2f, 14, 22);
            g.Make(parent, "胯块", Mat(lit, st.pelvis, st.pelvisTile, st.pelvisMetal, st.pelvisSmooth, new Vector2(2f, 1f)));
            if (st.sash.a > 0)
            {
                var s = new SBGeo(); float e = 2f / 3.2f, yb = center.y + size.y * 0.32f;
                var ring = new List<Vector3>();
                // 超椭球在 yb 这一高度的截面（水平截面也是超椭圆）
                float k = Mathf.Pow(1f - Mathf.Pow(Mathf.Abs((yb - center.y) / (size.y * 0.5f)), 3.2f), 1f / 3.2f);
                for (int i = 0; i < 28; i++)
                {
                    float a = i / 28f * Mathf.PI * 2f, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    ring.Add(new Vector3(center.x + Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e) * size.x * 0.5f * k * 1.01f, yb, center.z + Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e) * size.z * 0.5f * k * 1.01f));
                }
                s.Tube(ring, size.y * 0.09f, 6, true);
                s.Make(parent, "腰带", Mat(lit, st.sash, null, 0f, 0.35f, Vector2.one));
            }
        }

        /// <summary>程序躯干（竹竿侠的竹筒 / 木人桩的木桩）：从 y0 高 h，半径 r，顶和底都封成圆头。</summary>
        public static void ProcChest(Transform parent, float y0, float h, float r, Style st, Material lit)
        {
            var g = new SBGeo();
            var p = new List<Vector2>();
            float cap = r * 0.35f;
            for (int k = 0; k <= 4; k++) { float a = k / 4f * Mathf.PI * 0.5f; p.Add(new Vector2(r * Mathf.Sin(a), y0 + cap * (1f - Mathf.Cos(a)))); }
            p.Add(new Vector2(r, y0 + h - cap));
            for (int k = 1; k <= 4; k++) { float a = k / 4f * Mathf.PI * 0.5f; p.Add(new Vector2(r * Mathf.Cos(a), y0 + h - cap + cap * Mathf.Sin(a))); }
            g.Lathe(Vector3.zero, p, 20, 0f, false, false);
            g.Make(parent, "躯干", Mat(lit, st.pelvis, st.pelvisTile, st.pelvisMetal, st.pelvisSmooth, new Vector2(1.5f, h / (r * 2.5f))));
            var d = new SBGeo();
            if (st.procChest == "bamboo")
                foreach (float t in new[] { 0.42f, 0.86f })
                {
                    float yc = y0 + h * t;
                    d.Lathe(Vector3.zero, new[] { new Vector2(r * 0.99f, yc - r * 0.18f), new Vector2(r * 1.08f, yc - r * 0.04f), new Vector2(r * 1.08f, yc + r * 0.04f), new Vector2(r * 0.99f, yc + r * 0.18f) }, 20, 0f, false, false);
                }
            else
                foreach (float t in new[] { 0.25f, 0.8f })
                {
                    float yc = y0 + h * t, bw = h * 0.05f;
                    d.Lathe(Vector3.zero, new[] { new Vector2(r * 0.99f, yc - bw), new Vector2(r * 1.05f, yc - bw * 0.6f), new Vector2(r * 1.05f, yc + bw * 0.6f), new Vector2(r * 0.99f, yc + bw) }, 20, 0f, false, false);
                }
            d.Make(parent, "躯干箍", Mat(lit, st.procChest == "bamboo" ? st.tint * 0.95f : new Color(.25f, .2f, .17f), st.procChest == "bamboo" ? st.tile : null, st.procChest == "bamboo" ? 0f : 0.4f, 0.45f, Vector2.one));
        }
    }
}
