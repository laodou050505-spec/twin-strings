using UnityEngine;

namespace StringBrawl
{
    /// <summary>观战时被关注的那位操偶师的手：一只木头做的操偶手（像画室里的木手模：上了清漆的浅色木头、每个关节一颗铜珠、指尖各一个系线的小铜环），
    /// 五根手指各牵一根线。它悬在自己木偶的身后上方（和你自己玩的时候手在的位置一样），整只手都在吊着的对战屏下沿以下，不会和对战屏碰到。
    /// 手指的动作和玩家自己玩时的手势一样：拇指按下 = 左拳，食指按下 = 右拳，后三指 = 踢；按住不放 = 蓄力，松开 = 出招；
    /// 五指握拳 = 防守；拇指食指捏住往下一拽 = 必杀；手往下一沉 = 蹲，往上一提 = 跳。看着这只手，就知道他是怎么打出这一招的。</summary>
    public class SBPilotHand : MonoBehaviour
    {
        public readonly Transform[] tips = new Transform[5];
        /// <summary>名牌 / 想法纸条挂的位置：手的两侧（按观众看过去的左右），和手一样高。</summary>
        public Transform nameAt, thoughtAt;
        readonly Transform[][] seg = new Transform[5][];
        readonly Quaternion[] rest = new Quaternion[5];
        readonly float[] curl = new float[5], want = new float[5];
        float dip, ultT, back = Back0; bool snap = true;
        // 对战屏吊在擂台正中，下沿离台面 0.278 米：手掌中心定在 0.236（腕口铜箍最高到 0.256，往上一提也只到 0.268），整只手永远在屏的下沿以下
        public const float Height = 0.236f;
        const float Clear = 0.15f;             // 让开时手掌中心离那一处的水平距离（指尖伸直 0.12 + 余量）
        const float MaxR = 0.33f, NearMax = 0.08f;
        const float Back0 = 0.15f;             // 手在木偶身后多远：指尖伸直也够不到木偶正上方，木偶跳起来不会顶到手

        public static SBPilotHand Build(Transform parent, Material lit)
        {
            var go = new GameObject("操偶师的手"); go.transform.SetParent(parent, false);
            var h = go.AddComponent<SBPilotHand>(); var root = go.transform;
            h.nameAt = new GameObject("操偶手名牌位").transform; h.nameAt.SetParent(parent, false);
            h.thoughtAt = new GameObject("操偶手纸条位").transform; h.thoughtAt.SetParent(parent, false);
            // 木头：和木偶四肢同一张木纹（顺着手指的方向走），上清漆；深色漆木手腕；铜件
            Material Wood(float tileU, float tileV) => SBDoll.Mat(lit, new Color(.93f, .74f, .52f), "tile_wood", 0f, 0.42f, new Vector2(tileU, tileV));
            var dark = SBDoll.Mat(lit, new Color(.3f, .17f, .1f), "tile_wood", 0f, 0.6f, new Vector2(1f, 1f));
            var brass = new Material(lit) { color = new Color(1.05f, .78f, .34f) }; brass.SetFloat("_Metallic", 0.45f); brass.SetFloat("_Smoothness", 0.62f);
            var lay = Quaternion.Euler(90f, 0, 0);   // 把“竖着”的形体放倒成朝前（+z）：木纹就顺着手的长度方向
            // 手掌：扁圆的一块，掌根略窄、指根略宽（两块叠成），手心朝下
            var g = new SBGeo();
            g.Blob(new Vector3(0, 0, 0.004f), new Vector3(0.039f, 0.04f, 0.0105f), lay, 3.2f, 18, 22);
            g.Blob(new Vector3(0, -0.001f, -0.024f), new Vector3(0.033f, 0.022f, 0.0098f), lay, 2.8f, 12, 18);
            g.Make(root, "手掌", Wood(1f, 1f));
            // 腕关节：一颗大铜珠；后面一截深色漆木手腕（前粗后细，尾端收圆），腕口一道铜箍
            var wj = new SBGeo(); wj.Blob(new Vector3(0, 0, -0.045f), Vector3.one * 0.0125f, Quaternion.identity, 2f, 12, 16); wj.Make(root, "腕珠", brass);
            var w = new SBGeo(); w.Lathe(Vector3.zero, new[] { new Vector2(0f, 0f), new Vector2(0.012f, 0.001f), new Vector2(0.0165f, 0.006f), new Vector2(0.0155f, 0.03f), new Vector2(0.011f, 0.04f), new Vector2(0f, 0.042f) }, 20, 0f, false, false);
            var wg = w.Make(root, "手腕", dark); wg.transform.localRotation = Quaternion.Euler(-90f, 0, 0); wg.transform.localPosition = new Vector3(0, 0, -0.052f);
            var b = new SBGeo(); b.Lathe(Vector3.zero, new[] { new Vector2(0.0166f, 0.004f), new Vector2(0.0178f, 0.006f), new Vector2(0.0178f, 0.0095f), new Vector2(0.0166f, 0.0115f) }, 20, 0f, false, false);
            var bg = b.Make(root, "铜箍", brass); bg.transform.localRotation = wg.transform.localRotation; bg.transform.localPosition = wg.transform.localPosition;
            // 五指：指根位置、朝外撇的角度、长度、粗细（拇指在左边：这是一只右手，和玩家默认用的手一样）。拇指两节，其余三节
            var defs = new (Vector3 p, float yaw, float len, float r)[]
            {
                (new Vector3(-0.035f, -0.002f, -0.016f), -52f, 0.05f, 0.0086f),
                (new Vector3(-0.0275f, 0f, 0.041f), -6f, 0.07f, 0.0076f),
                (new Vector3(-0.0092f, 0f, 0.044f), -1f, 0.078f, 0.0079f),
                (new Vector3(0.0092f, 0f, 0.043f), 4f, 0.072f, 0.0076f),
                (new Vector3(0.027f, 0f, 0.039f), 10f, 0.057f, 0.0068f),
            };
            var woodF = Wood(1f, 1.4f);
            for (int i = 0; i < 5; i++)
            {
                var d = defs[i]; float[] ls = i == 0 ? new[] { 0.56f, 0.44f } : new[] { 0.44f, 0.31f, 0.25f };
                h.seg[i] = new Transform[ls.Length]; h.rest[i] = Quaternion.Euler(0, d.yaw, 0);
                Transform par = root; var at = d.p; float r = d.r;
                for (int k = 0; k < ls.Length; k++)
                {
                    float l = d.len * ls[k];
                    var a = new GameObject(k == 0 ? "指根" : "指节").transform; a.SetParent(par, false); a.localPosition = at; a.localRotation = k == 0 ? h.rest[i] : Quaternion.identity;
                    var s = new SBGeo(); s.Blob(new Vector3(0, 0, l * 0.5f), new Vector3(r, l * 0.5f + r * 0.2f, r), lay, 2.7f, 10, 14); s.Make(a, "木节", woodF);
                    var j = new SBGeo(); j.Blob(Vector3.zero, Vector3.one * r * 0.92f, Quaternion.identity, 2f, 8, 12); j.Make(a, "铜珠", brass);
                    h.seg[i][k] = a; par = a; at = new Vector3(0, 0, l); r *= 0.9f;
                }
                // 指尖下面一个系线的小铜环，线就拴在这里
                var t = new GameObject("指尖").transform; t.SetParent(par, false); t.localPosition = at + new Vector3(0, -r * 0.95f, -r * 0.6f);
                var ring = new SBGeo(); var pts = new System.Collections.Generic.List<Vector3>();
                for (int q = 0; q < 10; q++) { float an = q * Mathf.PI * 2f / 10f; pts.Add(new Vector3(0, Mathf.Sin(an) * 0.0024f, Mathf.Cos(an) * 0.0024f)); }
                ring.Tube(pts, 0.0006f, 5, true); ring.Make(t, "系线环", brass);
                h.tips[i] = t;
            }
            foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return h;
        }

        readonly System.Collections.Generic.List<Vector3> keys = new();
        Vector3 Push(Vector3 c, Vector3 f)
        {
            const float R = 0.075f;
            var right = new Vector3(f.z, 0, -f.x);
            for (int pass = 0; pass < 3; pass++)
                foreach (var k in keys)
                {
                    var rel = new Vector3(k.x - c.x, 0, k.z - c.z);
                    float along = Mathf.Clamp(Vector3.Dot(rel, f), -0.095f, 0.125f);
                    var perp = rel - f * along; float d = perp.magnitude;
                    if (d >= R) continue;
                    var away = d > 1e-4f ? -perp / d : (Vector3.Dot(rel, right) > 0 ? -right : right);
                    c += away * (R - d);
                }
            return c;
        }

        /// <summary>手上离某一点最近的地方有多远（按手掌、手腕、每一节手指各一个球粗略量；编辑器自查用）。</summary>
        public float Nearest(Vector3 k)
        {
            float d = Mathf.Min(Vector3.Distance(k, transform.position) - 0.03f, Vector3.Distance(k, transform.TransformPoint(0, 0, -0.07f)) - 0.02f);
            d = Mathf.Min(d, Mathf.Min(Vector3.Distance(k, transform.TransformPoint(0.02f, 0, 0.02f)), Vector3.Distance(k, transform.TransformPoint(-0.02f, 0, 0.02f))) - 0.025f);
            for (int i = 0; i < 5; i++) { foreach (var s in seg[i]) d = Mathf.Min(d, Vector3.Distance(k, s.position) - 0.012f); d = Mathf.Min(d, Vector3.Distance(k, tips[i].position) - 0.01f); }
            return d;
        }

        /// <summary>下一次 Tick 直接到位（换到另一位操偶师身上时用，不从老位置飘过去）。</summary>
        public void Snap() { snap = true; }

        /// <summary>跟着木偶走，并按木偶此刻在做什么摆出对应的手势。</summary>
        public void Tick(SBPuppet p, SBPuppet other, Vector3 arenaCenter, Vector3 headPos, float rattled, float dt)
        {
            if (!p) return;
            for (int i = 0; i < 5; i++) want[i] = 0.1f;
            var act = p.ActiveAttack;
            if (p.Ulting) { ultT += dt; want[0] = 0.8f; want[1] = 0.85f; want[2] = want[3] = want[4] = 0.3f; }           // 捏住
            else
            {
                ultT = 0;
                if (p.guard) for (int i = 0; i < 5; i++) want[i] = 1f;                                                       // 握拳 = 防守
                else if (p.Winding)
                {   // 按住哪根手指 = 准备出哪一招；松开就打出去
                    if (act == SBGestures.Act.JabL || act == SBGestures.Act.HeavyL) want[0] = 1f;
                    else if (act == SBGestures.Act.JabR || act == SBGestures.Act.Heavy) want[1] = 1f;
                    else if (SBGestures.IsKick(act)) want[2] = want[3] = want[4] = 1f;
                }
            }
            for (int i = 0; i < 5; i++)
            {
                curl[i] = snap ? want[i] : Mathf.MoveTowards(curl[i], want[i], dt * (want[i] > curl[i] ? 9f : 13f));
                float c = curl[i]; var s = seg[i];
                if (i == 0) { s[0].localRotation = rest[i] * Quaternion.Euler(c * 38f + 6f, c * 30f, 0); s[1].localRotation = Quaternion.Euler(c * 55f + 8f, 0, 0); }
                else { s[0].localRotation = rest[i] * Quaternion.Euler(c * 62f + 6f, 0, 0); s[1].localRotation = Quaternion.Euler(c * 70f + 8f, 0, 0); s[2].localRotation = Quaternion.Euler(c * 50f + 7f, 0, 0); }
            }
            // 手的高低：蹲 = 往下一沉，跳 = 往上一提，必杀 = 捏住往下一拽再回来（往上的幅度小：上面就是对战屏）
            float dw = p.Ulting ? (ultT < 0.35f ? -0.04f : 0f) : p.Crouching ? -0.028f : p.Lift > 0.01f ? 0.012f : 0f;
            dip = snap ? dw : Mathf.Lerp(dip, dw, 1f - Mathf.Exp(-dt * 14f));
            float y = arenaCenter.y + Height + dip;
            // 手在木偶身后；木偶升得很高（必杀）时再往后多让一截，木偶和手永远不碰
            float bw = Back0 + Mathf.Clamp((p.HeadTop.position.y + 0.05f - (y - 0.04f)) * 1.5f, 0f, 0.12f);
            back = snap ? bw : Mathf.Lerp(back, bw, 1f - Mathf.Exp(-dt * 8f));
            var f = p.Root.forward; f.y = 0; f = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
            var q = p.Root.position; var tp = new Vector3(q.x - f.x * back, y, q.z - f.z * back);
            // 让开两只木偶：谁的头顶 / 拳套升到了手的高度附近（高个子、跳起来、举手、长手臂伸过来、对手绕到身后），手就水平让开。
            // 手从上往下看是一条“手腕—指尖”的长条：长条两侧各留 7.5 厘米。先对目标位置让一次（提前滑开），跟随之后再对实际位置硬让一次（当帧到位，不留延迟）
            keys.Clear();
            foreach (var pp in new[] { p, other })
            {
                if (!pp) continue;
                foreach (var k in new[] { pp.HeadTop.position, pp.GlovePos(false), pp.GlovePos(true) })
                    if (k.y + 0.02f > y - 0.07f) keys.Add(k);                         // 比手的最低处（握拳时的指节）还低一截的不用管
            }
            // 手掌中心离擂台中心最多 0.33 米：手腕那头再伸出去也到不了看台背墙和两侧的旗子（最近的在 0.475 米处）
            // 朝观众这一侧最多伸出 0.08 米（手腕那头再长一截，离观众的脸也还有三十多厘米），不会凑到眼前
            var toViewer = new Vector3(headPos.x - arenaCenter.x, 0, headPos.z - arenaCenter.z); toViewer = toViewer.sqrMagnitude < 1e-6f ? Vector3.back : toViewer.normalized;
            Vector3 Keep(Vector3 c)
            {
                var o = new Vector3(c.x - arenaCenter.x, 0, c.z - arenaCenter.z);
                float near = Vector3.Dot(o, toViewer); if (near > NearMax) o -= toViewer * (near - NearMax);
                if (o.magnitude > MaxR) o = o.normalized * MaxR;
                c.x = arenaCenter.x + o.x; c.z = arenaCenter.z + o.z; return c;
            }
            tp = Push(Keep(Push(tp, f)), f);
            var np = snap ? tp : Vector3.Lerp(transform.position, tp, 1f - Mathf.Exp(-dt * 10f));
            np = Push(Keep(Push(np, f)), f); np.y = y;
            transform.position = np;
            float t = Time.time, amp = 2.5f + Mathf.Clamp01(rattled) * 3f;   // 轻轻晃；慌了晃得厉害些（幅度有上限，晃起来也碰不到对战屏）
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.9f) * amp * 0.6f + Mathf.Sin(t * 27f) * rattled * 1.5f, p.Root.eulerAngles.y, Mathf.Sin(t * 1.5f + 1f) * amp + Mathf.Sin(t * 31f) * rattled * 2f);
            // 名牌在手的右边、想法纸条在手的左边（按观众看过去的左右），和手一样高：不挡手，也在对战屏下沿以下
            var v = transform.position - headPos; v.y = 0; v = v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
            var right = Vector3.Cross(Vector3.up, v);
            Vector3 hp = transform.position, na = hp + right * 0.125f, ta = hp - right * 0.135f;
            float Rad(Vector3 c) => new Vector2(c.x - arenaCenter.x, c.z - arenaCenter.z).magnitude;
            // 手在擂台左右两边时，朝外那一侧的牌子换到朝里那一侧（排在另一块牌子外面），不伸到看台和旗子那边去
            Vector3 Pick(Vector3 a, Vector3 alt) => Rad(a) <= 0.36f || Rad(alt) >= Rad(a) ? a : alt;
            ta = Pick(ta, hp + right * 0.275f); na = Pick(na, hp - right * 0.275f);
            nameAt.position = na; thoughtAt.position = ta;
            snap = false;
        }
    }
}
