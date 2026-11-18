using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>整套界面（全是场景里的实物，没有贴脸平面 UI）。母题是“提线木偶戏台”：
    /// - 笼子远端观众头顶是一面“吊挂墙”：海报、逐字字块、撕边纸条，每一件都用自己的线从上面吊下来、各自轻轻晃，前后错开深度（头显里有真实的层次）；
    ///   换页时旧的收走，新的一件件从上面坠下来；打比赛时整面收上去；
    /// - 选择牌：挂在玩家这面铁丝网外的斜木牌，手掌移过去会翘起来变金色，捏一下（或捏住不放）就选中；
    /// - 场中大字：回合、开打、K.O.、必杀名，一个字一块碎纸，一块块砸过来；
    /// - 版面刻意不对称、互相压边，像手工拼贴；纸片四角各自歪一点，不是规整色块。</summary>
    public class SBStage : MonoBehaviour
    {
        public Transform arena, head;
        public const float BW = 0.52f, BH = 0.29f;
        static readonly Vector3 BoardHome = new(0, 0.325f, 0.29f);   // 笼子后半边上空：在桁架圈（半径 0.26）外、第一排看台（边心距 0.32）前，前后都不碰东西
        const float BoardScale = 1.1f;   // 离眼睛比原来近 0.24 米，看上去比原来还大一点
        const float BarY = 0.33f;   // 吊景杆在墙面坐标里的高度

        Transform board, content;
        float boardK, boardWant;
        Transform slam; float slamT;
        Transform ultHint, handHint;
        readonly List<(Transform t, Transform target, float up)> follows = new();
        readonly List<Transform> billboards = new();
        Material rope, peg;

        // ───────── 贴图 ─────────
        static readonly Dictionary<string, Texture2D> tex = new();
        public static Texture2D T(string name)
        {
            if (!tex.TryGetValue(name, out var t))
            {
                t = Resources.Load<Texture2D>("UI/" + name); tex[name] = t;
                if (t && name.StartsWith("card_")) { t.anisoLevel = 8; t.mipMapBias = -0.8f; }   // 角色卡：斜着看、离得远也保持清楚
            }
            return t;
        }

        public void Build()
        {
            rope = SBUI.Solid(new Color(.9f, .85f, .72f));
            peg = SBUI.Solid(new Color(.55f, .38f, .22f));
            board = new GameObject("吊挂墙").transform; board.SetParent(arena, false);
            board.localPosition = BoardHome; board.localRotation = Quaternion.Euler(8, 0, 0);
            content = new GameObject("墙面").transform; content.SetParent(board, false);
            board.localScale = Vector3.one * BoardScale;
            // 吊杆：一根黑色金属横杆（两头铜帽），两头用黑钢缆吊向上方；墙上所有东西的线都系在这根杆上。没有背幕，后面是场馆本身
            var beamMat = SBUI.Solid(new Color(.08f, .08f, .09f)); var capMat = SBUI.Solid(SBUI.Gold);
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(board, false); beam.transform.localPosition = new Vector3(0, BarY, 0.002f);
            beam.transform.localRotation = Quaternion.Euler(0, 0, 90); beam.transform.localScale = new Vector3(0.008f, BW * 0.55f, 0.008f);
            beam.GetComponent<Renderer>().material = beamMat;
            var cableM = SBUI.Solid(new Color(.06f, .06f, .06f));
            foreach (float x in new[] { -BW * 0.55f, BW * 0.55f })
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(board, false); cap.transform.localPosition = new Vector3(x, BarY, 0.002f); cap.transform.localScale = Vector3.one * 0.013f;
                cap.GetComponent<Renderer>().material = capMat;
                var r = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(r.GetComponent<Collider>());
                r.transform.SetParent(board, false); r.transform.localPosition = new Vector3(x * 0.92f, BarY + 0.4f, 0.002f);
                r.transform.localScale = new Vector3(0.0018f, 0.8f, 0.0018f); r.GetComponent<Renderer>().material = cableM;
            }
            board.gameObject.SetActive(false);
        }

        static Transform Cube(Transform p, Vector3 pos, Vector3 s, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(p, false); g.transform.localPosition = pos; g.transform.localScale = s; g.GetComponent<Renderer>().material = m; return g.transform;
        }

        // ───────── 每帧 ─────────
        public void Tick(float dt)
        {
            SBUI.FixRebuilt();
            if (fitIn > 0 && --fitIn == 0) SBUI.Fit(content);
            boardK = Mathf.MoveTowards(boardK, boardWant, dt * 2.2f);
            if (board)
            {
                bool vis = boardK > 0.001f;
                if (board.gameObject.activeSelf != vis) board.gameObject.SetActive(vis);
                float e = 1f - Mathf.Pow(1f - boardK, 3f);
                board.localPosition = BoardHome + Vector3.up * (1f - e) * 0.45f;   // 收上去 / 放下来
            }
            if (slam)
            {
                slamT -= dt;
                if (slamT <= 0) { Destroy(slam.gameObject); slam = null; }
            }
            foreach (var f in follows) if (f.t && f.target) f.t.position = f.target.position + Vector3.up * f.up;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i]; n.left -= dt;
                if (n.left <= 0 || !n.target || !n.t) { if (n.t) Destroy(n.t.gameObject); notes.RemoveAt(i); }
                else n.t.position = n.target.position + n.off;
            }
            if (thought)
            {
                thoughtLeft -= dt;
                if (thoughtLeft <= 0 || !thoughtTarget) { Destroy(thought.gameObject); thought = null; }
                else thought.position = thoughtTarget.position + Vector3.up * thoughtUp;
            }
            for (int i = billboards.Count - 1; i >= 0; i--)
            {
                var b = billboards[i]; if (!b) { billboards.RemoveAt(i); continue; }
                var d = b.position - head.position; d.y = 0;
                if (d.sqrMagnitude > 1e-5f) b.rotation = Quaternion.LookRotation(d);
            }
            TickTickets(dt);
            TickConfetti(dt);
        }

        public void HideBoard() => boardWant = 0;
        int fitIn;
        public bool BoardShown => boardWant > 0;

        void Clear()
        {
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            boardWant = 1; fitIn = 2;   // 新的一页排好之后，把所有字检查一遍：不许超出牌面
        }

        // ───────── 小工具 ─────────
        static SBPop Pop(Transform t, float delay, Vector3 slide = default, float from = 1.8f, float dur = 0.3f)
        {
            var p = t.gameObject.AddComponent<SBPop>(); p.delay = delay; p.slideFrom = slide; p.from = from; p.dur = dur; return p;
        }

        /// <summary>吊起来：在物件正上方做一个会晃的吊点，物件挂在下面；每个 x 一根线 + 一个小木夹。返回吊点。</summary>
        Transform Hang(Transform item, float halfH, float[] xs, float amp = 1f, float len = 0.3f, float topY = float.NaN)
        {
            if (item.parent == content) len = Mathf.Max(0.012f, (float.IsNaN(topY) ? BarY : topY) - (item.localPosition.y + halfH));   // 系到吊杆上（或上一个物件的底边）
            var hanger = new GameObject("吊点").transform; hanger.SetParent(item.parent, false);
            hanger.localPosition = item.localPosition + Vector3.up * (halfH + len);
            item.SetParent(hanger, false); item.localPosition = new Vector3(0, -(halfH + len), 0);
            foreach (float x in xs)
            {
                var r = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(r.GetComponent<Collider>());
                r.transform.SetParent(hanger, false); r.transform.localPosition = new Vector3(x, -len / 2f, 0.002f);
                r.transform.localScale = new Vector3(0.0009f, len, 0.0009f); r.GetComponent<Renderer>().material = rope;
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(hanger, false); c.transform.localPosition = new Vector3(x, -len + 0.003f, -0.001f);
                c.transform.localScale = new Vector3(0.006f, 0.014f, 0.007f); c.GetComponent<Renderer>().material = peg;
            }
            var sw = hanger.gameObject.AddComponent<SBSway>(); sw.amp = amp; sw.speed = 0.8f + Random.value * 0.5f;
            return hanger;
        }

        /// <summary>挂起来 + 从上面坠下来。</summary>
        Transform Drop(Transform item, float halfH, float[] xs, float delay, float amp = 1f, float len = 0.3f, Vector3 slide = default, float topY = float.NaN)
        {
            var h = Hang(item, halfH, xs, amp, len, topY);
            Pop(h, delay, slide == default ? Vector3.up * 0.28f : slide, 1f, 0.5f);
            return h;
        }

        /// <summary>小签：手剪的纸片（四角各歪一点）+ 错位黑影 + 字。</summary>
        public static Transform Chip(Transform parent, Vector3 pos, string s, float size, Color fg, Color bg, float tilt, SBUI.F f = SBUI.F.Body, int font = 0, bool shadow = true)
        {
            var root = new GameObject("签 " + s).transform; root.SetParent(parent, false); root.localPosition = pos; root.localRotation = Quaternion.Euler(0, 0, tilt);
            int seed = s.Length * 31 + (int)(size * 1e4f);
            float w = SBUI.EstWidth(s, size, f) + size * 1.1f, h = size * 1.55f * SBUI.Lines(s);
            if (shadow && bg != SBUI.Ink) SBUI.Slab(root, new Vector3(size * 0.14f, -size * 0.14f, 0), w, h, size * 0.4f, SBUI.Ink, 0.0016f, 0.06f, seed + 1);
            SBUI.Slab(root, Vector3.zero, w, h, size * 0.4f, bg, 0.0008f, 0.06f, seed);
            var t = SBUI.Text(root, s, size, fg, f, TextAnchor.MiddleCenter, new Vector3(0, 0, -0.0008f));
            if (font == 1) SBUI.ZhFont(t); else if (font == 2) SBUI.EnFont(t);
            SBUI.FitOne(t);
            return root;
        }

        /// <summary>撕边米白纸条 + 黑影，返回纸条根（字往上面放，z 用 -0.001）。</summary>
        static Transform Sheet(Transform parent, Vector3 pos, float w, float h, float tilt, int seed, Color? col = null)
        {
            var root = new GameObject("纸").transform; root.SetParent(parent, false); root.localPosition = pos; root.localRotation = Quaternion.Euler(0, 0, tilt);
            SBUI.Paper(root, new Vector3(0.004f, -0.004f, 0), w, h, SBUI.Ink, seed, 0.0018f);
            SBUI.Paper(root, Vector3.zero, w, h, col ?? SBUI.Cream, seed, 0.0008f);
            return root;
        }

        static TextMesh Txt(Transform p, Vector3 pos, string s, float size, Color c, SBUI.F f = SBUI.F.Body, TextAnchor a = TextAnchor.MiddleLeft)
            => SBUI.Text(p, s, size, c, f, a, pos);

        static Transform Diamond(Transform p, Vector3 pos, float s, Color c)
        {
            var d = SBUI.Slab(p, pos, s, s, 0, c, 0); d.localRotation = Quaternion.Euler(0, 0, 45); return d;
        }

        static float[] Ropes(float w) => w < 0.06f ? new[] { 0f } : new[] { -w * 0.32f, w * 0.32f };
        // 一排“一字一块牌”的大字实际多宽（maxW > 0：最多这么宽，超了整排缩小——Letters 那边要传同一个 maxW）
        static float LettersW(string s, float size, float maxW = 0f) { float w = SBUI.LettersWidth(s, size); return maxW > 0f ? Mathf.Min(w, maxW) : w; }

        // ───────── 标题 ─────────
        public void ShowTitle(bool handSeen, bool firstTime)
        {
            Clear();
            // 左：海报；右：一列从上往下——“双”“偶”两块大字（各吊一根线，像两只木偶）→ TWIN STRINGS（吊在大字下面）→ 一句话纸条 → 提示小签
            // 物件之间留空，线只往上连到正上方的物件，不穿过别的东西
            var poster = SBUI.Poster(content, new Vector3(-0.15f, 0f, 0), 0.18f, 0.24f, T("title"), SBUI.Cream, 0.004f);
            poster.localRotation = Quaternion.Euler(0, 0, -2.5f);
            Drop(poster, 0.122f, new[] { -0.06f, 0.06f }, 0f, 0.5f);
            float subTop;   // 下面那张一句话纸条的吊线往上系到哪（= 它正上方那个物件的底边）
            if (SBUI.En)
            {
                // 英文版：游戏名就是英文——“TWIN”“STRINGS”两块大字（红底 / 黑底，和中文版的“双”“偶”同一种做法，各自吊着），不再出现中文大字
                var a = SBUI.Letters(content, new Vector3(0.04f, 0.084f, -0.02f), "TWIN", 0.06f, new[] { (SBUI.Cream, SBUI.Red) }, 3, 0.55f, 0.04f, 6f, true, maxW: 0.17f);
                Drop(a, 0.036f, new[] { -0.05f, 0.05f }, 0.35f, 1.4f);
                var b = SBUI.Letters(content, new Vector3(0.075f, 0.004f, -0.03f), "STRINGS", 0.047f, new[] { (SBUI.Cream, SBUI.Ink) }, 8, 0.7f, 0.04f, 7f, true, maxW: 0.235f);
                Drop(b, 0.029f, new[] { -0.07f, 0.07f }, 0.5f, 1.2f, topY: 0.05f);
                subTop = -0.024f;
            }
            else
            {
                var a = SBUI.Letters(content, new Vector3(-0.005f, 0.075f, -0.02f), "双", 0.08f, new[] { (SBUI.Cream, SBUI.Red) }, 3, 0.55f, 0, 6f);
                Drop(a, 0.052f, new[] { 0f }, 0.35f, 1.8f);
                var b = SBUI.Letters(content, new Vector3(0.105f, 0.06f, -0.03f), "偶", 0.074f, new[] { (SBUI.Cream, SBUI.Ink) }, 8, 0.7f, 0, 8f);
                Drop(b, 0.048f, new[] { 0f }, 0.5f, 2.2f);
                var en = SBUI.Letters(content, new Vector3(0.06f, -0.018f, -0.02f), "TWIN STRINGS", 0.02f, SBUI.Gilt, 5, 0.9f, 0.03f, 6f, true, maxW: 0.2f);
                Drop(en, 0.015f, new[] { -0.06f, 0.045f }, 0.7f, 0.8f, topY: 0.012f);
                subTop = -0.034f;
            }
            var sub = Sheet(content, new Vector3(0.07f, -0.075f, -0.015f), 0.23f, 0.052f, 1.5f, 11);
            Txt(sub, new Vector3(-0.103f, 0, -0.001f), SBUI.L("五根手指，五根提线，\n一只木偶上擂台。", "Five fingers. Five strings.\nOne puppet in the cage."), SBUI.En ? 0.0135f : 0.015f, SBUI.Ink);
            Drop(sub, 0.026f, new[] { -0.08f, 0.08f }, 0.85f, 0.6f, topY: subTop);
            string hint = !handSeen ? SBUI.L("把一只手伸到正面这排木牌前", "Bring one hand to the signs in front")
                                    : SBUI.L("伸出食指，戳一下木牌", "Point and poke a sign");
            var h = Chip(content, new Vector3(0.07f, -0.127f, -0.02f), hint, 0.0145f, SBUI.Cream, SBUI.Ink, 2f);
            Pop(h, 1.1f, Vector3.zero, 1.6f, 0.25f);
            Credit(new Vector3(-0.15f, CreditY, -0.012f), -0.124f);
        }

        // ───────── 出品署名 ─────────
        const float CreditY = -0.167f;   // 比右边那条提示签（英文时很长，左端会伸到海报下面）再低一截：两张纸上下错开，谁也不压谁
        /// <summary>公司署名：海报底边下面吊着的一张小纸签（和这一页别的纸条同一种撕边纸、同一种吊线），标志在前、文字在后。
        /// 标志是官方 TRIPO 横版原图（黑字版，用在浅色纸上），原样贴上去：不改色、不拉伸（宽高比 4:1）、不加效果；图四周自带的留白保留。
        /// 文字用这一页的正文字体：中文“Tripo XR 出品”，英文“Tripo XR Developers”，跟着语言切换。比游戏名小得多，在海报正下方居中。
        /// topY = 吊线往上系到哪（海报底边）。</summary>
        void Credit(Vector3 pos, float topY)
        {
            var logo = T("tripo_logo"); if (!logo) return;
            string s = SBUI.L("Tripo XR 出品", "Tripo XR Developers");
            float fs = SBUI.En ? 0.0102f : 0.0118f, lh = 0.017f, lw = lh * logo.width / logo.height;      // 标志高 1.7 厘米（图四周自带留白，字标本身约占一半高，和后面的字差不多高）
            float tw = SBUI.RealWidth(s, fs, SBUI.F.Body), gap = 0.004f, padL = 0.006f, padR = 0.011f;       // 标志图左右自带约 6% 留白：左边距少给一点，两头看起来一样宽
            float w = padL + lw + gap + tw + padR, hh = 0.013f, pad = padL;
            var tag = Sheet(content, pos, w, hh * 2f, 1.2f, 53);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = "TRIPO 标志";
            q.transform.SetParent(tag, false); q.transform.localScale = new Vector3(lw, lh, 1); q.transform.localPosition = new Vector3(-w * 0.5f + pad + lw * 0.5f, 0, -0.0006f);
            q.GetComponent<Renderer>().material = SBUI.Tex(logo);
            Txt(tag, new Vector3(-w * 0.5f + pad + lw + gap, 0, -0.001f), s, fs, SBUI.Ink);
            Drop(tag, hh, new[] { -w * 0.3f, w * 0.3f }, 1.0f, 0.5f, topY: topY);
        }

        /// <summary>设置页：大字“设置”+ 一张说明纸条（每块牌子是干什么的）。</summary>
        public void ShowSettings(int page = 0)
        {
            Clear();
            string t = SBUI.L("设置", "SETTINGS"); float fs = SBUI.En ? 0.045f : 0.07f, w = LettersW(t, fs, 0.27f);
            var nm = SBUI.Letters(content, new Vector3(-0.1f, 0.075f, -0.02f), t, fs, SBUI.Hot, 33, 0.1f, 0.06f, 7f, SBUI.En, maxW: 0.27f);
            Drop(nm, fs * 0.65f, Ropes(w), 0f, 1f);
            float pw = 0.35f, ph = 0.15f;
            var sh = Sheet(content, new Vector3(0.0f, -0.06f, -0.015f), pw, ph, -1f, 71);
            string lines = page == 0
                ? SBUI.L("桌面 —— 把场地放到你面前的桌面上\n摆正 —— 把场地挪回你的正前方\n黄牌 —— 场地离你多远：偏近 / 适中 / 偏远\n更多 —— 操纵手、语言、重置榜单\n返回 —— 回到标题",
                         "Table: put the arena on your real table\nCenter: move the arena back in front of you\nYellow sign: distance - near / mid / far\nMore: hand, language, reset ladder\nBack: return to the title")
                : SBUI.L("红牌 —— 用哪只手操纵：左手 / 右手\n语言 —— 中文 / English\n重置榜 —— 擂台榜名次从头开始（按住不放才生效）\n返回 —— 回到上一页",
                         "Red sign: control hand - left / right\nLanguage: 中文 / English\nReset: start the ladder over (press and hold)\nBack: previous page");
            Txt(sh, new Vector3(-pw / 2f + 0.014f, 0, -0.001f), lines, SBUI.En ? 0.0125f : 0.0145f, SBUI.Ink);
            Drop(sh, ph / 2f, new[] { -pw * 0.35f, pw * 0.35f }, 0.2f, 0.5f, topY: 0.03f);
        }

        // ───────── 练习场 ─────────
        Renderer[] pips;
        public static readonly int TutSteps = 9;
        public void ShowTutorial(int step, string titleS, string instr, int need, string note)
        {
            Clear();
            // 手势图卡（朱红框），角上贴一张“练习场”小签
            var card = SBUI.Poster(content, new Vector3(-0.15f, 0.005f, 0), 0.15f, 0.15f, T("g" + (step + 1)), SBUI.Red, 0.005f);
            card.localRotation = Quaternion.Euler(0, 0, 4f);
            Chip(card, new Vector3(-0.045f, 0.078f, -0.004f), SBUI.L("练习场", "TRAINING"), 0.013f, SBUI.Cream, SBUI.Jade, -9f, SBUI.F.Title);
            Drop(card, 0.08f, new[] { -0.05f, 0.05f }, 0f, 0.8f);
            // 第几步：金色大号数字 + 小号“/9”
            var num = Chip(content, new Vector3(-0.038f, 0.105f, -0.03f), $"{step + 1}", 0.03f, SBUI.Ink, SBUI.Gold, -7f, SBUI.F.Title, 2);
            var of = Chip(num, new Vector3(0.034f, -0.016f, -0.003f), $"/ {TutSteps}", 0.013f, SBUI.Cream, SBUI.Ink, 8f, SBUI.F.Title, 2, false);
            Drop(num, 0.025f, new[] { 0f }, 0.1f, 1.8f, 0.22f);
            // 招式名：逐字字块
            float fs = 0.046f, w = LettersW(titleS, fs, 0.3f);
            var nm = SBUI.Letters(content, new Vector3(0.09f, 0.08f, -0.035f), titleS, fs, SBUI.Hot, step * 17 + 3, 0.3f, 0.06f, 9f, maxW: 0.3f);
            Drop(nm, 0.03f, Ropes(w), 0.15f, 1.2f, 0.26f);
            // 说明：撕边纸条 + 墨字；下面一排木钉（进度）、一张小黑签（提示）
            int lines = SBUI.Lines(instr); float ts = SBUI.En ? 0.0152f : 0.0175f;
            float ph = lines * ts * 1.2f + 0.024f, pw = 0.27f;
            var sheet = Sheet(content, new Vector3(0.085f, 0.03f - ph / 2f, -0.02f), pw, ph, -1.5f, 40 + step);
            Txt(sheet, new Vector3(-pw / 2f + 0.014f, 0, -0.001f), instr, ts, SBUI.Ink);
            pips = new Renderer[need];
            for (int i = 0; i < need; i++)
            {
                var d = Diamond(sheet, new Vector3(-pw / 2f + 0.02f + i * 0.024f, -ph / 2f - 0.016f, -0.002f), 0.013f, new Color(.32f, .3f, .3f));
                pips[i] = d.GetComponent<Renderer>();
            }
            if (!string.IsNullOrEmpty(note))
                Chip(sheet, new Vector3(0.03f, -ph / 2f - 0.042f, -0.003f), note, 0.0112f, SBUI.Cream, SBUI.Ink, 2f);
            Drop(sheet, ph / 2f, new[] { -pw * 0.35f, pw * 0.35f }, 0.35f, 0.6f, 0.3f);
        }

        public void TutProgress(int done)
        {
            if (pips == null) return;
            for (int i = 0; i < pips.Length; i++)
                if (pips[i]) pips[i].material.color = i < done ? SBUI.Gold : new Color(.32f, .3f, .3f);
            if (done > 0 && done <= pips.Length && pips[done - 1]) Pop(pips[done - 1].transform, 0, Vector3.zero, 2.6f, 0.25f);
        }

        // ───────── 选角色 ─────────
        static readonly Dictionary<string, (string zh, string en)> UltDesc = new()
        {
            ["weight"] = ("高高跳起砸下去，震一圈冲击波", "Leaps up, slams down: shockwave"),
            ["scarecrow"] = ("放出一群乌鸦，一只只啄过去", "Sends crows to peck the foe"),
            ["kite"] = ("张翅飞上天，从头顶俯冲一脚", "Soars up, dive-kicks from above"),
            ["bamboo"] = ("手臂一节节伸长，隔老远一拳", "Arm shoots out, segment by segment"),
            ["iron"] = ("浑身铁壁打不动，肩膀直线撞过去", "Unbreakable shoulder charge"),
            ["tumbler"] = ("像陀螺一样转着撞，一路连打", "Spins like a top, hits again and again"),
        };

        /// <summary>六边形属性图：纸上印的三圈六边形格线 + 六条轴，属性面是一块红色色块，外圈六个小字标签。v = 六项 0..1。</summary>
        static void Radar(Transform parent, Vector3 c, float R, float[] v, string[] labels, Color fill)
        {
            var ink = SBUI.Solid(new Color(.3f, .24f, .18f)); var edge = SBUI.Solid(new Color(.45f, .07f, .05f));
            Vector3 P(int i, float r) { float a = Mathf.PI / 2f - i * Mathf.PI / 3f; return c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0); }
            void Line(Vector3 p0, Vector3 p1, float w, Material m, float z)
            {
                var d = p1 - p0; var q = Cube(parent, new Vector3((p0.x + p1.x) / 2f, (p0.y + p1.y) / 2f, z), new Vector3(d.magnitude + w * 0.5f, w, 0.0002f), m);
                q.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            // 属性面（三角扇，正反两面都画）
            var pts = new Vector3[6]; for (int i = 0; i < 6; i++) pts[i] = P(i, R * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(v[i])));
            var mesh = new Mesh(); var vs = new Vector3[7]; vs[0] = c; for (int i = 0; i < 6; i++) vs[i + 1] = pts[i];
            var tr = new List<int>(); for (int i = 0; i < 6; i++) { int p0 = 1 + i, p1 = 1 + (i + 1) % 6; tr.AddRange(new[] { 0, p0, p1, 0, p1, p0 }); }
            mesh.vertices = vs; mesh.triangles = tr.ToArray(); mesh.RecalculateBounds();
            var fg = new GameObject("属性面"); fg.transform.SetParent(parent, false); fg.transform.localPosition = new Vector3(0, 0, -0.0005f);
            fg.AddComponent<MeshFilter>().sharedMesh = mesh; fg.AddComponent<MeshRenderer>().material = SBUI.Solid(fill);
            for (int ring = 1; ring <= 3; ring++) for (int i = 0; i < 6; i++) Line(P(i, R * ring / 3f), P(i + 1, R * ring / 3f), ring == 3 ? 0.0009f : 0.0005f, ink, -0.0008f);
            for (int i = 0; i < 6; i++) Line(c, P(i, R), 0.0005f, ink, -0.0008f);
            for (int i = 0; i < 6; i++) { Line(pts[i], pts[(i + 1) % 6], 0.0011f, edge, -0.001f); Cube(parent, new Vector3(pts[i].x, pts[i].y, -0.0011f), new Vector3(0.0026f, 0.0026f, 0.0002f), edge).localRotation = Quaternion.Euler(0, 0, 45); }
            for (int i = 0; i < 6; i++) { var lp = P(i, R + (i == 0 || i == 3 ? 0.008f : 0.013f)); Txt(parent, new Vector3(lp.x, lp.y, -0.001f), labels[i], 0.0105f, SBUI.Ink, SBUI.F.Title, TextAnchor.MiddleCenter); }
        }

        /// <summary>按宽度折行：中文按字数、英文按单词。</summary>
        // 换行。规矩：标点不许出现在一行的开头（该换行时下一个字是标点，就把上一行最后一个字带下来陪它）；开括号、开引号不许留在一行的末尾。
        const string NoStart = "，。、！？：；）》」』”’…—～·％%,.!?:;)]}>";
        const string NoEnd = "（《「『“‘([{<";
        public static string Wrap(string s, int n)
        {
            var sb = new System.Text.StringBuilder(); int col = 0;
            if (SBUI.En)
            {
                // 英文按词换行；单独的标点（· - |）跟着前一个词走，不会被甩到下一行开头
                var words = new List<string>();
                foreach (var w in s.Split(' '))
                {
                    if (w.Length == 0) continue;
                    bool punct = true; foreach (var ch in w) if (char.IsLetterOrDigit(ch)) { punct = false; break; }
                    if (punct && words.Count > 0) words[^1] += " " + w; else words.Add(w);
                }
                foreach (var w in words) { if (col > 0 && col + w.Length + 1 > n) { sb.Append('\n'); col = 0; } else if (col > 0) { sb.Append(' '); col++; } sb.Append(w); col += w.Length; }
                return sb.ToString();
            }
            foreach (var ch in s)
            {
                if (ch == '\n') { sb.Append(ch); col = 0; continue; }
                if (col >= n)
                {
                    if (NoStart.IndexOf(ch) >= 0)
                    {
                        char last = sb.Length > 0 ? sb[^1] : '\n';
                        if (col >= 2 && last != '\n' && NoStart.IndexOf(last) < 0) { sb.Length--; sb.Append('\n').Append(last); col = 1; }   // 带一个字下来
                        // 连着几个标点（……）：就留在这一行，多出一个标点的宽度
                    }
                    else { sb.Append('\n'); col = 0; }
                }
                else if (col == n - 1 && NoEnd.IndexOf(ch) >= 0 && col > 0) { sb.Append('\n'); col = 0; }                       // 开括号不留在行尾
                sb.Append(ch); col++;
            }
            // 最后一行不许只剩孤零零一个字：从上一行带一个字下来陪它（带下来的那个字如果是标点，就再多带一个）
            var txt = sb.ToString(); int nl = txt.LastIndexOf('\n');
            if (nl > 2 && txt.Length - nl - 1 == 1)
            {
                int take = NoStart.IndexOf(txt[nl - 1]) >= 0 ? 2 : 1;
                if (nl - take > 0 && txt[nl - take] != '\n' && (take == 1 || txt[nl - 1 - 1] != '\n'))
                    txt = txt.Substring(0, nl - take) + "\n" + txt.Substring(nl - take, take) + txt.Substring(nl + 1);
            }
            return txt;
        }

        public void ShowSelect(SBKit k, int idx = 0, int total = 0, string oppLine = null)
        {
            Clear();
            string hs = SBUI.L("选你的木偶", "PICK YOUR PUPPET"); float hw = LettersW(hs, 0.02f, 0.2f);
            var hdr = SBUI.Letters(content, new Vector3(-0.2f + hw / 2f, 0.128f, -0.03f), hs, 0.02f, SBUI.Cool, 21, 0, 0.03f, 6f, maxW: 0.2f);
            Drop(hdr, 0.014f, Ropes(hw), 0f, 0.25f, 0.22f);   // 这一页东西多：各块只轻轻晃，互相不碰
            if (k == null) return;
            int seed = k.id.Length * 7 + k.id[0];
            var card = SBUI.Poster(content, new Vector3(-0.16f, -0.02f, 0), 0.15f, 0.225f, T("card_" + k.id), SBUI.Cream, 0.004f);
            card.localRotation = Quaternion.Euler(0, 0, -3f);
            Drop(card, 0.113f, new[] { -0.05f, 0.05f }, 0f, 0.2f, 0.3f, new Vector3(-0.06f, 0.2f, 0));
            // 名字：逐字字块 + 另一种语言的名字小签
            // 名字从 x = -0.05 往右排，后面跟一块小签；两样加起来不能超出看板右边（0.245），所以名字最多占“剩下的宽度”，小签紧跟在名字后面、不压名字
            string otherS = SBUI.En ? k.zh : k.en; float chipW = SBUI.EstWidth(otherS, 0.014f, SBUI.F.Title) * 1.1f + 0.014f * 1.1f;
            float nameMax = Mathf.Min(0.21f, 0.245f + 0.05f - chipW - 0.008f);
            string name = SBUI.En ? k.en : k.zh; float nfs = SBUI.En ? 0.034f : 0.044f, nw = LettersW(name, nfs, nameMax);
            var nm = SBUI.Letters(content, new Vector3(-0.05f + nw / 2f, 0.078f, -0.035f), name, nfs, SBUI.Hot, seed, 0.1f, 0.05f, 9f, maxW: nameMax);
            Drop(nm, 0.028f, Ropes(nw), 0.05f, 0.25f, 0.24f);
            var other = Chip(content, new Vector3(-0.05f + nw + 0.008f + chipW / 2f, 0.092f, -0.04f), otherS, 0.014f, SBUI.Ink, SBUI.Gold, 6f, SBUI.F.Title, SBUI.En ? 1 : 2);
            Pop(other, 0.4f, Vector3.zero, 2f, 0.2f);
            // 说明书纸条：左边六边形属性图，右边一句特性 + 打法风格；下面挂必杀
            float pw = 0.27f, ph = 0.14f;
            var sheet = Sheet(content, new Vector3(0.085f, -0.05f, -0.02f), pw, ph, 1.2f, seed + 5);
            float x0 = -pw / 2f + 0.013f;
            float N(float val, float lo, float hi) => Mathf.InverseLerp(lo, hi, val);
            float[] vals = { N(k.speed, 0.5f, 1.5f), N(k.power, 0.7f, 1.4f), N(k.reach * k.armLen, 0.75f, 1.65f), N(k.weight, 0.6f, 1.8f), N(k.guard, 0.6f, 1.7f), N(k.rageGain, 0.7f, 1.7f) };
            string[] labs = SBUI.En ? new[] { "SPEED", "POWER", "REACH", "SOLID", "GUARD", "RAGE" } : new[] { "速度", "力量", "臂展", "下盘", "防守", "怒气" };   // 下盘 = 站得稳不稳（被打时退多远），不是血量——六只木偶血量一样
            Radar(sheet, new Vector3(x0 + 0.068f, 0.004f, 0), 0.042f, vals, labs, SBUI.Red);
            float xr = x0 + 0.142f;
            // 长处和短处并排写：每只木偶都有突出的本事，弱点也摆在明面上
            Chip(sheet, new Vector3(xr + 0.016f, 0.055f, -0.002f), SBUI.L("长处", "GOOD"), 0.0095f, SBUI.Cream, SBUI.Jade, -3f, SBUI.F.Title, 0, false);
            Txt(sheet, new Vector3(xr, 0.043f, -0.001f), Wrap(SBUI.En ? k.blurbEn : k.blurbZh, SBUI.En ? 20 : 9), SBUI.En ? 0.0092f : 0.011f, SBUI.Ink, SBUI.F.Body, TextAnchor.UpperLeft).lineSpacing = 1.15f;
            Chip(sheet, new Vector3(xr + 0.016f, -0.003f, -0.002f), SBUI.L("短处", "BAD"), 0.0095f, SBUI.Cream, SBUI.Red, -3f, SBUI.F.Title, 0, false);
            Txt(sheet, new Vector3(xr, -0.015f, -0.001f), Wrap(SBUI.En ? k.weakEn : k.weakZh, SBUI.En ? 20 : 9), SBUI.En ? 0.0092f : 0.011f, SBUI.Ink, SBUI.F.Body, TextAnchor.UpperLeft).lineSpacing = 1.15f;
            Chip(sheet, new Vector3(0.0f, -ph / 2f, -0.004f), SBUI.L("必杀 · " + k.ultZh, "ULT · " + k.ultEn), 0.0165f, SBUI.Ink, SBUI.Gold, 3f, SBUI.F.Title);
            Chip(sheet, new Vector3(0.02f, -ph / 2f - 0.026f, -0.003f), SBUI.En ? UltDesc[k.id].en : UltDesc[k.id].zh, 0.0108f, SBUI.Cream, SBUI.Ink, -1.5f);
            Drop(sheet, ph / 2f, new[] { -pw * 0.36f, pw * 0.36f }, 0.1f, 0.18f, 0.32f);
            var pin = oppLine != null ? Chip(content, new Vector3(0.175f, 0.132f, -0.04f), oppLine, 0.013f, SBUI.Cream, SBUI.Cobalt, 4f, SBUI.F.Title)
                                      : Chip(content, new Vector3(0.2f, 0.132f, -0.04f), SBUI.L($"{idx + 1} / {total} · 戳木牌换人", $"{idx + 1} / {total} · Poke the signs"), 0.0115f, SBUI.Ink, SBUI.Cream, 5f);
            Pop(pin, 0.3f, Vector3.zero, 1.8f, 0.22f);
        }

        // ───────── 擂台榜（选对手）─────────
        /// <summary>左边一串五块名牌（光标那块居中、放大变金色，你的那块是红的），右边是光标那位的详情：名次、水平、习惯、常用木偶、交手战绩。</summary>
        public void ShowLadder(int cur)
        {
            Clear();
            string hs = SBUI.L("擂台榜", "THE LADDER"); float hw = LettersW(hs, 0.022f, 0.2f);
            var hdr = SBUI.Letters(content, new Vector3(-0.21f + hw / 2f, 0.128f, -0.03f), hs, 0.022f, SBUI.Cool, 21, 0, 0.03f, 6f, maxW: 0.2f);
            Drop(hdr, 0.015f, Ropes(hw), 0f, 0.25f, 0.22f);
            int n = SBLadder.Count;
            var col = new GameObject("名牌串").transform; col.SetParent(content, false); col.localPosition = new Vector3(-0.15f, 0.075f, -0.01f);
            int first = Mathf.Clamp(cur - 2, 1, Mathf.Max(1, n - 4));
            for (int k = 0; k < 5 && first + k <= n; k++)
            {
                int r = first + k; string id = SBLadder.IdAt(r); var p = SBPilot.Get(id); bool isMe = id == SBLadder.You, on = r == cur;
                var pr = new GameObject("名牌").transform; pr.SetParent(col, false); pr.localPosition = new Vector3(on ? 0.01f : 0f, -k * 0.039f, on ? -0.006f : 0f); pr.localScale = Vector3.one * (on ? 1.1f : 1f);
                pr.localRotation = Quaternion.Euler(0, 0, ((r * 37) % 5 - 2) * 0.7f);
                Color bg = isMe ? SBUI.Red : on ? SBUI.Gold : SBUI.Cream, fg = isMe ? SBUI.Cream : SBUI.Ink;
                SBUI.Slab(pr, new Vector3(0.003f, -0.003f, 0), 0.19f, 0.031f, 0.008f, SBUI.Ink, 0.0016f, 0.06f, r * 7 + 1);
                SBUI.Slab(pr, Vector3.zero, 0.19f, 0.031f, 0.008f, bg, 0.0008f, 0.06f, r * 7);
                Txt(pr, new Vector3(-0.088f, 0, -0.001f), r.ToString(), 0.016f, fg, SBUI.F.Title);
                Txt(pr, new Vector3(-0.058f, 0, -0.001f), isMe ? SBUI.L("你", "YOU") : SBUI.En ? p.en : p.zh, SBUI.En ? 0.0115f : 0.0155f, fg, SBUI.F.Title);
                if (SBLadder.Moved.TryGetValue(id, out int mv) && mv != 0)
                    Chip(pr, new Vector3(0.083f, 0, -0.002f), mv > 0 ? SBUI.L("升", "UP") : SBUI.L("降", "DN"), 0.0085f, SBUI.Cream, mv > 0 ? SBUI.Jade : SBUI.Ink, 0f, SBUI.F.Title, 0, false);
            }
            Drop(col, 0.016f, new[] { -0.07f, 0.07f }, 0.05f, 0.12f, 0.2f);
            // 右边：详情
            string cid = SBLadder.IdAt(cur); var cp = SBPilot.Get(cid); int seed = cid.Length * 11 + cid[0];
            string name = cp == null ? SBUI.L("你", "YOU") : SBUI.En ? cp.en : cp.zh; float nfs = SBUI.En ? 0.022f : 0.03f, nw = LettersW(name, nfs, 0.26f);
            var nm = SBUI.Letters(content, new Vector3(-0.025f + nw / 2f, 0.084f, -0.035f), name, nfs, SBUI.Hot, seed, 0.1f, 0.05f, 8f, maxW: 0.26f);
            Drop(nm, 0.02f, Ropes(nw), 0.05f, 0.25f, 0.24f);
            var rk = Chip(content, new Vector3(0.195f, 0.132f, -0.04f), cp == null ? SBUI.L($"第 {cur} 名", $"RANK {cur}") : cur == 1 ? SBUI.L("第 1 名 · 擂台之王", "RANK 1 · CHAMPION") : SBUI.L($"第 {cur} 名 · 水平 {cp.tier}/5", $"RANK {cur} · LV {cp.tier}/5"), 0.013f, SBUI.Cream, SBUI.Cobalt, 4f, SBUI.F.Title);
            Pop(rk, 0.3f, Vector3.zero, 1.8f, 0.22f);
            float pw = 0.27f, ph = 0.122f;
            var sheet = Sheet(content, new Vector3(0.11f, -0.036f, -0.02f), pw, ph, 1.2f, seed + 5);
            float x0 = -pw / 2f + 0.014f;
            if (cp == null)
                Txt(sheet, new Vector3(x0, 0.004f, -0.001f), SBUI.L("这是你自己。\n“匹配”：系统替你找对手。\n“挑战榜”：自己挑人发起挑战。", "This is you.\nMATCH: we find you a rival.\nLADDER: pick anyone to challenge."), SBUI.En ? 0.0115f : 0.0135f, SBUI.Ink, SBUI.F.Body).lineSpacing = 1.2f;
            else
            {
                var hp = cur == 1 ? cp.Champion() : cp;   // 第 1 名：写的是“擂台之王”的打法
                Txt(sheet, new Vector3(x0, 0.05f, -0.001f), Wrap(SBUI.En ? hp.habitEn : hp.habitZh, SBUI.En ? 30 : 15), SBUI.En ? 0.0115f : 0.0145f, SBUI.Ink, SBUI.F.Title, TextAnchor.UpperLeft).lineSpacing = 1.2f;
                var ks = new System.Text.StringBuilder(); foreach (var pid in cp.puppets) { if (ks.Length > 0) ks.Append(" · "); var kk = SBKit.Get(pid); ks.Append(SBUI.En ? kk.en : kk.zh); }
                Chip(sheet, new Vector3(x0 + 0.03f, -0.012f, -0.002f), SBUI.L("常用木偶", "PUPPETS"), 0.0095f, SBUI.Cream, SBUI.Cobalt, -3f, SBUI.F.Title, 0, false);
                Txt(sheet, new Vector3(x0 + 0.066f, -0.012f, -0.001f), ks.ToString(), SBUI.En ? 0.009f : 0.0128f, SBUI.Ink, SBUI.F.Body);
                var (w, l) = SBLadder.Record(cid);
                Txt(sheet, new Vector3(x0, -0.04f, -0.001f), w + l == 0 ? SBUI.L("还没交过手", "No bouts yet") : SBUI.L($"交手：你 {w} 胜 {l} 负", $"Record: {w} W - {l} L"), SBUI.En ? 0.011f : 0.0135f, SBUI.Ink, SBUI.F.Body);
                if (SBLadder.Level(cid) > 0) Chip(sheet, new Vector3(0.06f, -ph / 2f, -0.004f), SBUI.L("他记着仇：比上次更强", "Holds a grudge: stronger now"), 0.0105f, SBUI.Cream, SBUI.Red, 2f, SBUI.F.Title);
            }
            Drop(sheet, ph / 2f, new[] { -pw * 0.36f, pw * 0.36f }, 0.1f, 0.18f, 0.32f);
        }

        /// <summary>寻找对手 / 发起挑战时的等待画面：大标题 + 一块金色名牌（显示“寻找中…”或被挑战者的名字）+ 一张纸条写当前状态（已等待几秒、谁正在比赛中）。</summary>
        TextMesh matchTxt, searchTxt;
        public void ShowSearching(string title)
        {
            Clear();
            float fs = SBUI.En ? 0.03f : 0.04f, hw = LettersW(title, fs, 0.44f);
            var hdr = SBUI.Letters(content, new Vector3(0, 0.075f, -0.04f), title, fs, SBUI.Cool, 9, 0, 0.05f, 7f, maxW: 0.44f);
            Drop(hdr, 0.03f, Ropes(hw), 0f, 0.4f, 0.24f);
            var root = new GameObject("等待名牌").transform; root.SetParent(content, false); root.localPosition = new Vector3(0, -0.015f, -0.03f); root.localRotation = Quaternion.Euler(0, 0, -2f);
            SBUI.Slab(root, new Vector3(0.004f, -0.004f, 0), 0.3f, 0.056f, 0.012f, SBUI.Ink, 0.0016f, 0.05f, 3);
            SBUI.Slab(root, Vector3.zero, 0.3f, 0.056f, 0.012f, SBUI.Gold, 0.0008f, 0.05f, 2);
            matchTxt = SBUI.Text(root, "", SBUI.En ? 0.02f : 0.028f, SBUI.Ink, SBUI.F.Title, TextAnchor.MiddleCenter, new Vector3(0, 0, -0.001f));
            Drop(root, 0.028f, new[] { -0.1f, 0.1f }, 0.1f, 0.3f, 0.3f);
            var strip = Sheet(content, new Vector3(0, -0.092f, -0.02f), 0.36f, 0.034f, 1f, 71);
            searchTxt = Txt(strip, new Vector3(0, 0, -0.001f), "", SBUI.En ? 0.0115f : 0.014f, SBUI.Ink, SBUI.F.Body, TextAnchor.MiddleCenter);
            Drop(strip, 0.017f, new[] { -0.13f, 0.13f }, 0.2f, 0.2f, 0.3f);
        }
        public void SearchStatus(string plate, string status)
        {
            if (matchTxt && matchTxt.text != plate) { matchTxt.text = plate; SBUI.Refont(matchTxt); SBUI.FitOne(matchTxt); }
            if (searchTxt && searchTxt.text != status) { searchTxt.text = status; SBUI.Refont(searchTxt); SBUI.FitOne(searchTxt); }
        }

        /// <summary>找到对手了 / 对方接受了挑战：左边是他这一场带的木偶，右边是这个人——名次、水平、习惯、和你的交手战绩；最后盖一枚“对方已准备”。</summary>
        public void ShowFound(SBPilot p, int rank, SBKit kit)
        {
            Clear();
            int seed = p.id.Length * 13 + p.id[0];
            var card = SBUI.Poster(content, new Vector3(-0.165f, -0.01f, 0), 0.14f, 0.21f, T("card_" + kit.id), SBUI.Cream, 0.004f);
            card.localRotation = Quaternion.Euler(0, 0, -3f);
            Drop(card, 0.105f, new[] { -0.045f, 0.045f }, 0f, 0.2f, 0.3f, new Vector3(-0.06f, 0.2f, 0));
            var kc = Chip(card, new Vector3(0, -0.108f, -0.006f), SBUI.L("他带：" + kit.zh, "BRINGS " + kit.en), 0.013f, SBUI.Cream, SBUI.Ink, 3f, SBUI.F.Title);
            string name = SBUI.En ? p.en : p.zh; float nfs = SBUI.En ? 0.024f : 0.034f, nw = LettersW(name, nfs, 0.29f);
            var nm = SBUI.Letters(content, new Vector3(-0.06f + nw / 2f, 0.082f, -0.035f), name, nfs, SBUI.Hot, seed, 0.1f, 0.05f, 8f, maxW: 0.29f);
            Drop(nm, 0.022f, Ropes(nw), 0.05f, 0.25f, 0.24f);
            var rk = Chip(content, new Vector3(0.195f, 0.132f, -0.04f), rank == 1 ? SBUI.L("第 1 名 · 擂台之王", "RANK 1 · CHAMPION") : SBUI.L($"第 {rank} 名 · 水平 {p.tier}/5", $"RANK {rank} · LV {p.tier}/5"), 0.013f, SBUI.Cream, rank == 1 ? SBUI.Red : SBUI.Cobalt, 4f, SBUI.F.Title);
            Pop(rk, 0.3f, Vector3.zero, 1.8f, 0.22f);
            float pw = 0.27f, ph = 0.1f;
            var sheet = Sheet(content, new Vector3(0.09f, -0.03f, -0.02f), pw, ph, 1.2f, seed + 5);
            float x0 = -pw / 2f + 0.014f;
            if (rank == 1) p = p.Champion();
            Txt(sheet, new Vector3(x0, 0.038f, -0.001f), Wrap(SBUI.En ? p.habitEn : p.habitZh, SBUI.En ? 30 : 15), SBUI.En ? 0.0115f : 0.0145f, SBUI.Ink, SBUI.F.Title, TextAnchor.UpperLeft).lineSpacing = 1.2f;
            var (w, l) = SBLadder.Record(p.id);
            Txt(sheet, new Vector3(x0, -0.028f, -0.001f), w + l == 0 ? SBUI.L("第一次交手", "First meeting") : SBUI.L($"交手：你 {w} 胜 {l} 负", $"Record: {w} W - {l} L"), SBUI.En ? 0.011f : 0.0135f, SBUI.Ink, SBUI.F.Body);
            if (SBLadder.Level(p.id) > 0) Chip(sheet, new Vector3(0.07f, -0.028f, -0.003f), SBUI.L("记着仇", "GRUDGE"), 0.0105f, SBUI.Cream, SBUI.Red, 3f, SBUI.F.Title, 0, false);
            Drop(sheet, ph / 2f, new[] { -pw * 0.36f, pw * 0.36f }, 0.1f, 0.18f, 0.32f);
            var ready = Chip(content, new Vector3(0.175f, -0.108f, -0.05f), SBUI.L("对方已准备", "RIVAL READY"), 0.015f, SBUI.Cream, SBUI.Jade, -7f, SBUI.F.Title);
            Pop(ready, 0.9f, Vector3.zero, 2.6f, 0.2f);
        }

        /// <summary>精彩对局：左边三块牌子是现在约好的三场，右边是选中的那一场——谁对谁、各带哪只木偶、这一场争的是什么。</summary>
        public void ShowBouts(List<SBBout> bouts, int cur)
        {
            Clear();
            string hs = SBUI.L("精彩对局", "BIG BOUTS"); float hw = LettersW(hs, 0.022f, 0.2f);
            var hdr = SBUI.Letters(content, new Vector3(-0.21f + hw / 2f, 0.128f, -0.03f), hs, 0.022f, SBUI.Cool, 23, 0, 0.03f, 6f, maxW: 0.2f);
            Drop(hdr, 0.015f, Ropes(hw), 0f, 0.25f, 0.22f);
            var col = new GameObject("对局串").transform; col.SetParent(content, false); col.localPosition = new Vector3(-0.15f, 0.05f, -0.01f);
            for (int k = 0; k < bouts.Count; k++)
            {
                var b = bouts[k]; bool on = k == cur;
                var pr = new GameObject("对局牌").transform; pr.SetParent(col, false); pr.localPosition = new Vector3(on ? 0.01f : 0f, -k * 0.048f, on ? -0.006f : 0f); pr.localScale = Vector3.one * (on ? 1.1f : 1f);
                pr.localRotation = Quaternion.Euler(0, 0, ((k * 37 + 11) % 5 - 2) * 0.7f);
                SBUI.Slab(pr, new Vector3(0.003f, -0.003f, 0), 0.19f, 0.036f, 0.008f, SBUI.Ink, 0.0016f, 0.06f, k * 7 + 31);
                SBUI.Slab(pr, Vector3.zero, 0.19f, 0.036f, 0.008f, on ? SBUI.Gold : SBUI.Cream, 0.0008f, 0.06f, k * 7 + 30);
                Txt(pr, new Vector3(-0.08f, 0, -0.001f), SBUI.En ? b.tagEn : b.tagZh, SBUI.En ? 0.0125f : 0.017f, SBUI.Ink, SBUI.F.Title);
                if (b.title) Chip(pr, new Vector3(0.066f, 0, -0.002f), SBUI.L("金腰带", "BELT"), 0.0085f, SBUI.Cream, SBUI.Red, 0f, SBUI.F.Title, 0, false);
            }
            Drop(col, 0.018f, new[] { -0.07f, 0.07f }, 0.05f, 0.12f, 0.2f);
            // 右边：这一场的详情
            var c = bouts[cur];
            var rk = Chip(content, new Vector3(0.195f, 0.132f, -0.04f), SBUI.En ? c.tagEn : c.tagZh, 0.014f, SBUI.Cream, c.title ? SBUI.Red : SBUI.Cobalt, 4f, SBUI.F.Title);
            Pop(rk, 0.3f, Vector3.zero, 1.8f, 0.22f);
            float pw = 0.255f, ph = 0.19f;
            var sheet = Sheet(content, new Vector3(0.122f, -0.012f, -0.02f), pw, ph, 1.2f, 40 + cur * 9);
            float x0 = -pw / 2f + 0.016f;
            void Row(float y, SBPilot p, SBKit k, Color chip)
            {
                int r = SBLadder.Rank(p.id);
                Chip(sheet, new Vector3(x0 + 0.02f, y, -0.002f), SBUI.L($"第 {r} 名", $"#{r}"), 0.0095f, SBUI.Cream, chip, -3f, SBUI.F.Title, 0, false);
                Txt(sheet, new Vector3(x0 + 0.048f, y, -0.001f), SBUI.En ? p.en : p.zh, SBUI.En ? 0.012f : 0.0165f, SBUI.Ink, SBUI.F.Title);
                Txt(sheet, new Vector3(x0 + 0.048f, y - 0.019f, -0.001f), SBUI.L($"带 {k.zh} · 水平 {p.tier}/5", $"with {k.en} · LV {p.tier}/5"), SBUI.En ? 0.0095f : 0.0118f, SBUI.Ink, SBUI.F.Body);
            }
            Row(0.07f, c.a, c.ka, SBUI.Red);
            Txt(sheet, new Vector3(x0 + 0.048f, 0.028f, -0.001f), SBUI.L("对", "VS"), SBUI.En ? 0.012f : 0.014f, SBUI.Red, SBUI.F.Title);
            Row(0.004f, c.b, c.kb, SBUI.Cobalt);
            Txt(sheet, new Vector3(x0, -0.04f, -0.001f), Wrap(SBUI.En ? c.lineEn : c.lineZh, SBUI.En ? 32 : 16), SBUI.En ? 0.0105f : 0.013f, SBUI.Ink, SBUI.F.Body, TextAnchor.UpperLeft).lineSpacing = 1.2f;
            Drop(sheet, ph / 2f, new[] { -pw * 0.36f, pw * 0.36f }, 0.1f, 0.18f, 0.32f);
        }

        /// <summary>观战开场：两位操偶师和他们带的木偶，中间是这一场的名目。</summary>
        public void ShowWatchVS(SBPilot pa, SBKit ka, SBPilot pb, SBKit kb, string tag, bool title)
        {
            Clear();
            var bg = SBUI.Poster(content, new Vector3(0, 0, 0.015f), 0.5f, 0.28f, T("vs_bg"), SBUI.Ink, 0.003f);
            Drop(bg, 0.14f, new[] { -0.2f, 0.2f }, 0f, 0.25f, 0.3f);
            var a = SBUI.Poster(content, new Vector3(-0.15f, 0f, -0.02f), 0.13f, 0.195f, T("card_" + ka.id), SBUI.Cream, 0.004f);
            a.localRotation = Quaternion.Euler(0, 0, 5f);
            SBUI.Letters(a, new Vector3(0, -0.112f, -0.008f), SBUI.En ? ka.en : ka.zh, SBUI.En ? 0.019f : 0.024f, SBUI.Hot, 31, 0.6f, 0.04f, 8f, maxW: 0.125f);
            Drop(a, 0.1f, new[] { -0.045f, 0.045f }, 0.15f, 1.4f, 0.3f, new Vector3(-0.32f, 0.12f, 0));
            var b = SBUI.Poster(content, new Vector3(0.15f, 0f, -0.02f), 0.13f, 0.195f, T("card_" + kb.id), SBUI.Cream, 0.004f);
            b.localRotation = Quaternion.Euler(0, 0, -5f);
            SBUI.Letters(b, new Vector3(0, -0.112f, -0.008f), SBUI.En ? kb.en : kb.zh, SBUI.En ? 0.019f : 0.024f, SBUI.Cool, 47, 0.7f, 0.04f, 8f, maxW: 0.125f);
            Drop(b, 0.1f, new[] { -0.045f, 0.045f }, 0.25f, 1.4f, 0.3f, new Vector3(0.32f, 0.12f, 0));
            var vs = SBUI.Letters(content, new Vector3(0, 0.01f, -0.055f), "VS", 0.07f, SBUI.Gilt, 2, 0.75f, 0.12f, 13f, true);
            Drop(vs, 0.045f, new[] { 0f }, 0.6f, 3f, 0.22f);
            var fn = Chip(content, new Vector3(0f, 0.13f, -0.03f), tag, SBUI.En ? 0.012f : 0.015f, title ? SBUI.Ink : SBUI.Ink, title ? SBUI.Gold : SBUI.Cream, -3f, SBUI.F.Title);
            Pop(fn, 0.4f, Vector3.zero, 2f, 0.22f);
            string na = SBUI.L($"第 {SBLadder.Rank(pa.id)} 名 · {pa.zh}", $"#{SBLadder.Rank(pa.id)} · {pa.en}"), nb = SBUI.L($"第 {SBLadder.Rank(pb.id)} 名 · {pb.zh}", $"#{SBLadder.Rank(pb.id)} · {pb.en}");
            float Fs(string s) => Mathf.Min(SBUI.En ? 0.0115f : 0.013f, 0.15f / Mathf.Max(0.01f, LettersW(s, 1f)));   // 名字长就缩小，三块牌子不互相碰
            var wa = Chip(content, new Vector3(-0.155f, 0.13f, -0.035f), na, Fs(na), SBUI.Cream, SBUI.Red, 3f, SBUI.F.Title);
            Pop(wa, 0.6f, Vector3.zero, 2f, 0.22f);
            var wb = Chip(content, new Vector3(0.155f, 0.13f, -0.035f), nb, Fs(nb), SBUI.Cream, SBUI.Cobalt, -3f, SBUI.F.Title);
            Pop(wb, 0.75f, Vector3.zero, 2f, 0.22f);
        }

        // ───────── VS ─────────
        public void ShowVS(SBKit me, SBKit opp, int ladder, int total, SBPilot pilot = null, string note = null)
        {
            Clear();
            var bg = SBUI.Poster(content, new Vector3(0, 0, 0.015f), 0.5f, 0.28f, T("vs_bg"), SBUI.Ink, 0.003f);
            Drop(bg, 0.14f, new[] { -0.2f, 0.2f }, 0f, 0.25f, 0.3f);
            var a = SBUI.Poster(content, new Vector3(-0.15f, 0.01f, -0.02f), 0.13f, 0.195f, T("card_" + me.id), SBUI.Cream, 0.004f);
            a.localRotation = Quaternion.Euler(0, 0, 5f);
            SBUI.Letters(a, new Vector3(0, -0.112f, -0.008f), SBUI.En ? me.en : me.zh, SBUI.En ? 0.019f : 0.024f, SBUI.Hot, 31, 0.6f, 0.04f, 8f, maxW: 0.125f);
            Drop(a, 0.1f, new[] { -0.045f, 0.045f }, 0.15f, 1.4f, 0.3f, new Vector3(-0.32f, 0.12f, 0));
            var b = SBUI.Poster(content, new Vector3(0.15f, 0.01f, -0.02f), 0.13f, 0.195f, T("card_" + opp.id), SBUI.Cream, 0.004f);
            b.localRotation = Quaternion.Euler(0, 0, -5f);
            SBUI.Letters(b, new Vector3(0, -0.112f, -0.008f), SBUI.En ? opp.en : opp.zh, SBUI.En ? 0.019f : 0.024f, SBUI.Cool, 47, 0.7f, 0.04f, 8f, maxW: 0.125f);
            Drop(b, 0.1f, new[] { -0.045f, 0.045f }, 0.25f, 1.4f, 0.3f, new Vector3(0.32f, 0.12f, 0));
            var vs = SBUI.Letters(content, new Vector3(0, 0.02f, -0.055f), "VS", 0.07f, SBUI.Gilt, 2, 0.75f, 0.12f, 13f, true);
            Drop(vs, 0.045f, new[] { 0f }, 0.6f, 3f, 0.22f);
            var fn = Chip(content, new Vector3(0.0f, 0.128f, -0.03f), total > 0 ? (ladder == total - 1 ? SBUI.L("最终战", "FINAL") : SBUI.L($"第 {ladder + 1} 战", $"BOUT {ladder + 1}")) : SBUI.L("排位赛", "RANKED"), 0.017f, SBUI.Ink, total > 0 && ladder == total - 1 ? SBUI.Gold : SBUI.Cream, -4f, SBUI.F.Body);
            Pop(fn, 0.4f, Vector3.zero, 2f, 0.22f);
            if (pilot != null && pilot.zh != "")
            {
                // 对面是谁在提线：名字 + 水平；中间一张小纸条写他的习惯（给你一条可以利用的线索）
                var who = Chip(content, new Vector3(0.142f, 0.128f, -0.035f), total > 0 ? SBUI.L($"操偶师 {pilot.zh} · 水平 {pilot.tier}/5", $"{pilot.en} · LV {pilot.tier}/5") : SBUI.L($"第 {ladder} 名 · {pilot.zh}", $"#{ladder} · {pilot.en}"), 0.014f, SBUI.Cream, SBUI.Cobalt, 3f, SBUI.F.Title);
                Pop(who, 0.7f, Vector3.zero, 2f, 0.22f);
                string hb = Wrap(SBUI.En ? pilot.habitEn : pilot.habitZh, SBUI.En ? 18 : 8); int ln = SBUI.Lines(hb);
                float sw = Mathf.Clamp(SBUI.EstWidth(hb, SBUI.En ? 0.0105f : 0.012f, SBUI.F.Body) + 0.036f, 0.1f, 0.16f), shh = 0.02f + ln * 0.017f;   // 宽度跟着字走，最宽 16 厘米：两边是两张卡上的名字，不压它们
                var strip = Sheet(content, new Vector3(0, -0.105f - (ln - 2) * 0.006f, -0.045f), sw, shh, -1.5f, 61);
                Txt(strip, new Vector3(0, 0, -0.001f), hb, SBUI.En ? 0.0105f : 0.012f, SBUI.Ink, SBUI.F.Body, TextAnchor.MiddleCenter).lineSpacing = 1.15f;
                Pop(strip, 1.1f, Vector3.up * 0.02f, 1.5f, 0.25f);
            }
            if ((pilot == null || pilot.zh == "") && !string.IsNullOrEmpty(note))
            {   // 没有操偶师的场合（闯关）：只放一张说明纸条
                string hb = note.Contains("\n") ? note : Wrap(note, SBUI.En ? 18 : 8); int ln = SBUI.Lines(hb);   // 已经自己分好行的就不再折
                float sw = Mathf.Clamp(SBUI.EstWidth(hb, SBUI.En ? 0.0105f : 0.012f, SBUI.F.Body) + 0.036f, 0.1f, 0.16f), shh = 0.02f + ln * 0.017f;
                var strip = Sheet(content, new Vector3(0, -0.105f - (ln - 2) * 0.006f, -0.045f), sw, shh, -1.5f, 61);
                Txt(strip, new Vector3(0, 0, -0.001f), hb, SBUI.En ? 0.0105f : 0.012f, SBUI.Ink, SBUI.F.Body, TextAnchor.MiddleCenter).lineSpacing = 1.15f;
                Pop(strip, 1.1f, Vector3.up * 0.02f, 1.5f, 0.25f);
            }
            for (int i = 0; i < total; i++)
            {
                var c = i < ladder ? SBUI.Red : i == ladder ? SBUI.Gold : new Color(.25f, .25f, .27f);
                var d = Diamond(content, new Vector3((i - (total - 1) / 2f) * 0.022f, -0.065f, -0.05f), i == ladder ? 0.017f : 0.011f, c);
                Pop(d, 0.9f + i * 0.06f, Vector3.zero, 2.5f, 0.2f);
            }
        }

        // ───────── 一场打完 ─────────
        public void ShowResult(bool win, SBKit me, SBKit next, bool final, SBPilot nextPilot = null, string lineOverride = null, string who = null)
        {
            Clear();
            if (who != null)
            {   // 观战：赢的是谁
                var wc = Chip(content, new Vector3(0.09f, 0.13f, -0.04f), who, Mathf.Min(0.016f, 0.2f / Mathf.Max(0.01f, LettersW(who, 1f))), SBUI.Cream, SBUI.Red, 3f, SBUI.F.Title);
                Pop(wc, 0.7f, Vector3.zero, 2f, 0.22f);
            }
            var card = SBUI.Poster(content, new Vector3(-0.15f, -0.01f, 0), 0.15f, 0.225f, T("card_" + me.id), SBUI.Cream, 0.004f);
            card.localRotation = Quaternion.Euler(0, 0, win ? -3f : 11f);
            Drop(card, 0.113f, win ? new[] { -0.05f, 0.05f } : new[] { 0.05f }, 0f, win ? 0.6f : 2f);   // 输了：只剩一根线吊着，歪着晃
            string big = win ? SBUI.L("胜！", "WIN!") : SBUI.L("败", "DEFEAT");
            float fs = 0.075f, w = LettersW(big, fs, 0.3f);
            var l = SBUI.Letters(content, new Vector3(0.09f, 0.05f, -0.04f), big, fs, win ? SBUI.Hot : new[] { (SBUI.Cream, SBUI.Ink), (SBUI.Ink, SBUI.Cream) }, win ? 4 : 9, 0.3f, 0.08f, win ? 10f : 5f, maxW: 0.3f);
            Drop(l, 0.05f, Ropes(w), 0.25f, 1.5f, 0.22f);
            string line = win ? (final || next == null ? "" : (nextPilot != null && nextPilot.zh != "" ? SBUI.L("下一位：" + nextPilot.zh, "NEXT: " + nextPilot.en) : SBUI.L("下一位：" + next.zh, "NEXT UP: " + next.en))) : SBUI.L("线没断，再来一场。", "Strings still intact. Go again.");
            if (lineOverride != null) line = lineOverride;
            if (line != "")
            {
                var sh = Sheet(content, new Vector3(0.09f, -0.055f, -0.02f), 0.25f, 0.04f, -1.5f, win ? 91 : 92);
                Txt(sh, new Vector3(-0.112f, 0, -0.001f), line, 0.0175f, SBUI.Ink, SBUI.F.Title);
                Drop(sh, 0.02f, new[] { -0.09f, 0.09f }, 0.5f, 0.7f, 0.32f);
            }
            if (!win)
            {
                var h = Chip(content, new Vector3(0.1f, -0.105f, -0.035f), SBUI.L("线没断，再来一场", "Strings still intact. Go again"), 0.0125f, SBUI.Cream, SBUI.Ink, 2f);
                Pop(h, 0.8f, Vector3.zero, 1.6f, 0.22f);
            }
        }

        public void ShowChampion(SBKit me, string whoName = null, string sub = null)
        {
            // 冠军站在台中央高举金腰带（腰带两头会垂下来、很宽）：牌子上的字全部排在最上面一行，中间和两侧都空着，不和腰带、木偶叠在一起
            Clear();
            string big = SBUI.L("擂台之王", "CHAMPION"); float fs = SBUI.En ? 0.04f : 0.054f, w = LettersW(big, fs, 0.26f);
            var l = SBUI.Letters(content, new Vector3(0, 0.1f, -0.045f), big, fs, SBUI.Gilt, 12, 0.3f, 0.09f, 10f, maxW: 0.26f);   // 左边是名字小签、右边是说明纸条：中间这排大字最多 26 厘米宽
            Drop(l, 0.04f, Ropes(w), 0.2f, 0.4f, 0.24f);
            string wn = whoName ?? (SBUI.En ? me.en : me.zh);
            var who = Chip(content, new Vector3(-0.2f, 0.1f, -0.04f), wn, Mathf.Min(0.024f, 0.082f / Mathf.Max(0.01f, LettersW(wn, 1f))), SBUI.Cream, SBUI.Red, 4f, SBUI.F.Title);   // 名字长就缩小，不碰到中间的大字、不出牌子
            Pop(who, 0.8f, Vector3.zero, 2.4f, 0.22f);
            var sh = Sheet(content, new Vector3(0.2f, 0.1f, -0.02f), 0.105f, 0.05f, 1.5f, 93);
            Txt(sh, new Vector3(0, 0, -0.001f), sub ?? SBUI.L("擂台榜\n第 1 名", "Top of\nthe ladder"), 0.014f, SBUI.Ink, SBUI.F.Title, TextAnchor.MiddleCenter).lineSpacing = 1.15f;
            Drop(sh, 0.025f, new[] { -0.035f, 0.035f }, 0.6f, 0.3f);
        }

        /// <summary>“放到桌上”：一张手势卡 + 一张纸条说明。</summary>
        public void ShowTable()
        {
            Clear();
            var ic = SBUI.Poster(content, new Vector3(-0.15f, -0.01f, 0), 0.15f, 0.15f, T("g1"), SBUI.Red, 0.005f);
            ic.localRotation = Quaternion.Euler(0, 0, 3f);
            Drop(ic, 0.08f, new[] { -0.05f, 0.05f }, 0f, 0.8f);
            string t = SBUI.L("放到桌上", "ON THE TABLE"); float w = LettersW(t, 0.04f, 0.3f);
            var nm = SBUI.Letters(content, new Vector3(0.09f, 0.08f, -0.035f), t, 0.04f, SBUI.Hot, 61, 0.2f, 0.06f, 8f, maxW: 0.3f);
            Drop(nm, 0.028f, Ropes(w), 0.1f, 1.2f, 0.26f);
            var sh = Sheet(content, new Vector3(0.085f, -0.02f, -0.02f), 0.27f, 0.08f, -1.5f, 62);
            Txt(sh, new Vector3(-0.12f, 0, -0.001f), SBUI.L("把一只手掌平放在桌面上，\n停住不动，一秒钟就放好。", "Lay one palm flat on your table\nand hold still for a second."), SBUI.En ? 0.015f : 0.0175f, SBUI.Ink);
            Drop(sh, 0.04f, new[] { -0.09f, 0.09f }, 0.3f, 0.6f);
        }

        // ───────── 场中大字（一个字一块碎纸，一块块砸过来） ─────────
        public void Slam(string s, Color fg, Color bg, float size = 0.04f, float dur = 1.2f, float tilt = -5f, float y = 0.17f)
        {
            if (slam) Destroy(slam.gameObject);
            var root = new GameObject("大字").transform; root.SetParent(arena, false); root.localPosition = new Vector3(0, y, 0);
            billboards.Add(root);
            var inner = new GameObject("斜").transform; inner.SetParent(root, false); inner.localRotation = Quaternion.Euler(0, 0, tilt);
            var alt = bg == SBUI.Ink ? (SBUI.Ink, SBUI.Cream) : (SBUI.Cream, SBUI.Ink);
            var pairs = new[] { (fg, bg), (fg, bg), alt };
            bool latin = true; foreach (var ch in s) if (ch > 0x2E80) latin = false;
            SBUI.Letters(inner, Vector3.zero, s, size, pairs, s.Length * 5 + (int)(size * 1000), 0f, 0.055f, 10f, latin);
            inner.gameObject.AddComponent<SBSway>().amp = 2f;
            slam = root; slamT = dur;
            var d = root.position - head.position; d.y = 0; if (d.sqrMagnitude > 1e-5f) root.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>必杀就绪的提示条（挂在大屏下面）。</summary>
        public void UltHint(bool on, bool firstTimes)
        {
            if (on && !ultHint)
            {
                ultHint = new GameObject("必杀提示").transform; ultHint.SetParent(arena, false); ultHint.localPosition = new Vector3(0, 0.272f, 0);
                billboards.Add(ultHint);
                string s = firstTimes ? SBUI.L("必杀就绪！捏住，往下一拽", "ULT READY! Pinch, then yank down")
                                      : SBUI.L("必杀就绪！", "ULT READY!");
                var t = Chip(ultHint, Vector3.zero, s, 0.015f, SBUI.Ink, SBUI.Gold, -3f);
                Pop(t, 0f, Vector3.zero, 2f, 0.2f).wobble = 2.5f;
            }
            else if (!on && ultHint) { Destroy(ultHint.gameObject); ultHint = null; }
        }

        /// <summary>第一场比赛的招式小抄：大屏左边吊一串 5 张迷你手势卡（左拳 / 右拳 / 踢 / 按住蓄力 / 防守）。</summary>
        Transform cheat;
        /// <summary>招式小抄：一排小手势卡。只在暂停时出现，挂在记分屏的位置（记分屏这时收起来了），对战中不显示。</summary>
        // 对战中的玩法小抄：记分屏正下方挂一排小牌（手势图 + 名字），打着打着抬眼就能看到怎么出招。
        // 可以收起：戳“提示”木牌开 / 关，开关状态存在本机。位置在记分屏下沿（0.278）和木偶头顶 / 操偶手之间的空当里，不挡擂台
        Transform tipRow;
        public void FightTips(bool on)
        {
            if (on && !tipRow)
            {
                tipRow = new GameObject("玩法小抄").transform; tipRow.SetParent(arena, false); tipRow.localPosition = new Vector3(0, 0.248f, 0.004f);
                billboards.Add(tipRow);
                var inner = new GameObject("串").transform; inner.SetParent(tipRow, false);
                (int g, string zh, string en)[] cards = { (2, "左拳", "L JAB"), (3, "右拳", "R JAB"), (5, "踢", "KICK"), (4, "按住重招", "HOLD"), (6, "握拳防守", "GUARD"), (7, "手沉=蹲", "DUCK"), (8, "手提=跳", "JUMP") };
                const float sp = 0.041f, cw = 0.026f;
                for (int i = 0; i < cards.Length; i++)
                {
                    float x = (i - (cards.Length - 1) / 2f) * sp;
                    var c = SBUI.Poster(inner, new Vector3(x, 0, 0), cw, cw, T("g" + cards[i].g), SBUI.Cream, 0.0012f);
                    Chip(c, new Vector3(0.001f, -cw * 0.5f - 0.0062f, -0.002f), SBUI.L(cards[i].zh, cards[i].en), 0.0072f, SBUI.Cream, SBUI.Ink, i % 2 == 0 ? -3f : 2f, SBUI.F.Title, 0, false);
                    c.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 1 : -1) * 2f);
                    var r = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(r.GetComponent<Collider>());
                    r.transform.SetParent(inner, false); r.transform.localPosition = new Vector3(x, cw * 0.5f + 0.007f, 0.002f); r.transform.localScale = new Vector3(0.0007f, 0.014f, 0.0007f);
                    r.GetComponent<Renderer>().material = rope;
                }
                Pop(inner, 0f, Vector3.up * 0.03f, 1f, 0.35f);
            }
            else if (!on && tipRow) { Destroy(tipRow.gameObject); tipRow = null; }
        }

        public void CheatSheet(bool on)
        {
            if (on && !cheat)
            {
                cheat = new GameObject("招式小抄").transform; cheat.SetParent(arena, false); cheat.localPosition = new Vector3(0, 0.33f, 0.003f);   // 暂停时记分屏收起，提示卡挂在它原来的位置
                billboards.Add(cheat);
                var inner = new GameObject("串").transform; inner.SetParent(cheat, false);
                (int g, string zh, string en)[] cards = { (2, "左拳", "L JAB"), (3, "右拳", "R JAB"), (5, "踢", "KICK"), (4, "按住=重招", "HOLD=HEAVY"), (6, "防守", "GUARD"), (7, "蹲→反击", "DUCK→HIT"), (8, "跳→反击", "JUMP→HIT") };
                const float sp = 0.052f, cw = 0.04f;
                for (int i = 0; i <= cards.Length; i++)
                {
                    float x = (i - cards.Length / 2f) * sp;
                    Transform c;
                    if (i < cards.Length)
                    {
                        c = SBUI.Poster(inner, new Vector3(x, 0, 0), cw, cw, T("g" + cards[i].g), SBUI.Cream, 0.0016f);
                        Chip(c, new Vector3(0.002f, -cw * 0.56f, -0.003f), SBUI.L(cards[i].zh, cards[i].en), 0.0095f, SBUI.Cream, SBUI.Red, i % 2 == 0 ? -4f : 3f, SBUI.F.Title);
                    }
                    else c = Chip(inner, new Vector3(x + 0.006f, -0.004f, 0), SBUI.L("顶网蓄力\n松手弹射", "PRESS NET\nRELEASE"), 0.0085f, SBUI.Ink, SBUI.Gold, 4f, SBUI.F.Title);   // 弹网没有手势图，用一张小签
                    c.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 1 : -1) * 3f);
                    var r = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(r.GetComponent<Collider>());
                    r.transform.SetParent(inner, false); r.transform.localPosition = new Vector3(x, cw * 0.5f + 0.004f, 0.002f); r.transform.localScale = new Vector3(0.0008f, 0.012f, 0.0008f);
                    r.GetComponent<Renderer>().material = rope;
                }
                Pop(inner, 0f, Vector3.up * 0.04f, 1f, 0.4f);
            }
            else if (!on && cheat) { Destroy(cheat.gameObject); cheat = null; }
        }

        /// <summary>手不见了（比赛暂停）。</summary>
        public void HandHint(bool on, bool rightHand = true)
        {
            if (on && !handHint)
            {
                string hz = rightHand ? "右手" : "左手", he = rightHand ? "RIGHT" : "LEFT";
                handHint = new GameObject("找手").transform; handHint.SetParent(arena, false); handHint.localPosition = new Vector3(0, 0.14f, -0.05f);
                billboards.Add(handHint);
                var t = Chip(handHint, Vector3.zero, SBUI.L($"暂停 · 把{hz}伸回笼子前面就行", $"PAUSED · Bring your {he} hand back in front of the cage"), 0.016f, SBUI.Cream, SBUI.Ink, -2f);
                Pop(t, 0f, Vector3.zero, 1.5f, 0.2f);
            }
            else if (!on && handHint) { Destroy(handHint.gameObject); handHint = null; }
        }

        /// <summary>玩家自己按的暂停。</summary>
        Transform pauseSign;
        public void PauseSign(bool on)
        {
            if (on && !pauseSign)
            {
                pauseSign = new GameObject("暂停牌").transform; pauseSign.SetParent(arena, false); pauseSign.localPosition = new Vector3(0, 0.235f, -0.02f);   // 在木偶头顶上方、记分屏下方的空档里，谁也不压
                billboards.Add(pauseSign);
                var big = SBUI.Letters(pauseSign, new Vector3(0, 0.02f, 0), SBUI.L("暂停", "PAUSED"), 0.036f, SBUI.Cool, 5, 0f, 0.04f, 7f);
                Pop(big, 0f, Vector3.zero, 1.5f, 0.2f);
                var t = Chip(pauseSign, new Vector3(0, -0.022f, 0), SBUI.L("歇一会儿 · 戳“继续”接着打", "Take a break · Poke RESUME to continue"), 0.013f, SBUI.Cream, SBUI.Ink, -2f);
                Pop(t, 0.1f, Vector3.zero, 1.5f, 0.2f);
            }
            else if (!on && pauseSign) { Destroy(pauseSign.gameObject); pauseSign = null; }
        }

        // ───────── 跟随小名牌 ─────────
        public void FollowTag(Transform target, string s, Color bg, float up)
        {
            var root = new GameObject("名牌").transform; root.SetParent(arena, false);
            billboards.Add(root);
            var t = Chip(root, Vector3.zero, s, 0.0145f, SBUI.Cream, bg, Random.Range(-7f, 7f), SBUI.F.Title, 0, false);
            Pop(t, Random.value * 0.25f, Vector3.up * 0.02f, 1.6f, 0.25f);
            follows.Add((root, target, up));
            root.position = target.position + Vector3.up * up;
        }
        // 木偶说话：一张小纸条跟着木偶（字幕），过一会儿自己收掉。同一只木偶同时只有一张
        class Note { public Transform t, target; public Vector3 off; public float left; }
        readonly List<Note> notes = new();
        public void Speak(Transform target, string s, Vector3 off, float dur = 2.4f)
        {
            if (!target || string.IsNullOrEmpty(s)) return;
            for (int i = notes.Count - 1; i >= 0; i--) if (notes[i].target == target) { if (notes[i].t) Destroy(notes[i].t.gameObject); notes.RemoveAt(i); }
            var root = new GameObject("台词").transform; root.SetParent(arena, false); billboards.Add(root);
            var c = Chip(root, Vector3.zero, s, SBUI.En ? 0.0105f : 0.0125f, SBUI.Ink, SBUI.Cream, Random.Range(-3f, 3f), SBUI.F.Body, 0, true);
            Pop(c, 0f, Vector3.up * 0.012f, 1.6f, 0.18f);
            root.position = target.position + off;
            notes.Add(new Note { t = root, target = target, off = off, left = dur });
        }
        /// <summary>还有台词纸条挂着（= 木偶的话还没说完）。</summary>
        public bool Speaking => notes.Count > 0;
        public void ClearSpeech() { foreach (var n in notes) if (n.t) Destroy(n.t.gameObject); notes.Clear(); }

        // 观战：近侧操偶师的“想法”小纸条，挂在他的提线木架上方，过一会儿自己收掉
        Transform thought, thoughtTarget; float thoughtUp, thoughtLeft;
        public void Thought(Transform target, string s, float up)
        {
            if (thought) { Destroy(thought.gameObject); thought = null; }
            if (string.IsNullOrEmpty(s) || !target) return;
            thought = new GameObject("想法").transform; thought.SetParent(arena, false); billboards.Add(thought);
            thoughtTarget = target; thoughtUp = up; thoughtLeft = 1.8f;
            var t = Chip(thought, Vector3.zero, s, 0.012f, SBUI.Ink, SBUI.Cream, Random.Range(-4f, 4f), SBUI.F.Body, 0, false);
            Pop(t, 0f, Vector3.up * 0.012f, 1.6f, 0.18f);
            thought.position = target.position + Vector3.up * up;
        }
        public void ClearFollow()
        {
            if (thought) { Destroy(thought.gameObject); thought = null; }
            foreach (var f in follows) if (f.t) Destroy(f.t.gameObject);
            follows.Clear();
        }
        public void HighlightFollow(int idx)
        {
            for (int i = 0; i < follows.Count; i++)
                if (follows[i].t) follows[i].t.localScale = Vector3.one * (i == idx ? 1.5f : 1f);
        }

        // ───────── 木牌（挂在玩家这面铁丝网外） ─────────
        class Ticket { public string id; public Transform root, card, pivot; public Renderer bg; public Color col; public float hov, hold, needHold, tilt, w, born; public bool touching, approach, pokeOnly; }
        readonly List<Ticket> tickets = new();
        Ticket hot;

        public void ClearTickets() { foreach (var t in tickets) { if (t.pivot) Destroy(t.pivot.gameObject); else if (t.root) Destroy(t.root.gameObject); } tickets.Clear(); hot = null; }

        public bool HasTicket(string id) { foreach (var t in tickets) if (t.id == id && t.root) return true; return false; }
        public void RemoveTicket(string id) { for (int i = tickets.Count - 1; i >= 0; i--) if (tickets[i].id == id) { if (tickets[i].pivot) Destroy(tickets[i].pivot.gameObject); else if (tickets[i].root) Destroy(tickets[i].root.gameObject); if (hot == tickets[i]) hot = null; tickets.RemoveAt(i); } }

        /// <summary>pokeOnly：只认指尖戳（手掌悬停 + 捏不算）——对战中的“暂停”牌用，免得出招的手路过误碰。</summary>
        public void AddTicket(string id, string label, Color bg, Color fg, float x, float w = 0.085f, float holdSec = 0f, float delay = 0f, int forceFont = 0, bool pokeOnly = false, int side = 0)
        {
            // side = 1：挂在玩家左前方那条斜边上（整块牌位绕笼子中心转 45°，牌子正对斜边外侧）；0 = 正面那条边
            Transform pivot = null;
            if (side != 0) { pivot = new GameObject("牌位").transform; pivot.SetParent(arena, false); pivot.localRotation = Quaternion.Euler(0, side * 45f, 0); }
            var root = new GameObject("木牌 " + id).transform; root.SetParent(pivot ? pivot : arena, false);
            // 护栏在 x 处的中线位置（正面那条边是直的，超出后接 45° 的斜边），牌子跟着护栏走
            float apo = 0.27f * Mathf.Cos(Mathf.PI / 8f), edge = apo * Mathf.Tan(Mathf.PI / 8f);
            float ZR(float xx) => -apo + Mathf.Max(0f, Mathf.Abs(xx) - edge);
            const float railCy = 0.0895f, railHw = 0.0088f, railTop = 0.098f, rootY = 0.068f;
            root.localPosition = new Vector3(x, rootY, ZR(x) - 0.046f);
            int seed = id.Length * 17 + id[0];
            float tilt = ((seed % 7) - 3) * 1.6f * (w < 0.045f ? 0.35f : 1f);   // 每块牌子歪得不一样（窄牌子挨得近，少歪一点，免得角碰角）
            // 两只黑色金属挂钩：牌子顶边 → 护栏外侧（一个小铜夹）→ 翻过护栏顶 → 钩到里侧
            var hookM = SBUI.Solid(new Color(.09f, .09f, .1f)); var clipM = SBUI.Solid(new Color(.85f, .64f, .3f));
            foreach (float dx in new[] { -w * 0.33f, w * 0.33f })
            {
                float zc = ZR(x + dx) - root.localPosition.z;
                var pts = new[] { new Vector3(dx, 0.0205f, 0.0143f), new Vector3(dx, railCy - rootY, zc - railHw - 0.0012f), new Vector3(dx, railTop - rootY + 0.0006f, zc - railHw * 0.55f),
                                  new Vector3(dx, railTop - rootY + 0.0013f, zc), new Vector3(dx, railTop - rootY + 0.0006f, zc + railHw * 0.55f), new Vector3(dx, railCy - rootY + 0.002f, zc + railHw + 0.0012f) };
                for (int k = 0; k < pts.Length - 1; k++)
                {
                    var r = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(r.GetComponent<Collider>());
                    r.transform.SetParent(root, false); r.transform.localPosition = (pts[k] + pts[k + 1]) / 2f;
                    r.transform.localRotation = Quaternion.LookRotation(pts[k + 1] - pts[k]); r.transform.localScale = new Vector3(0.0013f, 0.0013f, (pts[k + 1] - pts[k]).magnitude + 0.0013f);
                    r.GetComponent<Renderer>().material = hookM;
                }
                var clip = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(clip.GetComponent<Collider>());
                clip.transform.SetParent(root, false); clip.transform.localPosition = pts[1] + Vector3.back * 0.0004f; clip.transform.localScale = new Vector3(0.004f, 0.005f, 0.0012f);
                clip.GetComponent<Renderer>().material = clipM;
            }
            var card = new GameObject("牌面").transform; card.SetParent(root, false); card.localRotation = Quaternion.Euler(35, 0, tilt);
            var wood = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(wood.GetComponent<Collider>());
            wood.transform.SetParent(card, false); wood.transform.localPosition = new Vector3(0, 0, 0.004f);
            wood.transform.localScale = new Vector3(w + 0.006f, 0.048f, 0.006f); wood.GetComponent<Renderer>().material = SBUI.Solid(new Color(.2f, .13f, .08f));
            SBUI.Slab(card, new Vector3(0.003f, -0.003f, 0), w, 0.04f, Mathf.Min(0.012f, w * 0.16f), SBUI.Ink, 0.0006f, 0.07f, seed + 1);
            var bgT = SBUI.Slab(card, Vector3.zero, w, 0.04f, Mathf.Min(0.012f, w * 0.16f), bg, 0f, 0.07f, seed);
            float lfs = Mathf.Min(label.Length > 6 ? 0.014f : 0.019f, w * 0.84f / Mathf.Max(0.5f, SBUI.EstWidth(label, 1f, SBUI.F.Title)));   // 字不超出牌面
            var lt = SBUI.Text(card, label, lfs, fg, SBUI.F.Title, TextAnchor.MiddleCenter, new Vector3(0, holdSec > 0 ? 0.003f : 0, -0.0012f));
            if (forceFont == 1) SBUI.ZhFont(lt); else if (forceFont == 2) SBUI.EnFont(lt);
            SBUI.FitOne(lt);
            if (holdSec > 0) SBUI.Text(card, SBUI.L("按住不放", "press & hold"), 0.007f, fg, SBUI.F.Body, TextAnchor.MiddleCenter, new Vector3(0, -0.012f, -0.0012f));
            Pop(card, delay, new Vector3(0, 0.05f, 0), 1.4f, 0.3f);
            tickets.Add(new Ticket { id = id, root = root, card = card, bg = bgT.GetComponent<Renderer>(), col = bg, needHold = holdSec, tilt = tilt, w = w, born = Time.unscaledTime, pokeOnly = pokeOnly, pivot = pivot });
        }

        Vector3 palm; bool palmOn, pinch, pinchDown; string picked;
        readonly List<Vector3> tips = new();

        /// <summary>每帧喂手的数据（tipL / tipR = 两只手的食指尖，可以直接戳木牌）；返回上一帧被选中的牌子 id（没有就 null）。</summary>
        public string Feed(SBHands.Hand h, bool on, bool isPinch, bool isPinchDown, SBHands.Hand left = null, SBHands.Hand right = null)
        {
            palmOn = on; if (on) palm = h.palm; pinch = isPinch; pinchDown = isPinchDown;
            tips.Clear();
            if (left != null && left.tracked) tips.Add(left.tips[1]);
            if (right != null && right.tracked) tips.Add(right.tips[1]);
            var p = picked; picked = null; return p;
        }

        void TickTickets(float dt)
        {
            Ticket best = null; float bd = 0.055f;
            // 食指尖直接戳：指尖进到牌面前后几毫米、在牌子范围里 = 按下；靠近 4 厘米内 = 高亮
            Ticket poke = null; float pokeZ = 9f;
            foreach (var t in tickets)
            {
                if (!t.root) continue;
                // 只有“从正面戳”才算：指尖先停在牌子正前方（牌面范围内、离牌面 0.7~4.5 厘米），再往里戳穿牌面；
                // 从上方伸手越过护栏时碰到牌子不算；牌子出现后 1 秒内不响应
                bool touch = false, front = false;
                bool armed = Time.unscaledTime - t.born > 1.0f;
                foreach (var tip in tips)
                {
                    var lp = t.card.InverseTransformPoint(tip);
                    if (Mathf.Abs(lp.x) > t.w * (t.w < 0.045f ? 0.5f : 0.58f) || Mathf.Abs(lp.y) > 0.03f) continue;
                    if (lp.z <= -0.007f && lp.z > -0.045f) front = true;
                    if (lp.z > -0.007f && lp.z < 0.02f && t.approach && armed) touch = true;
                    if (lp.z > -0.045f && lp.z < 0.02f && Mathf.Abs(lp.z) < Mathf.Abs(pokeZ)) { pokeZ = lp.z; poke = t; }
                }
                if (front) t.approach = true;
                else if (!touch) t.approach = false;
                if (touch && !t.touching && t.needHold <= 0) { picked = t.id; SBFx.Ui(t.root.position, true); }
                if (touch && t.needHold > 0) { t.hold += dt; if (t.hold >= t.needHold) { picked = t.id; t.hold = 0; SBFx.Ui(t.root.position, true); } }
                t.touching = touch;
            }
            if (poke != null) { best = poke; bd = 0; }
            if (palmOn && poke == null)
                foreach (var t in tickets)
                {
                    if (!t.root || t.pokeOnly) continue;
                    var d = t.root.position - palm; d.y = 0;
                    if (d.magnitude < bd && palm.y > t.root.position.y - 0.03f) { bd = d.magnitude; best = t; }
                }
            if (best != hot) { hot = best; if (best != null) SBFx.Ui(best.root.position, false); }
            foreach (var t in tickets)
            {
                if (!t.root) continue;
                bool on = t == hot;
                t.hov = Mathf.MoveTowards(t.hov, on ? 1f : 0f, dt * 8f);
                t.root.localScale = Vector3.one;
                t.card.localRotation = Quaternion.Euler(35 - 9f * t.hov, 0, t.tilt * (1f - t.hov) + Mathf.Sin(Time.unscaledTime * 9f) * 1.2f * t.hov);
                var pop = t.card.GetComponent<SBPop>();
                if (!pop || !pop.enabled) t.card.localPosition = new Vector3(0, 0.003f * t.hov, -0.005f * t.hov);   // 朝你这边抬起半厘米：离护栏更远，碰不到旁边的牌子
                if (on && t.needHold > 0 && pinch) t.hold += dt;
                else if (!t.touching) t.hold = 0;
                float hk = t.needHold > 0 ? Mathf.Clamp01(t.hold / t.needHold) : 0f;                                   // 要按住的牌子：按得越久越亮
                t.bg.material.color = Color.Lerp(Color.Lerp(t.col, SBUI.Gold, t.hov * 0.85f), Color.white, hk * 0.8f);
            }
            if (hot != null && ((hot.needHold <= 0 && pinchDown) || (hot.needHold > 0 && hot.hold >= hot.needHold)))
            {
                picked = hot.id; hot.hold = 0;
                SBFx.Ui(hot.root.position, true);
            }
        }

        /// <summary>编辑器自动测试用：直接按下某张牌。</summary>
        public void Press(string id) { picked = id; }

        // ───────── 彩纸 ─────────
        class Bit { public Transform t; public Vector3 v, w; public float life; }
        readonly List<Bit> confetti = new();
        public void Confetti(int n = 90)
        {
            Color[] cols = { SBUI.Red, SBUI.Gold, SBUI.Cream, SBUI.Jade, SBUI.Cobalt };
            for (int i = 0; i < n; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(arena, false);
                q.transform.localPosition = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.3f, 0.45f), Random.Range(-0.2f, 0.3f));
                q.transform.localScale = new Vector3(0.008f, 0.012f, 1);
                q.GetComponent<Renderer>().material = SBUI.Solid(cols[Random.Range(0, cols.Length)]);
                confetti.Add(new Bit { t = q.transform, v = new Vector3(Random.Range(-0.03f, 0.03f), -Random.Range(0.04f, 0.09f), Random.Range(-0.03f, 0.03f)), w = Random.insideUnitSphere * 500f, life = 5f });
            }
        }
        void TickConfetti(float dt)
        {
            for (int i = confetti.Count - 1; i >= 0; i--)
            {
                var b = confetti[i]; b.life -= dt;
                if (b.life <= 0 || !b.t) { if (b.t) Destroy(b.t.gameObject); confetti.RemoveAt(i); continue; }
                b.t.position += (b.v + new Vector3(Mathf.Sin(Time.time * 3f + i) * 0.02f, 0, 0)) * dt;
                b.t.Rotate(b.w * dt, Space.Self);
                if (b.t.localPosition.y < 0.002f) { b.v = Vector3.zero; b.w = Vector3.zero; }
            }
        }
    }
}
