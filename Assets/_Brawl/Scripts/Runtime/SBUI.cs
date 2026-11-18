using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>界面工具：中英双语、三种字体、倾斜色块标签（格斗海报 + 拼贴感）、海报卡、冲出来 / 抖动 / 闪的动画。
    /// 所有界面都是场景里的实物（吊在八角笼上方的木牌、海报），没有贴在脸上的平面 UI。
    /// 配色：墨黑、朱红、米白、翠绿、钴蓝、金。</summary>
    public static class SBUI
    {
        public static readonly Color Ink = new(.06f, .05f, .05f), Red = new(.86f, .16f, .12f), Cream = new(.98f, .93f, .82f),
                                     Jade = new(.1f, .58f, .45f), Cobalt = new(.15f, .32f, .78f), Gold = new(1f, .74f, .18f);

        // ───────── 语言 ─────────
        public static bool En { get; private set; }
        public static void InitLang()
        {
            if (PlayerPrefs.HasKey("sb_lang")) En = PlayerPrefs.GetInt("sb_lang") == 1;
            else En = Application.systemLanguage != SystemLanguage.Chinese && Application.systemLanguage != SystemLanguage.ChineseSimplified && Application.systemLanguage != SystemLanguage.ChineseTraditional;
        }
        public static void ToggleLang() { En = !En; PlayerPrefs.SetInt("sb_lang", En ? 1 : 0); PlayerPrefs.Save(); }
        public static string L(string zh, string en) => En ? en : zh;

        // ───────── 字体与文字 ─────────
        public enum F { Title, Body }
        static Font zh, en, body;
        static readonly Dictionary<Font, Material> mats = new();
        public static Shader textShader;
        public static Material unlit;

        public static void Init(Shader text, Material unlitTemplate)
        {
            textShader = text; unlit = unlitTemplate;
            zh = Resources.Load<Font>("Fonts/SBZh"); en = Resources.Load<Font>("Fonts/SBEn"); body = Resources.Load<Font>("Fonts/SBBody");
            Font.textureRebuilt += f => { if (mats.TryGetValue(f, out var m)) m.mainTexture = f.material.mainTexture; dirty = true; };
            InitLang();
        }

        static Font Pick(F f) => f == F.Body ? body : En ? en : zh;
        static Material MatFor(Font f)
        {
            if (!mats.TryGetValue(f, out var m)) { m = new Material(textShader) { mainTexture = f.material.mainTexture }; mats[f] = m; }
            return m;
        }

        // 动态字体图集重建后，已有文字的贴图坐标会失效（显示成别的字），要整体重排一次
        static bool dirty;
        public static void Prewarm(string chars)
        {
            foreach (var f in new[] { zh, en, body }) if (f) f.RequestCharactersInTexture(chars, 80);
        }
        public static void FixRebuilt()
        {
            if (!dirty) return; dirty = false;
            foreach (var t in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if (!t.font) continue;
                var r = t.GetComponent<MeshRenderer>(); if (mats.TryGetValue(t.font, out var m)) m.mainTexture = t.font.material.mainTexture;
                var s = t.text; t.text = ""; t.text = s;
            }
        }

        /// <summary>一段文字（size = 字高，米）。中文标题用站酷庆科黄油体，英文标题用 Bangers，正文用思源黑体。</summary>
        public static TextMesh Text(Transform parent, string s, float size, Color c, F f = F.Title, TextAnchor anchor = TextAnchor.MiddleCenter, Vector3 pos = default)
        {
            var go = new GameObject("字"); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var t = go.AddComponent<TextMesh>();
            var font = Pick(f);
            t.font = font; t.fontSize = 80; t.characterSize = size / 8f * (f == F.Title && En ? 1.15f : 1f);
            t.anchor = anchor; t.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft ? TextAlignment.Left : anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Center;
            t.color = c; t.text = s; t.lineSpacing = 0.85f;
            go.GetComponent<MeshRenderer>().material = MatFor(font);
            var tf = go.AddComponent<SBTextFont>(); tf.kind = f; tf.baseSize = t.characterSize; tf.basePos = pos;
            // 居中的单行字：按“看得见的笔画”居中，而不是按字格居中。全角标点（！。？）的笔画只占字格的一小半，
            // 按字格居中时整行字会偏到一边（“胜！”偏左一厘米）。
            if (anchor == TextAnchor.MiddleCenter && s.IndexOf('\n') < 0) go.transform.localPosition += new Vector3(InkShift(t, s), 0, 0);
            return t;
        }

        // ───────── 文字不出牌面 ─────────
        /// <summary>把 root 下面所有的字检查一遍：每个字找到它所在的那块牌面（同一个父节点下、包住它的那块色块 / 纸条），
        /// 量一下字实际占的范围，超出牌面的可写范围（再留一圈边距）就整体缩小到刚好放得下。字是动态改的（记分屏上的名字）也可以再调一次。</summary>
        public static void Fit(Transform root)
        {
            if (!root) return;
            foreach (var t in root.GetComponentsInChildren<TextMesh>(true)) FitOne(t);
        }
        public static void FitOne(TextMesh t)
        {
            var tf = t.GetComponent<SBTextFont>(); var par = t.transform.parent; if (!tf || !par || string.IsNullOrEmpty(t.text) || tf.baseSize <= 0) return;
            t.characterSize = tf.baseSize;
            if (!InkRect(t, out float x0, out float x1, out float y0, out float y1)) return;
            var tp = tf.posSet ? tf.basePos : t.transform.localPosition;
            // 找牌面：父节点的直接子物体里，包住这行字的锚点、面积最小的那一块（一样大的正片和影子，取没挪位置的那块）
            SBPanel pn = null; float bestArea = float.MaxValue; Vector2 o = default;
            for (int i = 0; i < par.childCount; i++)
            {
                var p = par.GetChild(i).GetComponent<SBPanel>(); if (!p) continue;
                var lp = p.transform.localPosition; var d = new Vector2(tp.x - lp.x, tp.y - lp.y);
                if (Mathf.Abs(d.x) > p.w / 2 || Mathf.Abs(d.y) > p.h / 2) continue;
                float area = p.w * p.h + new Vector2(lp.x, lp.y).magnitude * 0.01f;
                if (area < bestArea) { bestArea = area; pn = p; o = d; }
            }
            if (!pn) return;
            float pad = Mathf.Clamp(Mathf.Min(pn.w, pn.h) * 0.04f, 0.0006f, 0.003f);
            // 字的四个角都要在牌面里（牌面是斜切的：越往上，左右两条边越往一边偏）。先试着挪一点（行首被斜边切到的，往里挪），挪不进去再缩小
            if (!tf.posSet) { tf.basePos = tp; tf.posSet = true; }
            tp = tf.basePos; o += Vector2.zero;
            bool Try(float k, out Vector2 sh)
            {
                sh = Vector2.zero;
                float H = pn.h / 2 - pn.jitY - pad, dyLo = -H - (o.y + y0 * k), dyHi = H - (o.y + y1 * k);
                if (dyLo > dyHi + 1e-6f) return false;
                sh.y = Mathf.Clamp(0f, dyLo, dyHi);
                float dxLo = float.MinValue, dxHi = float.MaxValue;
                foreach (float yy in new[] { o.y + sh.y + y0 * k, o.y + sh.y + y1 * k })
                {
                    float L = -pn.w / 2 + pn.skewL * yy / pn.h + pn.jitX + pad, R = pn.w / 2 + pn.skewR * yy / pn.h - pn.jitX - pad;
                    dxLo = Mathf.Max(dxLo, L - (o.x + x0 * k)); dxHi = Mathf.Min(dxHi, R - (o.x + x1 * k));
                }
                if (dxLo > dxHi + 1e-6f) return false;
                sh.x = Mathf.Clamp(0f, dxLo, dxHi);
                return sh.magnitude < 0.03f;
            }
            float kk = 1f; Vector2 shift;
            while (!Try(kk, out shift) && kk > 0.5f) kk -= 0.025f;
            t.transform.localPosition = tf.basePos + new Vector3(shift.x, shift.y, 0);
            if (kk < 0.999f) t.characterSize = tf.baseSize * kk;
#if UNITY_EDITOR
            if (kk < 0.95f || shift.magnitude > 0.002f) Debug.Log($"[SB] 排版 缩 {kk:0.00} 挪 {shift.x * 100f:0.0},{shift.y * 100f:0.0}cm：「{t.text.Replace("\n", "/")}」 字 {(x1 - x0) * 100f:0.0}×{(y1 - y0) * 100f:0.0}cm 牌面 {pn.w * 100f:0.0}×{pn.h * 100f:0.0}cm（{par.name}）");
#endif
        }

        /// <summary>这段字实际的笔画范围（相对它自己的锚点，本地坐标）。横向按每个字的字形算，纵向按首行最高的笔画、末行最低的笔画算。</summary>
        static bool InkRect(TextMesh t, out float x0, out float x1, out float y0, out float y1)
        {
            x0 = y0 = float.MaxValue; x1 = y1 = float.MinValue;
            var f = t.font; var s = t.text; if (!f || !f.dynamic) return false;
            f.RequestCharactersInTexture(s, t.fontSize, t.fontStyle);
            float unit = t.characterSize * 0.1f, sc = (float)t.fontSize / Mathf.Max(1, f.fontSize), asc = f.ascent * sc, lh = f.lineHeight * sc;
            var b = t.GetComponent<MeshRenderer>().localBounds; if (b.size.x <= 0 || b.size.y <= 0) return false;
            var lines = s.Split('\n'); float firstTop = asc, lastBot = -(lh - asc);
            for (int i = 0; i < lines.Length; i++)
            {
                float pen = 0, lo = float.MaxValue, hi = float.MinValue, top = float.MinValue, bot = float.MaxValue;
                foreach (var ch in lines[i])
                {
                    if (!f.GetCharacterInfo(ch, out var ci, t.fontSize, t.fontStyle)) return false;
                    if (ch != ' ' && ch != '\u3000' && ci.glyphWidth > 0) { lo = Mathf.Min(lo, pen + ci.minX); hi = Mathf.Max(hi, pen + ci.maxX); top = Mathf.Max(top, ci.maxY); bot = Mathf.Min(bot, ci.minY); }
                    pen += ci.advance;
                }
                if (hi < lo) continue;
                float start = t.alignment == TextAlignment.Left ? b.min.x / unit : t.alignment == TextAlignment.Right ? b.max.x / unit - pen : b.center.x / unit - pen / 2f;
                x0 = Mathf.Min(x0, (start + lo) * unit); x1 = Mathf.Max(x1, (start + hi) * unit);
                if (i == 0) firstTop = top; if (i == lines.Length - 1) lastBot = bot;
            }
            y1 = b.max.y - (asc - firstTop) * unit; y0 = b.min.y + (lastBot + (lh - asc)) * unit;
            return x1 > x0 && y1 > y0;
        }

        /// <summary>这一行字的笔画中心比字格中心偏了多少（本地坐标，往右为正时要把字往右挪这么多）。</summary>
        static float InkShift(TextMesh t, string s)
        {
            var f = t.font; if (!f || !f.dynamic || string.IsNullOrEmpty(s)) return 0f;
            f.RequestCharactersInTexture(s, t.fontSize, t.fontStyle);
            float pen = 0, lo = float.MaxValue, hi = float.MinValue;
            foreach (var ch in s)
            {
                if (!f.GetCharacterInfo(ch, out var ci, t.fontSize, t.fontStyle)) return 0f;
                if (ch != ' ' && ch != '\u3000' && ci.glyphWidth > 0) { lo = Mathf.Min(lo, pen + ci.minX); hi = Mathf.Max(hi, pen + ci.maxX); }
                pen += ci.advance;
            }
            if (hi < lo) return 0f;
            return (pen * 0.5f - (lo + hi) * 0.5f) * t.characterSize * 0.1f;   // 字号 80 时，一个像素 = characterSize × 0.1
        }

        /// <summary>不管当前语言，强制用英文标题字体（VS、TWIN STRINGS 这类固定英文字）。</summary>
        public static void EnFont(TextMesh t) => FixFont(t, en);
        public static void ZhFont(TextMesh t) => FixFont(t, zh);
        static void FixFont(TextMesh t, Font f)
        {
            t.font = f; t.GetComponent<MeshRenderer>().sharedMaterial = MatFor(f);
            var k = t.GetComponent<SBTextFont>(); if (k) Object.Destroy(k);
        }

        /// <summary>切换语言后重新挑字体。</summary>
        public static void Refont(TextMesh t)
        {
            var k = t.GetComponent<SBTextFont>(); if (!k) return;
            var font = Pick(k.kind); if (t.font == font) return;
            t.font = font; t.GetComponent<MeshRenderer>().material = MatFor(font);
        }

        // ───────── 色块 / 海报 ─────────
        public static Material Solid(Color c) { var m = new Material(unlit) { color = c }; return m; }
        public static Material Tex(Texture t) { var m = new Material(unlit); m.mainTexture = t; m.color = Color.white; return m; }

        /// <summary>平行四边形色块（拼贴风的标签底）：宽 w、高 h、斜切 skew（右上往右偏多少米）。</summary>
        public static Transform Slab(Transform parent, Vector3 pos, float w, float h, float skew, Color c, float zOff = 0.001f, float jitter = 0f, int seed = 0)
        {
            var g = new GameObject("色块"); g.transform.SetParent(parent, false); g.transform.localPosition = pos + Vector3.forward * zOff;
            var m = new Mesh();
            // jitter > 0：四个角各自随机挪一点，像手剪的纸片，不是规整的平行四边形
            var r = new System.Random(seed); float J() => ((float)r.NextDouble() * 2f - 1f) * jitter * Mathf.Min(w, h);
            m.vertices = new[] { new Vector3(-w / 2 - skew / 2 + J(), -h / 2 + J(), 0), new Vector3(w / 2 - skew / 2 + J(), -h / 2 + J(), 0), new Vector3(w / 2 + skew / 2 + J(), h / 2 + J(), 0), new Vector3(-w / 2 + skew / 2 + J(), h / 2 + J(), 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().material = Solid(c);
            {   // 这块牌面上能写字的范围：扣掉斜切掉的两头和手剪的毛边
                var pn = g.AddComponent<SBPanel>(); pn.w = w; pn.h = h; pn.skewL = pn.skewR = skew; pn.jitX = pn.jitY = jitter * Mathf.Min(w, h);
            }
            return g.transform;
        }

        /// <summary>海报卡：贴图 + 一圈描边。</summary>
        public static Transform Poster(Transform parent, Vector3 pos, float w, float h, Texture tex, Color frame, float border = 0.004f)
        {
            var root = new GameObject("海报").transform; root.SetParent(parent, false); root.localPosition = pos;
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(root, false); back.transform.localScale = new Vector3(w + border * 2, h + border * 2, 1); back.transform.localPosition = new Vector3(0, 0, 0.0008f);
            back.GetComponent<Renderer>().material = Solid(frame);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root, false); q.transform.localScale = new Vector3(w, h, 1);
            q.GetComponent<Renderer>().material = tex ? Tex(tex) : Solid(Ink);
            return root;
        }

        /// <summary>带底色的标签：倾斜色块 + 文字（P5 式的斜切拼贴字条），返回根节点。</summary>
        public static Transform Tag(Transform parent, Vector3 pos, string s, float size, Color fg, Color bg, float tilt = -4f, F f = F.Title, float padX = 0.6f, float padY = 0.35f)
        {
            var root = new GameObject("标签").transform; root.SetParent(parent, false); root.localPosition = pos; root.localRotation = Quaternion.Euler(0, 0, tilt);
            var t = Text(root, s, size, fg, f, TextAnchor.MiddleCenter, new Vector3(0, 0, -0.0012f));
            float w = Mathf.Max(size * 2f, EstWidth(s, size, f)) + size * padX * 2, h = size * (1f + padY * 2) * Lines(s);
            Slab(root, Vector3.zero, w, h, size * 0.5f, bg);
            FitOne(t);
            return root;
        }

        // ───────── 撕边纸条 ─────────
        /// <summary>上下两条边是撕出来的毛边、左右是斜剪的纸条（印说明文字用）。</summary>
        public static Transform Paper(Transform parent, Vector3 pos, float w, float h, Color c, int seed, float zOff = 0.001f)
        {
            var g = new GameObject("纸条"); g.transform.SetParent(parent, false); g.transform.localPosition = pos + Vector3.forward * zOff;
            int n = Mathf.Max(8, Mathf.RoundToInt(w / 0.005f));
            var r = new System.Random(seed); float R() => (float)r.NextDouble();
            float cutL = (R() - 0.5f) * h * 0.25f, cutR = (R() - 0.5f) * h * 0.25f;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n, x = -w / 2 + w * k;
                float edge = Mathf.Lerp(cutL, cutR, k);
                v.Add(new Vector3(x - edge, -h / 2 + (R() - 0.5f) * 0.0035f, 0));   // 下边
                v.Add(new Vector3(x + edge, h / 2 + (R() - 0.5f) * 0.0035f, 0));    // 上边
            }
            for (int i = 0; i < n; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = i * 2 + 2, t1 = i * 2 + 3;
                t.Add(b0); t.Add(t1); t.Add(b1); t.Add(b0); t.Add(t0); t.Add(t1);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().material = Solid(c);
            {   // 能写字的范围：扣掉两头斜剪掉的部分和上下撕出来的毛边
                var pn = g.AddComponent<SBPanel>(); pn.w = w; pn.h = h; pn.skewL = cutL * 2f; pn.skewR = cutR * 2f; pn.jitX = 0f; pn.jitY = 0.00175f;
            }
            return g.transform;
        }

        // ───────── 逐字字块 ─────────
        /// <summary>一个字一块碎纸：每块形状、倾斜、大小、高低都不一样，配色轮换，后一块压着前一块，一块一块砸进来。
        /// 标点并到前一个字上。pairs = (字色, 底色) 轮换表。</summary>
        /// <summary>“一字一块牌”的一排字总共多宽（和 Letters 里的算法一致）。</summary>
        public static float LettersWidth(string s, float size, bool forceEn = false, F f = F.Title)
        {
            var lf = forceEn ? en : Pick(f); float lk = size / 80f * (f == F.Title && En ? 1.15f : 1f), total = 0; string tk = null;
            float W(string t)
            {
                if (t == " ") return size * 0.4f;
                if (t[0] > 0x2E80) return size * (1.2f + (t.Length - 1) * 0.35f) * (f == F.Title && En ? 1.15f : 1f);
                float basic = size * (0.8f + (t.Length - 1) * 0.3f);
                if (!lf || !lf.dynamic) return basic;
                lf.RequestCharactersInTexture(t, 80); float adv = 0; foreach (var ch in t) if (lf.GetCharacterInfo(ch, out var ci, 80)) adv += ci.advance * lk;
                return Mathf.Max(basic, adv + size * 0.52f);
            }
            foreach (var ch in s)
            {
                bool punct = ".,!?！？。，、:：".IndexOf(ch) >= 0;
                if (punct && tk != null) { tk += ch; continue; }
                if (tk != null) total += W(tk) * 0.92f;
                tk = ch.ToString();
            }
            if (tk != null) total += W(tk) * 0.92f;
            return total;
        }

        public static Transform Letters(Transform parent, Vector3 pos, string s, float size, (Color fg, Color bg)[] pairs, int seed,
                                        float delay = 0f, float stagger = 0.05f, float tiltAmp = 9f, bool forceEn = false, F f = F.Title, float maxW = 0f)
        {
            var root = new GameObject("字块 " + s).transform; root.SetParent(parent, false); root.localPosition = pos;
            var toks = new List<string>();
            foreach (var ch in s)
            {
                bool punct = ".,!?！？。，、:：".IndexOf(ch) >= 0;
                if (punct && toks.Count > 0) toks[^1] += ch; else toks.Add(ch.ToString());
            }
            var r = new System.Random(seed); float R() => (float)r.NextDouble();
            var lf = forceEn ? en : Pick(f); float lk = size / 80f * (f == F.Title && En ? 1.15f : 1f);
            float W(string tk)
            {
                if (tk == " ") return size * 0.4f;
                if (tk[0] > 0x2E80) return size * (1.2f + (tk.Length - 1) * 0.35f) * (f == F.Title && En ? 1.15f : 1f);   // 英文界面里标题字放大了 1.15 倍，牌子跟着放大
                float basic = size * (0.8f + (tk.Length - 1) * 0.3f);
                if (!lf || !lf.dynamic) return basic;
                lf.RequestCharactersInTexture(tk, 80); float adv = 0; foreach (var ch in tk) if (lf.GetCharacterInfo(ch, out var ci, 80)) adv += ci.advance * lk;
                return Mathf.Max(basic, adv + size * 0.52f);   // 宽字母（M、W）的牌子跟着变宽，字不出牌（牌子是斜切、手剪的，两边要多留一点）
            }
            float total = 0; foreach (var tk in toks) total += W(tk) * 0.92f;
            float x = -total / 2; int k = 0;
            for (int i = 0; i < toks.Count; i++)
            {
                var tk = toks[i]; float w = W(tk);
                if (tk == " ") { x += w; continue; }
                var p = pairs[(k + seed) % pairs.Length];
                var g = new GameObject(tk).transform; g.SetParent(root, false);
                g.localPosition = new Vector3(x + w * 0.46f, (R() - 0.5f) * size * 0.24f, -0.0016f * (k % 3));
                g.localRotation = Quaternion.Euler(0, 0, (R() * 2f - 1f) * tiltAmp);
                g.localScale = Vector3.one * (0.9f + R() * 0.24f);
                float hh = size * (1.22f + R() * 0.12f);
                if (p.bg != Ink) Slab(g, new Vector3(size * 0.07f, -size * 0.07f, 0), w * 0.97f, hh, (R() - 0.5f) * size * 0.3f, Ink, 0.0012f, 0.07f, seed + i * 13);
                Slab(g, Vector3.zero, w * 0.95f, hh, (R() - 0.5f) * size * 0.3f, p.bg, 0.0006f, 0.07f, seed + i * 7);
                var t = Text(g, tk, size, p.fg, f, TextAnchor.MiddleCenter, new Vector3(0, size * 0.03f, -0.0008f));
                if (forceEn) EnFont(t);
                FitOne(t);
                var pop = g.gameObject.AddComponent<SBPop>(); pop.delay = delay + k * stagger; pop.from = 2.6f; pop.dur = 0.2f;
                pop.slideFrom = new Vector3((R() - 0.5f) * size, size * (0.5f + R()), 0);
                x += w * 0.92f; k++;
            }
            if (maxW > 0f && total > maxW) root.localScale = Vector3.one * (maxW / total);   // 这一排字太宽：整排缩小到给它留的宽度里，不压到旁边的牌子
            return root;
        }

        public static readonly (Color, Color)[] Hot = { (Cream, Red), (Cream, Ink), (Ink, Gold), (Cream, Red), (Ink, Cream) };
        public static readonly (Color, Color)[] Cool = { (Cream, Ink), (Cream, Cobalt), (Ink, Cream), (Cream, Jade) };
        public static readonly (Color, Color)[] Gilt = { (Ink, Gold), (Gold, Ink), (Ink, Gold), (Cream, Red) };

        public static int Lines(string s) { int n = 1; foreach (var ch in s) if (ch == '\n') n++; return n; }
        /// <summary>一行字按字体里每个字的真实步进量出来的宽度（EstWidth 会取“粗估”和它之间大的那个，用来做牌子偏保守；要把两样东西挨着排齐时用这个）。</summary>
        public static float RealWidth(string s, float size, F f)
        {
            var font = Pick(f); if (!font || !font.dynamic) return EstWidth(s, size, f);
            font.RequestCharactersInTexture(s, 80); float w = 0;
            foreach (var ch in s) w += font.GetCharacterInfo(ch, out var ci, 80) ? ci.advance * size / 80f : size * 0.5f;
            return w;
        }
        public static float EstWidth(string s, float size, F f)
        {
            float best = 0, cur = 0;
            foreach (var ch in s)
            {
                if (ch == '\n') { best = Mathf.Max(best, cur); cur = 0; continue; }
                cur += ch > 0x2E80 ? size * 1.0f : size * (f == F.Body ? 0.55f : 0.5f);
            }
            float est = Mathf.Max(best, cur);
            // 按字体里每个字的真实宽度再量一遍，取大的：英文标题字体的 M、W 比估的宽，牌子要跟着字做宽，不能让字伸出去
            var font = Pick(f);
            if (font && font.dynamic)
            {
                font.RequestCharactersInTexture(s, 80);
                float k = size / 80f * (f == F.Title && En ? 1.15f : 1f), mb = 0, mc = 0;
                foreach (var ch in s)
                {
                    if (ch == '\n') { mb = Mathf.Max(mb, mc); mc = 0; continue; }
                    if (font.GetCharacterInfo(ch, out var ci, 80)) mc += ci.advance * k; else mc += size * 0.5f;
                }
                est = Mathf.Max(est, Mathf.Max(mb, mc));
            }
            return est;
        }
    }

    /// <summary>吊着的东西轻轻晃（像提线木偶戏台上的道具）。</summary>
    public class SBSway : MonoBehaviour
    {
        public float amp = 1f, speed = 1f;
        Quaternion b; float ph;
        void Start() { b = transform.localRotation; ph = Random.value * 20f; }
        void Update()
        {
            float t = Time.unscaledTime * speed + ph;
            transform.localRotation = b * Quaternion.Euler(Mathf.Sin(t * 0.8f) * amp * 0.6f, 0, Mathf.Sin(t * 1.15f) * amp + Mathf.Sin(t * 2.7f) * amp * 0.25f);
        }
    }

    /// <summary>记住一段文字用哪类字体（切语言时换字体）。</summary>
    public class SBTextFont : MonoBehaviour { public SBUI.F kind; public float baseSize; public Vector3 basePos; public bool posSet; }
    /// <summary>一块能写字的牌面：inner = 上面可以写字的范围（本地坐标）。</summary>
    public class SBPanel : MonoBehaviour { public float w, h, skewL, skewR, jitX, jitY; }   // 宽、高；左右两条边从下到上斜了多少；边缘的毛边

    /// <summary>界面元素的出场动画：从远处“砸”过来（放大 → 回弹）、轻微抖、再慢慢呼吸。</summary>
    public class SBPop : MonoBehaviour
    {
        public float delay, dur = 0.35f, from = 2.2f, wobble = 0f;
        public Vector3 slideFrom;            // 从哪个方向滑进来（本地坐标偏移）
        Vector3 basePos, baseScale; float t = float.NaN; bool init;

        void Init() { if (init) return; init = true; basePos = transform.localPosition; baseScale = transform.localScale; transform.localScale = Vector3.zero; }
        void OnEnable() { Init(); t = float.NaN; transform.localScale = Vector3.zero; }
        void Update()
        {
            Init();
            if (float.IsNaN(t)) t = -delay;   // 第一帧才读 delay（AddComponent 之后才设的参数也能生效）
            t += Time.unscaledDeltaTime;
            if (t < 0) { transform.localScale = Vector3.zero; return; }
            float k = Mathf.Clamp01(t / dur);
            // 回弹：先冲过头再回来
            float s = k < 1f ? Mathf.Lerp(from, 1f, 1f - Mathf.Pow(1f - k, 3f)) + Mathf.Sin(k * Mathf.PI) * 0.08f : 1f;
            transform.localScale = baseScale * s;
            transform.localPosition = basePos + slideFrom * Mathf.Pow(1f - k, 3f);
            if (k >= 1f)
            {
                if (wobble <= 0) { transform.localScale = baseScale; transform.localPosition = basePos; enabled = false; return; }
                transform.localScale = baseScale * (1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.015f * wobble);
            }
        }
    }
}
