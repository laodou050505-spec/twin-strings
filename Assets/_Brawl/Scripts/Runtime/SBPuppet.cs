using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>吊线木偶 v3：所有角色同一套骨架，按 SBKit 换部件。动作做大、做夸张，同时保持“乱”：
    /// - 每个关节角度都是欠阻尼弹簧（会甩过头再弹回来）+ 一点随机抖动；
    /// - 待机：拳击架势（拳头护脸、膝盖微屈、上下颠）；走路：两腿乱迈、身子跟着晃；
    /// - 出招有三段：预备（往后拉）→ 打出去（扭腰、送肩、伸直手臂 / 腿，整个人往前冲）→ 收回；
    /// - 出招时如果对手就在前面一点点，会自动往前多冲一步（出招辅助，打得到人）；
    /// - 生命 Health 0..100：越低关节越松、越晃、越暗；到 0 = KO：提线断了一样整个人瘫下去；
    /// - 八角笼：由外面给 clamp（把位置夹回笼内）；被打飞撞到铁丝网会弹回来，还额外掉血（撞网）。
    /// 必须先 Init 再用（AddComponent 时 Awake 先跑，那时外面还没给材质）。</summary>
    public class SBPuppet : MonoBehaviour
    {
        // —— 外部设置 ——
        [System.NonSerialized] public Vector3 target;
        [System.NonSerialized] public Transform opponent;
        [System.NonSerialized] public bool guard;
        [System.NonSerialized] public float charge;
        [System.NonSerialized] public int chargeSide = 1;   // 正在蓄力的是哪边：0 左拳 1 右拳 2 腿
        [System.NonSerialized] public bool frozen;
        [System.NonSerialized] public float floorY;
        [System.NonSerialized] public System.Func<Vector3, Vector3> clamp;   // 夹回笼内（返回夹过的位置）
        [System.NonSerialized] public System.Func<Vector3, Vector3> limbClamp;   // 拳头 / 脚 / 头不许伸出笼壁（只留一点点顶网的余量）
        [System.NonSerialized] public System.Action<SBPuppet, float> onFence;  // 撞网（速度）
        [System.NonSerialized] public SBPuppet foe;                           // 对手（必杀技、身体碰撞要用）
        [System.NonSerialized] public float dmgOut = 1f, dmgIn = 1f;          // 打出去 / 挨打的伤害倍率（榜首的操偶师更狠、更经打）
        [System.NonSerialized] public bool taunt;                             // 挑衅：一只手招手、一只手叉腰（电脑对手占上风时）
        [System.NonSerialized] public bool victory;                           // 赢了：双臂高举、一下一下地跳
        [System.NonSerialized] public System.Action<SBPuppet, float, Vector3> onUltHit;   // 必杀技打中（力度、方向）

        // —— 必杀技：怒气满 100 才能放；每个角色效果不一样 ——
        public float Meter { get; private set; }
        public bool UltReady => Meter >= 100f && !KO;
        public bool Ulting { get; private set; }
        public void AddMeter(float v) { if (!Ulting) Meter = Mathf.Clamp(Meter + v, 0, 100); }
        float ultT, ultTick; bool ultHit; Vector3 ultDir, ultFrom;

        public SBKit Kit { get; private set; }
        public float H { get; private set; }
        public float Health { get; private set; } = 100f;
        public bool KO { get; private set; }
        public Transform Root { get; private set; }
        public Transform Head { get; private set; }
        public Transform FistL { get; private set; }
        public Transform FistR { get; private set; }
        public Transform WireL { get; private set; }   // 拳套手背上的系线铜扣（提线系这里）
        public Transform WireR { get; private set; }
        public Transform FootR { get; private set; }
        public Transform FootL { get; private set; }
        public Transform KneeL { get; private set; }
        public Transform KneeR { get; private set; }
        public Transform HeadTop { get; private set; }
        public float BodyR { get; private set; }         // 身子实际半宽（量出来的，打中判定用）   // 头顶的系线铜扣（提线连这里）
        public Transform Torso => torso;
        public bool Ready { get; private set; }
        public bool Crouching => crouch > 0.45f;
        public float Lift => lift;
        public bool WindingUp => phase == Ph.Wind;
        public string DebugDef => $"{phase} 硬直{Mathf.Max(0, stun):0.00} 举防{(guard ? 1 : 0)} 耐久{GuardHP:0} 破防{Mathf.Max(0, breakT):0.0}";
        public float WindLeft => phase == Ph.Wind ? Mathf.Max(0f, windDur - phT) : 0f;   // 预备还剩多久才打出去
        public bool Winding => phase == Ph.Wind;            // 正在预备（手指按着还没松）
        public bool Recovering => phase == Ph.Recover;      // 刚出完招、正在收（空当）
        public bool Busy => phase != Ph.None;
        public bool Stunned => stun > 0;
        /// <summary>预备动作做到一半收住（假动作）。</summary>
        public void CancelWind() { if (phase == Ph.Wind) phase = Ph.None; }
        public SBGestures.Act ActiveAttack => phase == Ph.Strike || phase == Ph.Wind ? attack : SBGestures.Act.None;
        public Vector3 Velocity => vel;

        Transform hips, torso, headPiv;
        readonly Transform[] sh = new Transform[2], el = new Transform[2], hp = new Transform[2], kn = new Transform[2];
        Renderer[] rends; Color[] baseCols;

        // 关节弹簧
        class S { public float x, v, t; public float Step(float dt, float k, float z) { v += ((t - x) * k - v * 2f * Mathf.Sqrt(k) * z) * dt; x += v * dt; return x; } }
        readonly S koPitch = new();   // 倒地：整个人以胯为轴往后仰倒
        readonly S twist = new(), lean = new(), side = new(), hPitch = new(), hYaw = new(), hRoll = new(), bob = new();
        readonly S[] sP = { new(), new() }, sR = { new(), new() }, eB = { new(), new() }, lP = { new(), new() }, lK = { new(), new() }, wR = { new(), new() };   // wR = 手腕转（前臂竖起来护脸时手背朝前，伸直出拳时转回来）

        Vector3 vel, lastVel;
        float vy, lift, yaw, crouch, crouchV;
        float walkPh, seed, duckT, stun, flash, hurtT, koT;
        Color flashCol = Color.white;
        enum Ph { None, Wind, Strike, Recover }
        Ph phase; float phT; SBGestures.Act attack; bool attackHit; float windDur;

        float Speed => Kit.speed;

        // ───────────── 搭模型 ─────────────
        float hipsH, armOut;   // 胯的高度；胳膊为了不贴着身子往外张开的角度（胖身子才需要）

        public void Init(SBKit kit, Material lit, float height = 0.16f)
        {
            litMat = lit;
            Kit = kit; H = height; seed = Random.value * 100f;
            float s = H; string id = kit.id; var white = Color.white;
            var st = SBDoll.Get(id); var look = Look(id);
            var brass = M(lit, new Color(.85f, .66f, .32f), 0.55f, 0.65f);
            Root = new GameObject(kit.zh).transform; Root.SetParent(transform, false);

            // ── 尺寸（以身高 H 为单位）：四肢粗细、各节长度、关节球大小 ──
            // 关节球都比四肢细：连躯干的（肩 / 髋）大一号，四肢中间的（肘 / 膝 / 踝）小一号
            float armW = s * kit.limb * 0.95f, legW = armW * 1.1f;   // 四肢细长一些（像美术人偶），关节球跟着按比例变小
            float ua = s * 0.17f * kit.armLen, fa = s * 0.16f * kit.armLen, th = s * 0.22f * kit.legLen, sn = s * 0.21f * kit.legLen;
            armSpan = ua + fa; legSpan = th + sn;
            float rS = armW * 0.46f, rE = armW * 0.34f, rH = legW * 0.48f, rK = legW * 0.35f, rA = legW * 0.29f;   // 大关节（肩 / 髋）> 小关节（肘 / 膝 / 踝），都比四肢细
            // 脚：先按脚 / 鞋模型算好大小，让踝球正好坐在鞋口（脚背）正中，再由它定出脚踝离地多高
            float ankleY = legW * 1.2f, footSc = 0f, footSeatY = 0f; Vector3 footSz = Vector3.zero, footSeat = Vector3.zero;
            var fp = SBParts.Get(id + "_foot");
            if (fp != null)
            {
                footSz = fp.lo.bounds.size;
                footSc = Mathf.Min(legW * 2.7f / Mathf.Max(footSz.z, 1e-5f), legW * 1.7f / Mathf.Max(footSz.x, 1e-5f));
                // 接触面中心：鞋 = 鞋口中心，踝球沉进鞋口 0.35 个半径；光脚 = 脚踝截面中心，踝球坐在截面上（压进 0.4 个半径）
                var seat = SBParts.AnkleSeat(id + "_foot");
                footSeat = seat.p; footSeatY = seat.shoe ? -rA * 0.35f : rA * 0.6f;
                ankleY = (seat.p.y - fp.lo.bounds.min.y) * footSc + footSeatY;
            }
            hipsH = ankleY + sn + th;
            hips = new GameObject("胯").transform; hips.SetParent(Root, false); hips.localPosition = new Vector3(0, hipsH, 0);
            float hipX = Mathf.Max(legW * 0.78f, s * 0.05f);

            // ── 躯干：整体身子（秤砣 / 不倒翁）或者 胯块 + 腰球 + 胸 ──
            torso = new GameObject("躯干").transform; torso.SetParent(hips, false);
            string chestName; float cBot, cH, csc;
            bool whole = st.body != null && SBParts.Has(st.body);
            if (whole)
            {
                chestName = st.body; cH = s * kit.torsoH; cBot = rH * 0.4f;
                csc = SBParts.MakeByHeight(chestName, torso, cBot, cH, lit, white, look.m, look.s, out _);
                // 髋球藏在身子底下：髋距不超过身子底部宽度
                float bw = SBParts.HalfWidthAt(chestName, 0.1f).x * csc;
                hipX = Mathf.Clamp(bw * 0.55f, legW * 0.6f, hipX * 1.4f);
            }
            else
            {
                float pw = 2f * hipX + legW * 1.05f, ph = s * 0.09f, pd = pw * 0.6f;
                float pBot = rH * 0.72f;   // 髋球大半露在胯块下面：抬腿踢的时候大腿不顶进胯块
                SBDoll.Pelvis(hips, new Vector3(0, pBot + ph * 0.5f, 0), new Vector3(pw, ph, pd), st, lit);
                float rWa = pw * 0.15f;
                var waist = Joint(hips, new Vector3(0, pBot + ph + rWa * 0.35f, 0));
                JointBall(waist, rWa, st, lit);
                torso.SetParent(waist, false);   // 扭腰、前后倾都绕腰球转
                cBot = rWa * 0.45f; cH = s * kit.torsoH;
                if (st.chest != null && SBParts.Has(st.chest))
                {
                    chestName = st.chest;
                    csc = SBParts.MakeByHeight(chestName, torso, cBot, cH, lit, white, look.m, look.s, out _);
                }
                else
                {
                    chestName = null; csc = 0f;
                    SBDoll.ProcChest(torso, cBot, cH, Mathf.Max(pw * 0.42f, s * 0.07f), st, lit);
                }
            }
            // 身子在某个高度的半宽（量模型；程序躯干就是圆筒半径）
            float procR = Mathf.Max((2f * hipX + legW * 1.05f) * 0.42f, s * 0.07f);
            float W(float yAbs) => chestName == null ? procR : SBParts.HalfWidthAt(chestName, Mathf.InverseLerp(cBot, cBot + cH, yAbs)).x * csc;
            BodyR = Mathf.Max(W(cBot + cH * 0.5f), s * 0.05f);
            float topY = cBot + cH;

            // ── 头：小颈球坐在胸顶，头压在颈球上；整体身子的角色没有单独的头 ──
            headPiv = new GameObject("头").transform; headPiv.SetParent(torso, false);
            Transform mh = null;
            if (kit.noHead && whole)
            {
                headPiv.localPosition = new Vector3(0, cBot + cH * 0.78f, 0);
                Head = Joint(headPiv, Vector3.zero);
                float top = cBot + cH * SBParts.TopAt(chestName);
                HeadTop = Bead(torso, new Vector3(0, top - s * 0.004f, 0), s * 0.02f, brass);
            }
            else
            {
                float rN = s * 0.024f;
                var neck = Joint(torso, new Vector3(0, topY - rN * 0.1f, 0));
                JointBall(neck, rN, st, lit);
                headPiv.SetParent(neck, false); headPiv.localPosition = Vector3.zero;
                string hn = id == "bamboo" && SBParts.Has("bamboo_head2") ? "bamboo_head2" : id == "iron" && SBParts.Has("iron_head2") ? "iron_head2" : id + "_head";
                float hh = s * kit.headH, hb = rN * 0.45f - hh * kit.neckSink;   // 头往颈球上压：领口宽的（稻草人）压得深，领子盖住胸口
                SBParts.MakeByHeight(hn, headPiv, hb, hh, lit, white, look.m, look.s, out mh);
                Head = mh ? mh : Joint(headPiv, new Vector3(0, hb + hh * 0.5f, 0));
                // 头顶系线铜扣：贴在头顶实际的最高点上（量模型），不悬空
                HeadTop = Bead(headPiv, new Vector3(0, hb + hh * SBParts.TopAt(hn) - s * 0.004f, 0), s * 0.02f, brass);
            }

            // ── 胳膊：大肩球贴在胸侧（球心在表面外 0.3 个半径，球的大半嵌进身子里）──
            float shY = cBot + cH * kit.shoulderY;
            float shSurf = W(shY);
            float shX = shSurf + rS * 0.3f;
            // 胳膊垂下来会不会碰到下面更宽的身子：会的话整条胳膊往外张开一点（不再用悬空的销子撑开）
            armOut = 0f;
            for (int q = 1; q <= 10; q++)
            {
                float d = (ua + fa) * q / 10f, y = shY - d; if (y < cBot) break;
                float need = W(y) + armW * 0.55f - shX;
                if (need > 0) armOut = Mathf.Max(armOut, Mathf.Asin(Mathf.Clamp01(need / d)) * Mathf.Rad2Deg);
            }
            armOut = Mathf.Min(armOut, 40f);
            for (int i = 0; i < 2; i++)
            {
                int sx = i == 0 ? -1 : 1;
                sh[i] = Joint(torso, new Vector3(sx * shX, shY, 0));
                JointBall(sh[i], rS, st, lit);
                SBDoll.Limb(sh[i], "上臂", -rS * 0.45f, -(ua - rE * 0.45f), armW, st, lit);
                el[i] = Joint(sh[i], new Vector3(0, -ua, 0));
                JointBall(el[i], rE, st, lit);
                // 拳套大小 = 攻击力：打得越重拳套越大。先把拳套尺寸算出来，前臂的手腕端要照着护腕口收细
                float gs = s * 0.11f * Mathf.Lerp(0.78f, 1.3f, Mathf.InverseLerp(0.75f, 1.35f, kit.power));
                float gh = gs * 1.2f * 1.5f, gsc = 0f, rimY = armW * 0.9f, plugR = 0f, plugTop = 0f;   // rimY = 护腕口沿在手腕上方多高
                var gp = SBParts.Get("glove2");
                if (gp != null)
                {
                    var gb0 = gp.lo.bounds; gsc = gh / Mathf.Max(gb0.size.x, Mathf.Max(gb0.size.y, gb0.size.z));
                    float hole = SBParts.CuffInner("glove2"), rf = armW * 0.92f * 0.5f;
                    // 腕榫 = 护腕洞口内径的 0.94：整截从洞口正中插进去，四周留一丝缝，不切进口沿。
                    // 胳膊特别粗而拳套特别小的（木人桩）腕榫会细得像牙签，那就把拳套放大到够用
                    if (hole * gsc * 0.94f < rf * 0.5f) { float up = rf * 0.5f / (hole * gsc * 0.94f); gs *= up; gh *= up; gsc *= up; }
                    plugR = hole * gsc * 0.94f; plugTop = armW * 0.15f + rimY + armW * 0.28f;   // 腕榫一直细到护腕口沿上方一小截，看得见一段细手腕
                }
                gloveRad = gh * 0.23f; gloveOff = Mathf.Max(0f, gh * 0.62f - rimY);   // 拳套那一团的半径、中心在手腕下面多远
                SBDoll.Limb(el[i], "前臂", -rE * 0.45f, -(fa + armW * 0.15f), armW * 0.92f, st, lit, plugR, plugTop);
                var fist = Joint(el[i], new Vector3(0, -fa, 0));
                // 拳套：生成的右手拳套（白皮，按角色染色），左手 = 右手镜像。
                // 护腕口中心（量模型：俯视看护腕那个开口的中心）对准前臂中轴，口沿越过手腕往上包住前臂一截
                Transform wire;
                if (gp != null)
                {
                    var gb = gp.lo.bounds;
                    var cuff = SBParts.AnkleSeat("glove2").p;          // 护腕口中心（模型坐标）
                    float msx = sx < 0 ? -1f : 1f;                      // 左手镜像：x 取反
                    var gc = new Vector3(-cuff.x * msx * gsc, rimY - cuff.y * gsc, -cuff.z * gsc);
                    SBParts.Make("glove2", fist, gc, gb.size * gsc, lit, kit.glove, true, 0f, 0f, 0.45f, sx < 0);
                    // 系线铜扣：手背（+z 那面）实际表面上，拳头上部
                    float yN = 0.62f; var fr = SBParts.FrontAt("glove2", yN);
                    wire = Rivet(fist, Vector3.forward, 0f, s * 0.011f, brass);
                    wire.localPosition = gc + new Vector3(fr.x * msx * gsc, (gb.min.y + gb.size.y * yN) * gsc, fr.y * gsc);
                }
                else { SBGlove.Build(fist, gs * 1.2f, armW * 0.92f * 0.5f, sx, kit.glove, lit); wire = Bead(fist, Vector3.zero, s * 0.011f, brass); }
                if (i == 0) { FistL = fist; WireL = wire; } else { FistR = fist; WireR = wire; }
            }

            // ── 腿：大髋球在胯块底下两侧（大半嵌进胯块）→ 大腿 → 小膝球 → 小腿 → 小踝球 → 脚（左脚 = 右脚镜像）──
            for (int i = 0; i < 2; i++)
            {
                int sx = i == 0 ? -1 : 1;
                hp[i] = Joint(hips, new Vector3(sx * hipX, 0, 0));
                JointBall(hp[i], rH, st, lit);
                SBDoll.Limb(hp[i], "大腿", -rH * 0.45f, -(th - rK * 0.45f), legW, st, lit);
                kn[i] = Joint(hp[i], new Vector3(0, -th, 0));
                JointBall(kn[i], rK, st, lit);
                var knee = Rivet(kn[i], Vector3.forward, rK, s * 0.012f, brass);   // 系线铜扣钉在膝球正面（垫片贴着球面）
                SBDoll.Limb(kn[i], "小腿", -rK * 0.45f, -(sn - rA * 0.45f), legW * 0.92f, st, lit);
                var foot = Joint(kn[i], new Vector3(0, -sn, 0));
                JointBall(foot, rA, st, lit);
                float fBot = -ankleY; footRad = legW * 0.7f;
                if (footSc > 0f)
                {   // 接触面中心对准踝球正下方（左脚是镜像，x 跟着翻），鞋底贴地
                    // 脚模型大多是右脚形状：右脚原样、左脚镜像；个别生成成了左脚形状（铁甲将军）就反过来，否则两只脚看着是穿反的
                    bool mir = (sx < 0) != SBParts.IsLeftFootModel(id + "_foot");
                    float px = (mir ? -footSeat.x : footSeat.x) * footSc;
                    SBParts.Make(id + "_foot", foot, new Vector3(-px, fBot + footSz.y * footSc * 0.5f, -footSeat.z * footSc), footSz * footSc, lit, white, true, 0f, look.m, look.s, mir);
                }
                else
                {   // 没有生成的脚：一只圆头木脚，正中在踝球下面
                    float fh = ankleY - rA * 0.5f;
                    var fg = new SBGeo(); fg.Blob(new Vector3(0, fBot + fh * 0.5f, 0), new Vector3(legW * 0.42f, fh * 0.5f, legW * 0.78f), Quaternion.identity, 2.6f, 10, 16);
                    fg.Make(foot, "脚", SBDoll.Mat(lit, st.tint, st.tile, st.metal, st.smooth, Vector2.one));
                }
                if (i == 0) { KneeL = knee; FootL = foot; } else { KneeR = knee; FootR = foot; }
            }
            if (id == "kite" && chestName != null) BuildPaperWings(chestName, cBot, cH, csc);
            rends = Root.GetComponentsInChildren<Renderer>();
            baseCols = new Color[rends.Length];
            gloveR = new bool[rends.Length];   // 哪些是拳套（防守耐久、反击就绪都显示在拳套上）
            for (int i = 0; i < rends.Length; i++) { var tp = rends[i].transform.parent; gloveR[i] = tp && tp.name == "glove2"; }
            BuildParts(); KoSeed();
            for (int i = 0; i < rends.Length; i++) { var bc = rends[i].material.color; baseCols[i] = new Color(bc.r * 1.15f, bc.g * 1.15f, bc.b * 1.15f, bc.a); }   // 稍微提亮：像被顶光照着
            Ready = true;
        }

        /// <summary>关节球：木球（带一道箍和两颗轴钉的生成模型），半径 r，按角色染色。</summary>
        static void JointBall(Transform p, float r, SBDoll.Style st, Material lit)
        {
            // 每个木偶自己的关节球颜色和质感（黄铜 / 红漆 / 黑铁 / 竹节黄 ……），光滑小球，中间一道细缝像转轴
            var g = new SBGeo(); g.Blob(Vector3.zero, Vector3.one * r, Quaternion.identity, 2f, 10, 16);
            g.Make(p, "关节球", M(lit, st.joint, st.jointMetal, st.jointSmooth));
            var seam = new SBGeo(); var ring = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < 20; k++) { float a = k / 20f * Mathf.PI * 2f; ring.Add(new Vector3(0, Mathf.Sin(a) * r * 1.005f, Mathf.Cos(a) * r * 1.005f)); }
            seam.Tube(ring, r * 0.06f, 4, true);
            seam.Make(p, "球缝", M(lit, st.joint * 0.55f, st.jointMetal, 0.3f));
        }

        public static Material paperMat;   // 由 SBMatch 赋值（场景里引用的材质资产，打包才会带上着色器）
        readonly Transform[] wings = new Transform[2];   // 纸鸢的两片纸翼：站着时轻轻扇，倒地时摊平贴在台面上
        void BuildPaperWings(string bodyName, float tBot, float tH, float bsc)
        {
            var tex = Resources.Load<Texture2D>("UI/kite_wing"); if (!tex || !paperMat) return;
            var mat = new Material(paperMat) { mainTexture = tex };
            float hgt = H * 0.5f, wid = hgt * tex.width / tex.height;
            var back = SBParts.HalfWidthAt(bodyName, 0.75f).y * bsc;
            var root = new GameObject("纸翼").transform; root.SetParent(torso, false);
            root.localPosition = new Vector3(0, tBot + tH * 0.98f, -back * 0.85f);   // 竹座在图的上部中间，对准背上肩胛之间
            foreach (int sx in new[] { -1, 1 })
            {
                var w = new GameObject(sx < 0 ? "左翼" : "右翼").transform; w.SetParent(root, false);
                w.localRotation = Quaternion.Euler(0, sx * 22f, 0); wings[sx < 0 ? 0 : 1] = w;
                var m = new Mesh();
                // 图的上沿 = 竹座上沿附近（图里竹座在 y≈0.28 处）；图的中线 = 翼根
                float top = hgt * 0.27f, bot = top - hgt;
                float x0 = 0f, x1 = sx * wid * 0.5f; float u0 = 0.5f, u1 = sx < 0 ? 0f : 1f;
                m.vertices = new[] { new Vector3(x0, bot, 0), new Vector3(x1, bot, 0), new Vector3(x1, top, 0), new Vector3(x0, top, 0) };
                m.uv = new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1) };
                m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                m.triangles = sx < 0 ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
                m.RecalculateBounds();
                w.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
                var r = w.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static Material M(Material lit, Color c) { var m = new Material(lit) { color = c }; return m; }

        /// <summary>木偶球关节：一颗木球 + 一圈竖着套上的铜箍 + 左右两颗销钉（转轴）。</summary>
        static void Ball(Transform p, float d, Material wood, Material band, Material lit, float jm)
        {
            // 有生成的雕花球关节就用（木关节：胡桃木 + 铜铰链；金属关节：錾花黄铜 + 铁铰链）
            bool brassJ = jm > 0.3f;
            if (SBParts.Make(brassJ ? "joint_brass" : "joint_wood", p, Vector3.zero, Vector3.one * d * 0.8f, lit, Color.white, true, 0f, brassJ ? 0.45f : 0f, brassJ ? 0.62f : 0.55f)) return;
            d *= 0.8f;
            Part(SBKit.Shape.Sphere, p, Vector3.zero, Vector3.one * d, wood);
            Part(SBKit.Shape.Cylinder, p, Vector3.zero, new Vector3(d * 1.07f, d * 0.06f, d * 1.07f), band).transform.localRotation = Quaternion.Euler(0, 0, 90);
            foreach (int sx in new[] { -1, 1 })
                Part(SBKit.Shape.Cylinder, p, new Vector3(sx * d * 0.5f, 0, 0), new Vector3(d * 0.3f, d * 0.05f, d * 0.3f), band).transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        /// <summary>系线的铜扣：一颗小铜珠 + 一圈扁垫片，提线就系在这里。</summary>
        static Transform Bead(Transform p, Vector3 pos, float d, Material brass)
        {
            var t = Joint(p, pos);
            Part(SBKit.Shape.Sphere, t, Vector3.zero, Vector3.one * d, brass);
            Part(SBKit.Shape.Cylinder, t, new Vector3(0, -d * 0.35f, 0), new Vector3(d * 1.5f, d * 0.12f, d * 1.5f), brass);
            return t;
        }
        /// <summary>钉在球面上的系线铜扣：垫片贴着球面（法线 = 方向 dir），铜珠一半露在外面。返回铜珠的位置（提线系这里）。</summary>
        static Transform Rivet(Transform ball, Vector3 dir, float r, float d, Material brass)
        {
            var t = Joint(ball, dir.normalized * r);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            Part(SBKit.Shape.Cylinder, t, new Vector3(0, -d * 0.05f, 0), new Vector3(d * 1.25f, d * 0.08f, d * 1.25f), brass);   // 垫片：压在球面上
            Part(SBKit.Shape.Sphere, t, new Vector3(0, d * 0.2f, 0), Vector3.one * d, brass);                                      // 铜珠：根部埋进垫片
            return t;
        }
        static Material M(Material lit, Color c, float metal, float smooth) { var m = new Material(lit) { color = c }; m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); return m; }

        /// <summary>每个角色的材质感：身上部件（金属度、光滑度）+ 关节球（颜色、金属度、光滑度）。
        /// 漆器亮、青铜和铁带金属光、纸和稻草哑光；关节是上了清漆的木球或黄铜 / 乌钢球。</summary>
        static (float m, float s, Color j, float jm, float js) Look(string id) => id switch
        {
            "weight" => (0.05f, 0.42f, new Color(.78f, .58f, .3f), 0f, 0.5f),     // 设定图里是深色木球关节   // 青铜：金属度低一点，铜绿才看得出来
            "scarecrow" => (0f, 0.08f, new Color(.36f, .24f, .14f), 0f, 0.5f),
            "kite" => (0f, 0.18f, new Color(.80f, .68f, .47f), 0f, 0.45f),
            "bamboo" => (0f, 0.42f, new Color(.42f, .30f, .16f), 0f, 0.55f),
            "iron" => (0.2f, 0.52f, new Color(.3f, .3f, .33f), 0.55f, 0.68f),
            "tumbler" => (0f, 0.78f, new Color(.9f, .68f, .28f), 0f, 0.6f),      // 设定图里是本色木球关节
            _ => (0f, 0.3f, new Color(.36f, .24f, .14f), 0f, 0.5f),
        };

        /// <summary>一节四肢（上臂 / 前臂 / 大腿 / 小腿）：在关节坐标里从 y0 长到 y1（y1 &lt; y0），粗 w。
        /// 两头接关节球的一端只插进球里 0.1 个粗细（断口的棱角也在球里面）；接身子 / 拳套 / 脚的一端多伸进去一点。</summary>
        static bool Limb(string id, Transform joint, float y0, float y1, float w, Material lit)
        {
            var lk = Look(id);
            return SBParts.Make(id + "_limb", joint, new Vector3(0, (y0 + y1) * 0.5f, 0), new Vector3(w, y0 - y1, w), lit, Color.white, false, 0f, lk.m, lk.s) != null;
        }
        static Transform Joint(Transform p, Vector3 pos) { var t = new GameObject("关节").transform; t.SetParent(p, false); t.localPosition = pos; return t; }

        static GameObject Part(SBKit.Shape s, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var t = s switch { SBKit.Shape.Box => PrimitiveType.Cube, SBKit.Shape.Capsule => PrimitiveType.Capsule, SBKit.Shape.Cylinder => PrimitiveType.Cylinder, _ => PrimitiveType.Sphere };
            var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            if (t == PrimitiveType.Capsule || t == PrimitiveType.Cylinder) scale = new Vector3(scale.x, scale.y, scale.z);
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().material = m;
            return g;
        }

        // ───────────── 外部调用 ─────────────
        public void Place(Vector3 p, float faceYaw)
        {
            Root.position = p; target = p; vel = lastVel = Vector3.zero; vy = lift = 0; yaw = faceYaw;
            stun = 0; phase = Ph.None; KO = false; koT = 0; Root.rotation = Quaternion.Euler(0, yaw, 0);
            koPitch.x = koPitch.v = koPitch.t = 0;
        }
        public void ResetHealth() { Health = 100f; KO = false; EndUlt(); GuardHP = 100f; breakT = 0f; counterT = -9f; justBroke = false; dashT = 0f; dashBonusT = -9f; KoSeed(); }

        float chargeLv, revengeT, comboT; int combo;

        // —— 防守耐久：每挡一下掉一截，掉光 = 被打穿（双臂震开、站不稳、一小段时间举不起防守）；松开拳头才慢慢回 ——
        public float GuardHP { get; private set; } = 100f;
        public static Material glowMat;
        // 防守看得见：举起防守就是“双手抱头”——两只拳套并排举在脸前、顶到头顶那么高，把头护在后面，从木偶背后也看得见头两侧的拳套。
        //   拳套微微绷大一点（最多 1.2 倍，越挡越瘪回去）；挡住一下，拳套被压一下再弹回来；
        //   被打穿：拳套一下子瘪回原样，棉絮从里面崩出来。不用任何光效，都是拳套自己在变。
        float gloveK = 1f;
        public float GloveScale => gloveK;
        // 防守时两条手臂的角度（上臂前抬、外展、肘弯），按身材解出来
        readonly Vector3[] guardAng = { new(-62f, -14f, -118f), new(-62f, 14f, -118f) }; bool guardSolved, guardSolved2;
        public static float GuardGap = 0.004f;   // 拳套离脸、两只拳套之间留的空隙
        const float GuardGlove = 1.2f;           // 防守时拳套只微微绷大一点（双手挡脸，不需要很大）；越挡越瘪回去
        void SolveGuard()
        {
            if (guardSolved) guardSolved2 = true; guardSolved = true;
            if (!FistL || !FistR || !torso) return;
            float gr = gloveRad * GuardGlove;                                             // 按防守时的拳套大小算
            // 目标高度：拳套的顶和头顶差不多平
            var topL = torso.InverseTransformPoint(HeadTop.position); float yT = topL.y - gr * 0.85f;
            // 量一下这个高度上，头（连同帽子、整只身子的那种角色就是身子上半截）最靠前到哪、最宽到哪：只看躯干下面的模型，不算两条手臂
            float front = 0f, halfW = 0f; bool any = false;
            foreach (var mf in torso.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable || mf.transform.IsChildOf(sh[0]) || mf.transform.IsChildOf(sh[1])) continue;
                var vs = mf.sharedMesh.vertices; int step = Mathf.Max(1, vs.Length / 6000);
                for (int k = 0; k < vs.Length; k += step)
                {
                    var p = torso.InverseTransformPoint(mf.transform.TransformPoint(vs[k]));
                    if (p.y < yT - gr * 1.1f || p.y > yT + gr * 1.1f) continue;
                    if (Mathf.Abs(p.x) < gr * 2.2f) front = any ? Mathf.Max(front, p.z) : p.z;   // 拳套正对着的那一条里，最靠前的地方
                    halfW = Mathf.Max(halfW, Mathf.Abs(p.x)); any = true;
                }
            }
            if (!any) front = BodyR;
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f; var fist = i == 0 ? FistL : FistR;
                var T = new Vector3(sx * (gr + GuardGap * 0.5f), yT, front + gr + GuardGap + H * 0.02f);   // 这只拳套中心该到的位置（躯干坐标）
                var S = sh[i].localPosition; var U0 = el[i].localPosition;
                var F0 = el[i].InverseTransformPoint(fist.position) + Vector3.down * (gloveOff * GuardGlove);
                float aOut = (i == 0 ? -armOut : armOut);
                float best = float.MaxValue; var ba = guardAng[i];
                void Try(float sp, float z, float eb)
                {
                    var q = Quaternion.Euler(sp, 0, z + aOut); var elbow = S + q * U0; var g = elbow + q * (Quaternion.Euler(eb, 0, 0) * F0);
                    float e = (g - T).sqrMagnitude;
                    if (elbow.y > g.y) e += (elbow.y - g.y) * (elbow.y - g.y) * 4f;       // 手肘要在拳套下面（前臂竖着），不要翻到上面去
                    e += Mathf.Max(0f, Mathf.Abs(elbow.x) - Mathf.Abs(S.x) - H * 0.12f) * 0.02f;   // 手肘别往外撑得太开
                    if (e < best) { best = e; ba = new Vector3(sp, z, eb); }
                }
                for (float sp = -170f; sp <= -10f; sp += 5f) for (float eb = -155f; eb <= -15f; eb += 5f) for (float z = -60f; z <= 60f; z += 6f) Try(sp, z, eb);
                var c0 = ba;
                for (float sp = c0.x - 5f; sp <= c0.x + 5f; sp += 1f) for (float eb = c0.z - 5f; eb <= c0.z + 5f; eb += 1f) for (float z = c0.y - 6f; z <= c0.y + 6f; z += 1.5f) Try(sp, z, eb);
                guardAng[i] = ba; guardErr = Mathf.Sqrt(best);
            }
        }
        float guardErr;
        public string GuardDebug => $"上臂 {guardAng[1].x:0} 外展 {guardAng[1].y:0} 肘 {guardAng[1].z:0} 误差 {guardErr * 100f:0.0}cm";
        float guardK; Material litMat;
        public float GuardPad => guardK * H * 0.12f;   // 鼓起来的拳套挡在身前：防守时身子“厚”一点，打在拳套上也算打中（被挡）
#if UNITY_EDITOR
        /// <summary>编辑器里看盾牌用：直接设防守耐久；brk = 立刻算作被打穿。</summary>
        public void DebugGuard(float hp, bool brk) { dbgGuard = true; GuardHP = hp; if (brk) { breakT = 1.3f; justBroke = true; SBFx.Stuffing(GlovePos(false), Root.forward * -1f); SBFx.Stuffing(GlovePos(true), Root.forward * -1f); } }
#endif
        public bool GuardBroken => breakT > 0f;
        [System.NonSerialized] public bool dbgGuard;   // 编辑器里看防守用：强制举着防守
        public bool GuardUp => (guard || dbgGuard) && breakT <= 0f;
        // —— 闪躲反击：蹲 / 跳躲开对手的招之后一小段时间里，下一击必中重创（挡不住）——
        public bool CounterReady => counterT > Time.time && !KO;
        float breakT, lastBlockT = -9f, counterT = -9f; bool justBroke; bool[] gloveR;
        public void GrantCounter(float dur) { if (KO) return; counterT = Time.time + dur; }

        // —— 弹网冲刺：顶在网上蓄力，松开被弹出去；冲刺中 / 刚冲完打中的第一下加伤 ——
        float dashT, dashBonus, dashBonusT = -9f, vicT; bool dashHit;
        public bool Dashing => dashT > 0f;
        public void Dash(Vector3 dest, float dur, float bonus)
        {
            if (KO || !Ready || stun > 0) return;
            var d = dest - Root.position; d.y = 0;
            dashT = dur; target = dest; vel = d / Mathf.Max(dur, 0.05f); dashBonus = bonus; dashBonusT = Time.time + dur + 0.6f;
            lean.v += 700f; SBFx.Whoosh(Root.position, 1.2f);
        }
        public bool ConsumeDashHit() { bool b = dashHit; dashHit = false; return b; }

        // —— 出招的接触点：拳 = 拳套那一团的中心，腿 = 脚踝；判定和“不许插进对手身子”都用它 ——
        float gloveOff, gloveRad, footRad, armSpan, legSpan;
        public Vector3 StrikePoint { get; private set; }
        public Vector3 StrikeOrigin { get; private set; }   // 出招那条肢体的根：肩 / 髋（判定看“根 → 拳套 / 脚”这一整条）
        public float GloveR => gloveRad;
        public Vector3 GlovePos(bool right) => LimbPoint(right ? FistR : FistL);
        /// <summary>描边（选人时标出选中的）：给每个部件再套两层材质——先“占位”、再“描边”，只留最外一圈轮廓；传 null 去掉。</summary>
        public void SetOutline(Material mask, Material line)
        {
            if (rends == null) return;
            foreach (var r in rends)
            {
                if (!r || r.transform == wings[0] || r.transform == wings[1]) continue;
                var ms = r.sharedMaterials; bool has = ms.Length > 1;
                if (mask && line && !has) r.sharedMaterials = new[] { ms[0], mask, line }; else if (!(mask && line) && has) r.sharedMaterials = new[] { ms[0] };
            }
        }
        public float StrikeRad { get; private set; }
        Vector3 LimbPoint(Transform e) => e == FistL || e == FistR ? e.TransformPoint(0, -gloveOff, 0) : e.position;
        float LimbRad(Transform e) => e == FistL || e == FistR ? gloveRad : footRad;

        // —— 精确找最低点：每个网格取 26 个方向上最远的顶点当外壳采样点（缓存一次）。
        //    原来用包围盒，人一倾斜盒子就比实际大，倒地时会被“垫”起来悬空 ——
        static readonly Dictionary<Mesh, Vector3[]> hullCache = new();
        static Vector3[] Hull(Mesh m)
        {
            if (hullCache.TryGetValue(m, out var h)) return h;
            var list = new List<Vector3>();
            if (m.isReadable)
            {
                var vs = m.vertices;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    var dir = new Vector3(x, y, z); float best = float.MinValue; int bi = -1;
                    for (int i = 0; i < vs.Length; i++) { float dd = Vector3.Dot(vs[i], dir); if (dd > best) { best = dd; bi = i; } }
                    if (bi >= 0 && !list.Contains(vs[bi])) list.Add(vs[bi]);
                }
            }
            else { var b = m.bounds; for (int i = 0; i < 8; i++) list.Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1))); }
            h = list.ToArray(); hullCache[m] = h; return h;
        }
        struct HullPart { public Transform t; public Vector3[] pts; public int g; }   // g：0 躯干 1 头 2 / 3 左右臂 4 / 5 左右腿
        HullPart[] parts = new HullPart[0];
        readonly float[] gLow = new float[6];
        readonly float[] koArm = new float[2], koLeg = new float[2]; float koTilt;
        void BuildParts()
        {
            var list = new List<HullPart>();
            foreach (var r in rends)
            {
                var mf = r ? r.GetComponent<MeshFilter>() : null; if (!mf || !mf.sharedMesh) continue;
                var tr = r.transform;
                if (tr.name == "高" && tr.parent && tr.parent.Find("低")) continue;   // 高低两档外形一样，只算低的那份（顶点少得多，进场时不卡）
                if (tr == wings[0] || tr == wings[1]) continue;                       // 纸翼是软的，不拿来“垫”身子
                // 胳膊 / 腿只算外面那一节（前臂 + 拳套、小腿 + 脚）：它们的高低确实由肩 / 髋的角度决定；
                // 上臂、大腿根贴着身子几乎不动，算进躯干（不然根部本来就比台面低时，角度会一路调到头）
                int g = tr.IsChildOf(el[0]) ? 2 : tr.IsChildOf(el[1]) ? 3 : tr.IsChildOf(kn[0]) ? 4 : tr.IsChildOf(kn[1]) ? 5 : headPiv && tr.IsChildOf(headPiv) ? 1 : 0;
                list.Add(new HullPart { t = tr, pts = Hull(mf.sharedMesh), g = g });
            }
            parts = list.ToArray();
        }
        /// <summary>调试：各部位最低点离台面多高（毫米）、倒地时各关节补偿角。</summary>
        public string KoDebug { get { var sb = new System.Text.StringBuilder(); for (int g = 0; g < 6; g++) sb.Append(gLow[g] > 1e6f ? " -" : " " + ((gLow[g] - floorY) * 1000f).ToString("0.0")); return $"低点mm[躯干 头 左臂 右臂 左腿 右腿]={sb} 臂角 {koArm[0]:0},{koArm[1]:0} 腿角 {koLeg[0]:0},{koLeg[1]:0} 仰角补 {koTilt:0.0} 胯高mm {(hips.position.y - floorY) * 1000f:0.0}"; } }
        public string PartsDebug { get { var sb = new System.Text.StringBuilder(); var cnt = new int[6]; foreach (var pt in parts) cnt[pt.g]++; for (int g = 0; g < 6; g++) sb.Append(cnt[g] + " "); sb.Append("| 躯干组: "); foreach (var pt in parts) if (pt.g == 0) sb.Append(pt.t.name + "<" + pt.t.parent.name + " "); return sb.ToString(); } }
        /// <summary>倒地补偿角的初值：仰面躺着时肩 / 髋离台面大约一个身子半厚，手脚要往下放这么多才着地。</summary>
        void KoSeed()
        {
            float a = Mathf.Asin(Mathf.Clamp((BodyR - gloveRad) / Mathf.Max(armSpan * 0.75f, 1e-4f), 0f, 0.95f)) * Mathf.Rad2Deg;
            float l = Mathf.Asin(Mathf.Clamp((BodyR - footRad) / Mathf.Max(legSpan, 1e-4f), 0f, 0.95f)) * Mathf.Rad2Deg;
            koArm[0] = koArm[1] = a; koLeg[0] = koLeg[1] = l; koTilt = 0f;
        }
        void Lowest()
        {
            for (int g = 0; g < 6; g++) gLow[g] = float.MaxValue;
            foreach (var pt in parts)
            {
                if (!pt.t) continue; var m = pt.t.localToWorldMatrix; float lo = gLow[pt.g];
                foreach (var v in pt.pts) { float y = m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13; if (y < lo) lo = y; }
                gLow[pt.g] = lo;
            }
        }
        public void UseCounter() { counterT = -9f; }
        public bool ConsumeBreak() { bool b = justBroke; justBroke = false; return b; }
        public int Combo => Time.time - comboT < 1.2f ? combo : 0;
        /// <summary>这一招打中了（没被挡就算连击）。</summary>
        public void Landed(bool blocked) { if (dashBonusT > Time.time) { dashBonusT = -9f; dashHit = true; } if (blocked) { combo = 0; return; } combo = Time.time - comboT < 1.2f ? combo + 1 : 1; comboT = Time.time; revengeT = 0; }
        public float Reach => H * 0.62f * Kit.armLen * Kit.reach;

        public void Do(SBGestures.Act a, float windup = -1f, float chargeLevel = 0f)
        {
            if (!Ready || KO || frozen || stun > 0 || a == SBGestures.Act.None) return;
            if (a == SBGestures.Act.Jump) { if (lift <= 0.001f && !Crouching) { vy = 0.7f * Mathf.Sqrt(Kit.jump); lP[0].v -= 300; lP[1].v -= 300; SBFx.Whoosh(Root.position, 0.4f); } return; }
            if (a == SBGestures.Act.Duck) { duckT = 0.6f; return; }
            if (phase == Ph.Wind || phase == Ph.Strike) return;
            // 收招慢的木偶（秤砣、铁甲）：出完一招要缓一下才能出下一招——这是它们实打实的短处，对玩家和电脑都一样。
            // 缓的这一小段里按下的招不丢，排着队等缓完立刻出（手感是“慢”，不是“没反应”）。
            if (phase == Ph.Recover && phT < RecoverLock) { if (windup < 0) { queued = a; queuedLv = chargeLevel; queuedAt = Time.time; } return; }
            queued = SBGestures.Act.None;
            attack = a; attackHit = false; chargeLv = chargeLevel;
            // 玩家的招：极短的预备（看得出往后拉，但不拖手感）；AI 的招：长预备，给玩家躲
            float baseWind = a == SBGestures.Act.HeavyKick ? 0.14f : SBGestures.IsHeavy(a) ? 0.12f : a == SBGestures.Act.Kick ? 0.1f : 0.05f;
            windDur = windup >= 0 ? windup * Mathf.Sqrt(Kit.windK) : baseWind * Kit.windK;   // 电脑的预备也按木偶快慢伸缩：慢木偶出手更好躲，快木偶更难躲
            phase = Ph.Wind; phT = 0;
        }

        float StrikeDur => (attack switch { SBGestures.Act.Heavy or SBGestures.Act.HeavyL => 0.2f, SBGestures.Act.HeavyKick => 0.24f, SBGestures.Act.Kick => 0.18f, _ => 0.13f }) / Mathf.Pow(Speed, 0.8f);
        // 收招里有多长是“动不了”的：普通木偶 0（随时可以接下一招），越慢的锁得越久
        float RecoverLock => RecoverDur * Mathf.Clamp01(Kit.recoverK / Mathf.Pow(Speed, 0.8f) - 1f);
        SBGestures.Act queued; float queuedLv, queuedAt;
        float RecoverDur => Kit.recoverK * (attack switch { SBGestures.Act.Heavy or SBGestures.Act.HeavyL => 0.32f, SBGestures.Act.HeavyKick => 0.4f, SBGestures.Act.Kick => 0.28f, _ => 0.16f }) / Mathf.Pow(Speed, 0.8f);

        void BeginStrike()
        {
            phase = Ph.Strike; phT = 0;
            // 往前冲：基础冲劲 + 出招辅助（对手就在前面一点点时，多冲一步够到他）
            var f = Root.forward;
            bool kick = SBGestures.IsKick(attack), heavy = SBGestures.IsHeavy(attack);
            float push = heavy && !kick ? 0.5f : attack == SBGestures.Act.HeavyKick ? 0.3f : kick ? 0.1f : 0.22f;
            if (opponent)
            {
                var d = opponent.position - Root.position; d.y = 0;
                float dist = d.magnitude, want = kick ? H * 0.75f * Kit.legLen * Kit.reach : Reach;
                if (dist > want && dist < want + H * 0.9f * Kit.lunge) { f = d / dist; push += (dist - want) * 9f * Kit.lunge; }
            }
            vel += f * push;
            twist.v += SBGestures.Arm(attack) == 0 && !kick ? 400 : attack == SBGestures.Act.HeavyKick ? -700 : kick ? 0 : -500;
            SBFx.Whoosh(Root.position, heavy ? 1f : 0.6f);
        }

        /// <summary>出招判定：打出去那一段里返回拳头 / 脚、力度。</summary>
        public bool AttackLive(out Transform limb, out float power)
        {
            limb = null; power = 0;
            if (phase != Ph.Strike || attackHit) return false;
            if (phT < StrikeDur * 0.25f) return false;
            limb = SBGestures.IsKick(attack) ? FootR : SBGestures.Arm(attack) == 0 ? FistL : FistR;
            StrikePoint = LimbPoint(limb); StrikeRad = LimbRad(limb);
            StrikeOrigin = SBGestures.IsKick(attack) ? hp[1].position : sh[SBGestures.Arm(attack)].position;
            power = attack switch
            {
                // 重招 = 快拳的 1.6 倍；蓄力只是再高一点（蓄满 +0.3，到 1.9 倍），不随着蓄力时间一路涨上去。重招真正的用处是破防
                SBGestures.Act.Heavy or SBGestures.Act.HeavyL => 1.6f + 0.3f * chargeLv,
                SBGestures.Act.HeavyKick => 1.85f + 0.3f * chargeLv,                        // 蓄满 2.15，出得慢
                SBGestures.Act.Kick => 1.35f,
                _ => 1f,
            } * Kit.power;
            power *= 1f + Kit.comboBonus * Mathf.Min(Combo, 4);   // 连击加成
            if (revengeT > Time.time) power *= Kit.revenge;        // 反击加成
            power *= 1f + Kit.lowHpPower * (1f - Health / 100f);   // 残血加伤
            power *= dmgOut;
            // 手太长的木偶：对手贴到身前（比臂展的一半还近）就使不上劲
            if (Kit.closeWeak < 1f && foe) { var cd = foe.Root.position - Root.position; cd.y = 0; if (cd.magnitude < Reach * 0.5f + foe.BodyR) power *= Kit.closeWeak; }
            if (dashBonusT > Time.time) power *= 1f + dashBonus;   // 弹网冲刺加伤
            return true;
        }
        public void MarkHit() => attackHit = true;

        /// <summary>点 p 离身体（腰到头顶的胶囊）有多远（≤ 0 = 碰到了）。</summary>
        public float BodyDist(Vector3 p)
        {
            var a = hips.position; var b = Head.position;
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector3.Distance(p, a + ab * t) - BodyR;
        }

        public const float DmgK = 1.25f;   // 伤害系数（原来 7：几下就没血。现在一回合能打 30 秒以上）
        /// <summary>挨打。返回是否挡住。power 已含攻击方力量；伤害按自己的体重打折。</summary>
        // 连着挨打的保护：0.9 秒内连中三下（没挡住的），第三下会把人弹开一大步，并且接下来一小会儿打不出硬直（不会被一路快拳压到死）
        int hurtChain; float lastHurtT = -9f, armorT;
        public bool Armored => armorT > 0f;
        public bool ConsumeBreakAway() { bool b = brokeAway; brokeAway = false; return b; }
        bool brokeAway;

        public bool TakeHit(Vector3 dir, float power, float pierce = 0f, float drain = 0f, bool unblockable = false, bool chainable = true)
        {
            if (KO) return false;
            if (Ulting && (Kit.id == "iron" || Kit.id == "tumbler")) { flash = 0.08f; flashCol = new Color(1f, .8f, .3f); return true; }   // 铁甲冲锋、不倒旋风时打不动
            dir.y = 0; dir.Normalize();
            bool blocked = GuardUp && !unblockable;
            // 每只木偶的血量一样：挨同样的一下，掉的血一样多（体重不再减伤，只决定被推开多远、推不推得动）
            float dmg = power * DmgK * dmgIn * (blocked ? Mathf.Max(Kit.blockDmg, pierce) : 1f);
            bool shrug = armorT > 0f && !blocked;                 // 刚被弹开的保护时间里：还会掉血，但不出硬直、不被打断
            bool away = false;
            if (!blocked && chainable && !shrug)
            {
                hurtChain = Time.time - lastHurtT < 0.9f ? hurtChain + 1 : 1; lastHurtT = Time.time;
                if (hurtChain >= 3) { hurtChain = 0; away = true; armorT = 0.9f; brokeAway = true; }
            }
            if (!blocked) { revengeT = Time.time + 2f; combo = 0; }
            Health = Mathf.Max(0, Health - dmg);
            float kb = power * 0.32f / Kit.weight * (blocked ? 0.3f : 1f) * (1.6f - Health / 100f * 0.6f);
            vel += dir * kb;
            var local = Root.InverseTransformDirection(dir);
            float k = blocked ? 0.3f : 1f;
            lean.v += local.z * 900f * power * k; side.v -= local.x * 900f * power * k;
            hPitch.v += local.z * 1800f * power * k; hRoll.v -= local.x * 1500f * power * k; hYaw.v += Random.Range(-1f, 1f) * 900f * power * k;
            for (int i = 0; i < 2; i++) { sP[i].v += Random.Range(200f, 900f) * power * k; eB[i].v += Random.Range(-600f, 600f) * k; }
            stun = blocked ? 0.06f : shrug ? 0.02f : away ? 0.12f : 0.1f + 0.08f * power;
            if (Kit.steady && !blocked && power < 1.7f) stun = Mathf.Min(stun, 0.03f);   // 下盘稳（不倒翁的长处）：挨快拳、普通的腿几乎不晃，照样掉血，但手上的招不断、能立刻还手；重招照样打得动它
            if (away) vel += dir * 0.5f / Mathf.Sqrt(Kit.weight);   // 第三下：被打得弹出去一大步，脱开对手的拳头
            if (phase == Ph.Wind && !blocked && !shrug && !(Kit.armor && SBGestures.IsHeavy(attack) && power < 1.7f) && !(Kit.steady && power < 1.7f)) phase = Ph.None;   // 被打断（秤砣的重招不会被轻的打断）
            hurtT = blocked ? 0 : 0.35f;
            flash = 0.12f; flashCol = blocked ? new Color(.6f, .8f, 1f) : Color.white;
            if (blocked && drain > 0f)
            {
                GuardHP = Mathf.Max(0f, GuardHP - drain); lastBlockT = Time.time;
                if (GuardHP <= 0f)
                {   // 打穿了：双臂被震开、往后一仰
                    breakT = 1.3f; stun = 0.75f; justBroke = true; hurtT = 0.5f;
                    SBFx.Stuffing(GlovePos(false), dir); SBFx.Stuffing(GlovePos(true), dir);   // 拳套被打穿：棉絮崩出来
                    sR[0].v -= 1500f; sR[1].v += 1500f; sP[0].v += 1200f; sP[1].v += 1200f; lean.v -= 600f;
                    flash = 0.25f; flashCol = new Color(1f, .35f, .2f);
                }
            }
            if (Health <= 0) { KO = true; koT = 0; vel += dir * 0.3f; }
            return blocked;
        }

        public void FenceDamage(float d) { if (!KO) { Health = Mathf.Max(0, Health - d); if (Health <= 0) { KO = true; koT = 0; } } flash = 0.1f; flashCol = new Color(1f, .85f, .6f); }

        // ───────────── 每帧 ─────────────
        void Update()
        {
            if (!Ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.04f), t = Time.time;
            stun -= dt; duckT -= dt; hurtT -= dt; flash -= dt; armorT -= dt;
            if (breakT > 0f) { breakT -= dt; if (breakT <= 0f) GuardHP = Mathf.Max(GuardHP, 45f); }
            else if (!guard && Time.time - lastBlockT > 0.8f) GuardHP = Mathf.Min(100f, GuardHP + 28f * dt);
            guardK = Mathf.MoveTowards(guardK, GuardUp && !KO ? 1f : 0f, dt * (GuardUp ? 11f : 7f));
            {
                float hit = Mathf.Clamp01(1f - (Time.time - lastBlockT) / 0.14f);
                float want = GuardUp && !KO ? Mathf.Lerp(1.04f, GuardGlove, GuardHP / 100f) * (1f - 0.12f * hit) : 1f;
                gloveK = Mathf.Lerp(gloveK, want, 1f - Mathf.Exp(-dt * (want > gloveK ? 16f : 22f)));
            }
            float loose = KO ? 0.25f : 0.55f + 0.45f * Health / 100f;   // 关节紧不紧

            // —— 水平移动：软弹簧追目标 ——
            var p = Root.position;
            bool stunned = stun > 0 || KO;
            var to = (frozen || KO || Ulting ? p : target) - p; to.y = 0;
            float k = Ulting ? 0f : stunned ? 2f : 55f * Speed, d = Ulting ? 1.5f : stunned ? 3f : 10f;
            var acc = to * k - vel * d;
            if (!stunned) acc = Vector3.ClampMagnitude(acc, 3f * Speed);
            bool dashing = dashT > 0f && !stunned && !Ulting;
            if (dashing) { dashT -= dt; acc = Vector3.zero; } else dashT = 0f;
            vel += acc * dt; vel.y = 0;
            // 走路按木偶快慢限速；出拳那一下的前冲不受“走得慢”拖累（手短的木偶靠整个身子撞上去够人）
            if (!stunned && !Ulting && !dashing) vel = Vector3.ClampMagnitude(vel, 0.5f * (phase == Ph.Strike ? Mathf.Max(Speed, 1f) * Kit.lunge : Speed));
            p += vel * dt;
            // 身体碰撞：两个木偶的身子（腰到头顶那一段；倒地时是横着的一段）不许叠在一起，轻的多让、重的少让
            if (foe && foe.Ready && !Ulting && !foe.Ulting && !KO)
            {
                Vector3 fa0 = foe.hips.position, fb0 = foe.Head.position; fa0.y = fb0.y = 0;
                var pp = new Vector3(p.x, 0, p.z); var ab = fb0 - fa0;
                float tt = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(pp - fa0, ab) / ab.sqrMagnitude) : 0f;
                var dv = pp - (fa0 + ab * tt); float dist = dv.magnitude, min = BodyR + foe.BodyR + H * 0.08f + GuardPad + foe.GuardPad;
                if (dist < min)
                {
                    var nn = dist > 1e-4f ? dv / dist : -Root.forward;
                    float share = foe.KO || foe.frozen ? 1f : foe.Kit.weight / (Kit.weight + foe.Kit.weight);
                    p += nn * (min - dist) * share;
                    float into = -Vector3.Dot(vel, nn); if (into > 0) vel += nn * into;
                }
            }
            if (lift > 0 || vy > 0) { vy -= 5.5f * dt; lift = Mathf.Max(0, lift + vy * dt); if (lift == 0 && vy < 0) { vy = 0; crouchV += 6f; lean.v += 200; } }
            p.y = floorY + lift;
            // 八角笼：夹回笼内；撞网速度快就弹回来、掉血
            if (clamp != null)
            {
                var c = clamp(p);
                if ((c - p).sqrMagnitude > 1e-8f)
                {
                    var n = (c - p).normalized; float into = -Vector3.Dot(vel, n);
                    if (Ulting) { if (into > 0.25f) onFence?.Invoke(this, into); vel = Vector3.ProjectOnPlane(vel, n); if (Kit.id == "iron" && ultT < 0.85f) ultT = 0.85f; }   // 必杀撞到网：停下，不伤自己
                    else if (into > 0) { if (into > 0.25f) onFence?.Invoke(this, into); vel += n * into * 1.5f; }
                    p = c;
                }
            }
            Root.position = p;

            // —— 朝向：面朝对手 ——
            if (opponent && !KO && !(Ulting && Kit.id == "tumbler"))
            {
                var f = opponent.position - p; f.y = 0;
                if (f.sqrMagnitude > 1e-5f) yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, (stunned ? 120f : 420f) * dt);
            }
            Root.rotation = Quaternion.Euler(0, yaw, 0);

            // —— 惯性晃动：加速度反方向推身子 ——
            var a = (vel - lastVel) / Mathf.Max(dt, 1e-3f); lastVel = vel;
            var la = Quaternion.Euler(0, -yaw, 0) * a;
            lean.v += -la.z * 9f; side.v += la.x * 9f;
            float sp = new Vector2(vel.x, vel.z).magnitude;
            var lv = Quaternion.Euler(0, -yaw, 0) * vel;

            // —— 姿势目标 ——
            float n1(float o) => (Mathf.PerlinNoise(t * 1.7f + o, seed) - 0.5f) * 2f;
            float mess = 9f * (1.6f - loose) * Kit.loose;
            crouch = Mathf.SmoothDamp(crouch, duckT > 0 ? 1f : 0f, ref crouchV, 0.06f);
            walkPh += dt * (3f + sp * 75f / Mathf.Max(0.6f, Kit.legLen));
            float walk = Mathf.Clamp01(sp * 10f);
            float stance = Mathf.Sin(t * Kit.bobRate + seed) * 0.5f + 0.5f;    // 拳击架势的上下颠

            // 躯干
            twist.t = n1(1) * mess * 0.6f;
            lean.t = Kit.lean0 + crouch * 30f + lv.z * 40f + n1(2) * mess * 0.4f;
            side.t = -lv.x * 30f + n1(3) * mess * 0.4f + Mathf.Sin(t * 2.4f + seed) * Kit.sway;
            bob.t = -stance * H * 0.03f * Kit.bobAmp * (1 - walk) - crouch * H * 0.28f;
            // 头：盯着对手，稍微低头
            hPitch.t = 8f + crouch * 10f + n1(4) * mess * 0.5f; hYaw.t = n1(5) * mess; hRoll.t = n1(6) * mess * 0.5f;
            // 胳膊：拳头护脸（伤得越重越耷拉）
            float droop = (1 - loose) * 50f;
            for (int i = 0; i < 2; i++)
            {
                float sw = Mathf.Sin(walkPh + i * Mathf.PI) * walk;
                sP[i].t = Kit.armP + droop + sw * 15f + n1(10 + i) * mess;
                sR[i].t = (i == 0 ? 1 : -1) * (-12f) ;
                eB[i].t = Kit.armE + droop * 1.2f + n1(12 + i) * mess;
                float lg = Mathf.Sin(walkPh + i * Mathf.PI) * walk * 38f;
                lP[i].t = -lg - 12f - crouch * 55f + n1(14 + i) * mess * 0.5f;
                lK[i].t = 20f + Mathf.Max(0, -Mathf.Cos(walkPh + i * Mathf.PI)) * walk * 55f + crouch * 95f + stance * 6f;
            }
            if (GuardUp)
            {
                // 高位防守：上臂往前下垂、前臂竖起来，两只拳套并排挡在下巴前（只往里收一点点，前臂不交叉）
                // 双手抱头：两只鼓大的拳套并排举在脸前、顶到头顶那么高，把头护在后面。每只木偶的肩宽、臂长、头的大小都不一样，
                // 所以手臂角度是按它自己的身材算出来的（SolveGuard），不是一套固定角度
                if (!guardSolved || (guardSolved2 == false && guardK > 0.97f)) SolveGuard();
                for (int i = 0; i < 2; i++) { sP[i].t = guardAng[i].x; sR[i].t = guardAng[i].y; eB[i].t = guardAng[i].z; }
                hPitch.t = 10f; lean.t += 6f;
            }
            if (charge > 0 && phase == Ph.None)
            {
                if (chargeSide == 2) { lP[1].t = -35f - charge * 30f; lK[1].t = 70f + charge * 30f; twist.t = 25f + charge * 30f; lean.t -= 10f * charge; }   // 回旋踢：右膝提起、身子往后拧
                else
                {
                    int ca = chargeSide; float sg = ca == 0 ? -1f : 1f;
                    sP[ca].t = 35f + charge * 25f; eB[ca].t = -100f; twist.t = sg * (30f + charge * 25f); lean.t -= 8f * charge;
                }
            }
            if (hurtT > 0) { hPitch.t -= 25f; lean.t -= 15f; }
            if (taunt && !KO && phase == Ph.None && !victory)
            {   // 挑衅：右手伸出去招手，左手叉腰，身子往后仰
                sP[1].t = -80f; eB[1].t = -55f + Mathf.Sin(t * 12f) * 30f; sR[1].t = -6f;
                sP[0].t = 12f; eB[0].t = -95f; sR[0].t = -32f; lean.t = -8f; hPitch.t = -10f;
            }
            if (victory && !KO)
            {   // 赢了：双臂高举、仰头、一下一下地跳
                for (int i = 0; i < 2; i++) { sP[i].t = -168f + Mathf.Sin(t * 7f + i * 1.7f) * 7f; sR[i].t = (i == 0 ? -1 : 1) * 16f; eB[i].t = -22f; lP[i].t = -6f; lK[i].t = 12f; }
                hPitch.t = -14f; lean.t = -3f; twist.t = Mathf.Sin(t * 2.2f) * 14f;
                vicT -= dt; if (vicT <= 0f && lift <= 0.001f) { vy = 0.5f; vicT = 0.85f; }
            }

            // 出招三段
            if (phase != Ph.None)
            {
                phT += dt;
                int arm = SBGestures.Arm(attack); float sg = arm == 0 ? -1f : 1f;
                switch (phase)
                {
                    case Ph.Wind:
                        if (attack == SBGestures.Act.HeavyKick) { lP[1].t = -60f; lK[1].t = 100f; twist.t = 55f; lean.t = -14f; sP[0].t = -40f; sP[1].t = 15f; }
                        else if (attack == SBGestures.Act.Kick) { lP[1].t = -70f; lK[1].t = 110f; lean.t = -12f; sP[0].t = -20f; sP[1].t = 10f; }
                        else if (attack == SBGestures.Act.Heavy) { sP[1].t = 45f; eB[1].t = -95f; twist.t = 45f; lean.t = -5f; }
                        else if (attack == SBGestures.Act.HeavyL) { sP[0].t = 20f; eB[0].t = -80f; sR[0].t = -25f; twist.t = -40f; lean.t = -5f; }   // 左勾拳：手往外侧拉开
                        else { sP[arm].t = -35f; eB[arm].t = -125f; twist.t = arm == 0 ? -18f : 18f; }
                        if (phT >= windDur) BeginStrike();
                        break;
                    case Ph.Strike:
                        if (attack == SBGestures.Act.HeavyKick) { lP[1].t = -100f; lK[1].t = -5f; twist.t = -70f; lean.t = -16f; side.t = -8f; lP[0].t = 10f; lK[0].t = 30f; sP[0].t = -50f; sR[0].t = -40f; sP[1].t = 25f; }   // 回旋踢：拧腰甩腿
                        else if (attack == SBGestures.Act.Kick) { lP[1].t = -95f; lK[1].t = -5f; lean.t = -25f; lP[0].t = 10f; lK[0].t = 30f; sP[0].t = -30f; sP[1].t = 20f; }
                        else if (attack == SBGestures.Act.Heavy) { sP[1].t = -100f; eB[1].t = -5f; sR[1].t = 12f; twist.t = -50f; lean.t = 22f; sP[0].t = -30f; eB[0].t = -130f; }
                        else if (attack == SBGestures.Act.HeavyL) { sP[0].t = -85f; eB[0].t = -70f; sR[0].t = 35f; twist.t = 55f; lean.t = 16f; sP[1].t = -30f; eB[1].t = -130f; }   // 左勾拳：弯着胳膊横着抡过来
                        else { sP[arm].t = -92f; eB[arm].t = -2f; sR[arm].t = arm == 0 ? 10f : -10f; twist.t = arm == 0 ? 32f : -32f; lean.t = 14f; }
                        // 出拳瞄着对手的胸口：对手矮就往下打，对手高就往上打（原来一律平着打，高个打矮个会从头顶过去）
                        if (!SBGestures.IsKick(attack) && foe && foe.Ready)
                        {
                            var so = sh[arm].position; var tg = Vector3.Lerp(foe.torso.position, foe.Head.position, 0.55f);
                            float hd = new Vector2(tg.x - so.x, tg.z - so.z).magnitude;
                            sP[arm].t += Mathf.Clamp(Mathf.Atan2(so.y - tg.y, Mathf.Max(hd, 0.01f)) * Mathf.Rad2Deg, -30f, 40f);
                        }
                        if (phT >= StrikeDur) { phase = Ph.Recover; phT = 0; }
                        break;
                    case Ph.Recover:
                        if (phT >= RecoverDur) phase = Ph.None;
                        if (queued != SBGestures.Act.None && phT >= RecoverLock) { var q = queued; queued = SBGestures.Act.None; if (Time.time - queuedAt < 0.6f) Do(q, -1f, queuedLv); }
                        break;
                }
            }

            if (Ulting) UltUpdate(dt, t);

            // 纸翼：站着时轻轻扇动；倒地时摊平
            if (wings[0])
            {
                float wa = KO ? Mathf.Lerp(22f, 2f, Mathf.Clamp01(koT * 2.5f)) : 22f + Mathf.Sin(t * 5f + seed) * 5f + Mathf.Clamp(sp * 40f, 0f, 14f);
                wings[0].localRotation = Quaternion.Euler(0, -wa, 0); wings[1].localRotation = Quaternion.Euler(0, wa, 0);
            }
            // KO：提线断了，整个人瘫下去
            if (KO)
            {
                koT += dt;
                float c = Mathf.Clamp01(koT * 2.2f);
                // 提线一断：先膝盖一软，再整个人往后仰面倒平；腿松开伸直、两臂摊开、头歪向一边
                // 躺平之后，身子、头、两臂、两腿各自量离台面多高，一点点调到都贴着台面（不悬空、不插进去）
                koPitch.t = (-86f - koTilt) * Mathf.Clamp01(koT * 1.8f - 0.15f);
                lean.t = 0f; side.t = 0f; twist.t = 6f * c;
                hPitch.t = -6f * c; hRoll.t = 26f * c; hYaw.t = 0f;
                bob.t = -HipsH * 0.75f * c;
                float knee = koT < 0.25f ? 60f : 4f;
                for (int i = 0; i < 2; i++) { sP[i].t = koArm[i] * c; sR[i].t = (i == 0 ? -1 : 1) * (38f + i * 14f) * c; eB[i].t = -6f * c; lP[i].t = koLeg[i] * c; lK[i].t = knee * c; }
            }

            // —— 弹簧求值 ——
            float z = KO ? (koT < 0.45f ? 0.25f : 1f) : 0.38f;   // 倒地：先软塌塌甩两下，0.45 秒后不再回弹，稳稳躺住          // 阻尼比：小于 1 = 甩过头再回来（乱的来源）
            float kk = 260f * loose * Mathf.Lerp(0.8f, 1.2f, Speed - 0.5f);
            if (phase == Ph.Strike) kk *= 2.6f;      // 打出去那一下要快、要脆
            float bobY = bob.Step(dt, 180f, 0.5f);
            hips.localPosition = new Vector3(0, HipsH + bobY, 0);
            // 扭腰、前后倾只转上半身，腿不跟着转
            float sideA = side.Step(dt, kk * 0.8f, z);
            if (!KO) koPitch.t = 0;
            hips.localRotation = Quaternion.Euler(koPitch.Step(dt, KO ? 90f : 200f, KO ? 0.45f : 0.9f), 0, sideA * 0.3f);
            torso.localRotation = Quaternion.Euler(lean.Step(dt, kk * 0.8f, z), twist.Step(dt, kk, z), sideA * 0.7f);
            headPiv.localRotation = Quaternion.Euler(hPitch.Step(dt, kk * 0.7f, z * 0.8f), hYaw.Step(dt, kk * 0.7f, z * 0.8f), hRoll.Step(dt, kk * 0.7f, z * 0.8f));
            for (int i = 0; i < 2; i++)
            {
                sh[i].localRotation = Quaternion.Euler(sP[i].Step(dt, kk, z), 0, sR[i].Step(dt, kk, z) + (i == 0 ? -armOut : armOut));
                el[i].localRotation = Quaternion.Euler(eB[i].Step(dt, kk, z), 0, 0);
                wR[i].t = Mathf.Lerp(0f, 180f, Mathf.InverseLerp(-35f, -85f, eB[i].t)) * (i == 0 ? -1f : 1f);
                var fj = i == 0 ? FistL : FistR; if (fj) fj.localRotation = Quaternion.Euler(0, wR[i].Step(dt, kk * 1.4f, 0.7f), 0);
                if (fj && !Ulting) fj.localScale = Vector3.one * gloveK;
                float legK = kk * (phase == Ph.Strike && SBGestures.IsKick(attack) && i == 1 ? 1f : 0.9f);
                hp[i].localRotation = Quaternion.Euler(lP[i].Step(dt, legK, z + 0.1f), 0, i == 0 ? -3f : 3f);
                kn[i].localRotation = Quaternion.Euler(lK[i].Step(dt, legK, z + 0.1f), 0, 0);
                // 脚掌始终放平（像真的脚踝）：只跟身子朝向转，不跟着膝盖、大腿一起前后倾；踢腿那只脚才跟着小腿甩出去
                var ft = i == 0 ? FootL : FootR;
                bool kickLeg = i == 1 && SBGestures.IsKick(attack) && (phase == Ph.Wind || phase == Ph.Strike);
                if (ft && !KO && !Ulting)
                {
                    var level = Quaternion.Euler(0, Root.eulerAngles.y, 0);
                    ft.rotation = kickLeg ? Quaternion.Slerp(ft.rotation, ft.parent.rotation, Mathf.Clamp01(dt * 25f)) : Quaternion.Slerp(ft.rotation, level, Mathf.Clamp01(dt * 30f));
                }
                else if (ft) ft.localRotation = Quaternion.Slerp(ft.localRotation, Quaternion.identity, Mathf.Clamp01(dt * 10f));
            }

            // —— 不穿地：姿势算完后找全身最低点，低于台面就整体抬起来（倒地是躺在台面上，不是插进去）——
            Lowest();
            float bodyMin = Mathf.Min(gLow[0], gLow[1]), allMin = bodyMin, fl = floorY + 0.0004f;
            for (int g = 2; g < 6; g++) allMin = Mathf.Min(allMin, gLow[g]);
            if (KO)
            {
                // 倒地：身子（躯干 + 头）的最低点正好落在台面上——高了就放下去，低了就抬起来
                float settle = Mathf.SmoothStep(0f, 1f, (koT - 0.2f) / 0.35f);
                float shift = bodyMin < float.MaxValue ? fl - bodyMin : 0f; if (shift < 0) shift *= settle;
                hips.position += Vector3.up * shift;
                if (koT > 0.6f)
                {
                    // 慢慢逼近（比关节弹簧慢得多，不会互相追着抖）
                    float rate = dt * 3f, cap = 70f * dt;
                    for (int i = 0; i < 2; i++)
                    {   // 胳膊 / 腿：离台面还有多高，就再往下放一点；压进台面了就往上收一点
                        if (gLow[2 + i] < float.MaxValue) koArm[i] = Mathf.Clamp(koArm[i] + Mathf.Clamp(Mathf.Asin(Mathf.Clamp((gLow[2 + i] + shift - fl) / Mathf.Max(armSpan, 1e-4f), -0.7f, 0.7f)) * Mathf.Rad2Deg * rate, -cap, cap), -15f, 95f);
                        if (gLow[4 + i] < float.MaxValue) koLeg[i] = Mathf.Clamp(koLeg[i] + Mathf.Clamp(Mathf.Asin(Mathf.Clamp((gLow[4 + i] + shift - fl) / Mathf.Max(legSpan, 1e-4f), -0.7f, 0.7f)) * Mathf.Rad2Deg * rate, -cap, cap), -30f, 70f);
                    }
                    // 头那头和腰那头不一样高：整个人再往后仰 / 往回收一点，两头都着地
                    if (gLow[1] < float.MaxValue && gLow[0] < float.MaxValue)
                        koTilt = Mathf.Clamp(koTilt + Mathf.Clamp((gLow[1] - gLow[0]) / Mathf.Max(H * 0.3f, 1e-4f) * Mathf.Rad2Deg * dt * 2f, -30f * dt, 30f * dt), -25f, 25f);
                }
            }
            else
            {
                float under = fl - allMin;
                if (under > 0.0006f && allMin < float.MaxValue) hips.position += Vector3.up * under;
            }
            // 拳套 / 脚不许插进对手身子里：最多陷进去小半个拳套（看着是“打实了”），再深两个人就按体重往两边让
            if (foe && foe.Ready && !KO && !Ulting && !foe.Ulting)
            {
                float worst = 0f;
                foreach (var e in new[] { FistL, FistR, FootL, FootR })
                    if (e) worst = Mathf.Max(worst, LimbRad(e) * 0.55f - foe.BodyDist(LimbPoint(e)));
                if (worst > 0f)
                {
                    var nn = Root.position - foe.Root.position; nn.y = 0; nn = nn.sqrMagnitude > 1e-8f ? nn.normalized : -Root.forward;
                    float share = foe.KO || foe.frozen ? 1f : foe.Kit.weight / (Kit.weight + foe.Kit.weight);
                    Root.position += nn * Mathf.Min(worst, 0.012f) * share;
                }
            }
            // 不穿出笼子：拳头 / 脚 / 头伸出笼壁的话，整个身子往回让（像被网弹回来），伸出去最多的那一处说了算
            if (limbClamp != null)
            {
                var push = Vector3.zero;
                foreach (var e in new[] { FistL, FistR, FootL, FootR, Head })
                {
                    if (!e) continue;
                    var dd = limbClamp(e.position) - e.position; dd.y = 0;
                    if (dd.sqrMagnitude > push.sqrMagnitude) push = dd;
                }
                if (push.sqrMagnitude > 1e-8f) hips.position += push;
            }

            // —— 颜色：挨打闪一下；AI 预备动作发红；伤越重越暗 ——
            bool warn = phase == Ph.Wind && windDur > 0.2f;
            float w = warn ? 0.5f + 0.5f * Mathf.Sin(t * 40f) : 0f;
            for (int i = 0; i < rends.Length; i++)
            {
                var col = baseCols[i] * Mathf.Lerp(0.65f, 1f, Health / 100f);
                if (w > 0) col = Color.Lerp(col, new Color(1f, .3f, .15f), w * 0.55f);
                if (gloveR != null && gloveR[i])
                {
                    // 拳套上看状态（闪得慢，不刺眼）：金光 = 可以反击；越挡越“烧红”；被打穿 = 发灰
                    if (CounterReady) col = Color.Lerp(col, new Color(1.5f, 1.15f, .35f), 0.6f + 0.25f * Mathf.Sin(t * 14f));
                    else if (breakT > 0f) col = Color.Lerp(col, new Color(.28f, .26f, .26f), 0.65f);
                    else if (GuardHP < 99f)
                    {
                        float kq = 1f - GuardHP / 100f; if (GuardHP < 35f) kq *= 0.7f + 0.3f * Mathf.Sin(t * 9f);
                        col = Color.Lerp(col, new Color(1.5f, .5f, .08f), kq * 0.8f);
                    }
                }
                if (flash > 0) col = Color.Lerp(col, flashCol, 0.7f);
                rends[i].material.color = col;
            }
        }

        // ───────────── 必杀技 ─────────────
        public void StartUlt()
        {
            if (!UltReady || frozen || Ulting) return;
            Ulting = true; ultT = 0; ultTick = 0; ultHit = false; Meter = 0;
            phase = Ph.None; stun = 0;
            ultFrom = Root.position;
            ultDir = foe ? (foe.Root.position - Root.position) : Root.forward; ultDir.y = 0; ultDir = ultDir.sqrMagnitude > 1e-5f ? ultDir.normalized : Root.forward;
            flash = 0.3f; flashCol = new Color(1f, .85f, .3f);
            SBFx.Ult(Root.position + Vector3.up * H * 0.6f, Kit.id);
        }

        void EndUlt()
        {
            if (!Ulting) return;
            Ulting = false; lift = 0; vy = 0;
            if (el[1]) el[1].localScale = Vector3.one;
            if (FistR) FistR.localScale = Vector3.one;
        }

        // 打中对手：b 在范围 r 里就算（只打一次，或每 tick 秒一次）
        bool UltTouch(float r, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!foe || foe.KO) return false;
            var d = foe.Root.position - Root.position; d.y = 0;
            dir = d.sqrMagnitude > 1e-6f ? d.normalized : Root.forward;
            return d.magnitude < r;
        }

        void UltUpdate(float dt, float t)
        {
            ultT += dt;
            switch (Kit.id)
            {
                case "weight":   // 千斤坠：高高跳起，砸下去一圈冲击波
                {
                    if (ultT < 0.45f) { lift = Mathf.Sin(ultT / 0.45f * Mathf.PI * 0.5f) * H * 0.9f; for (int i = 0; i < 2; i++) { lP[i].t = -60f; lK[i].t = 120f; sP[i].t = -170f; eB[i].t = -20f; } }
                    else if (ultT < 0.6f) { lift = Mathf.Lerp(H * 0.9f, 0, (ultT - 0.45f) / 0.15f); for (int i = 0; i < 2; i++) { sP[i].t = -10f; lP[i].t = -20f; lK[i].t = 40f; } }
                    else if (!ultHit)
                    {
                        ultHit = true; lift = 0; crouchV += 10f;
                        SBFx.Shockwave(Root.position, H * 1.4f, new Color(1f, .75f, .35f));
                        if (UltTouch(H * 1.4f, out var d) && foe.Lift < 0.01f) onUltHit?.Invoke(this, 3.2f, d);
                    }
                    else if (ultT > 1.0f) EndUlt();
                    break;
                }
                case "scarecrow":   // 乌鸦群：张开双臂放出一群乌鸦，一只只啄过去
                {
                    for (int i = 0; i < 2; i++) { sP[i].t = -90f; sR[i].t = i == 0 ? -80f : 80f; eB[i].t = 0f; }
                    hPitch.t = -20f;
                    if (!ultHit) { ultHit = true; if (foe) SBFx.Crows(Root.position + Vector3.up * H * 0.8f, foe, 7, (f, k) => onUltHit?.Invoke(this, 0.55f, (f.Root.position - Root.position).normalized)); }
                    if (ultT > 1.4f) EndUlt();
                    break;
                }
                case "kite":   // 纸鸢冲天：张翅飞上去，从对手头顶俯冲一脚
                {
                    var tp = foe ? foe.Root.position : Root.position + ultDir * 0.1f;
                    if (ultT < 0.55f)
                    {
                        float k = ultT / 0.55f;
                        lift = Mathf.Sin(k * Mathf.PI * 0.5f) * H * 1.1f;
                        var p = Vector3.Lerp(ultFrom, tp - ultDir * H * 0.35f, k * k); p.y = Root.position.y; Root.position = p; vel = Vector3.zero;
                        for (int i = 0; i < 2; i++) { sP[i].t = 20f; sR[i].t = i == 0 ? -100f : 100f; eB[i].t = 0f; lP[i].t = 20f; lK[i].t = 30f; }
                        SBFx.Trail(Root.position + Vector3.up * (lift + H * 0.6f), new Color(1f, .98f, .92f));
                    }
                    else if (ultT < 0.75f)
                    {
                        lift = Mathf.Lerp(H * 1.1f, 0, (ultT - 0.55f) / 0.2f);
                        lP[1].t = -80f; lK[1].t = 0f; lean.t = -30f;
                        if (!ultHit && UltTouch(H * 0.75f, out var d)) { ultHit = true; onUltHit?.Invoke(this, 2.8f, d); }
                    }
                    else { lift = 0; if (ultT > 1.0f) EndUlt(); }
                    break;
                }
                case "bamboo":   // 节节高：右臂一节一节伸长，超远距离一拳
                {
                    float k = Mathf.Clamp01((ultT - 0.15f) / 0.25f);
                    sP[1].t = -92f; eB[1].t = 0f; twist.t = -40f; lean.t = 15f;
                    float grow = ultT < 0.15f ? 1f : ultT < 0.75f ? 1f + k * 3.5f : Mathf.Lerp(4.5f, 1f, (ultT - 0.75f) / 0.25f);
                    // 伸长的胳膊不许捅出笼子：最长只到笼壁
                    if (limbClamp != null && grow > 1f)
                    {
                        float fa = Mathf.Abs(FistR.localPosition.y), lo = 1f, hi = grow;
                        Vector3 Tip(float g) => el[1].position - el[1].up * fa * g;
                        if ((limbClamp(Tip(hi)) - Tip(hi)).sqrMagnitude > 1e-8f)
                        { for (int it = 0; it < 8; it++) { float mid = (lo + hi) * 0.5f; if ((limbClamp(Tip(mid)) - Tip(mid)).sqrMagnitude > 1e-8f) hi = mid; else lo = mid; } grow = lo; }
                    }
                    el[1].localScale = new Vector3(1f, grow, 1f);
                    FistR.localScale = new Vector3(1f, 1f / grow, 1f);   // 拳套不跟着拉长
                    if (k >= 1f && !ultHit)
                    {
                        if (UltTouch(H * 2.8f, out var d) && Vector3.Angle(d, Root.forward) < 30f) { ultHit = true; onUltHit?.Invoke(this, 2.6f, d); }
                    }
                    if (ultT > 1.0f) EndUlt();
                    break;
                }
                case "iron":   // 铁壁冲锋：浑身发光、打不动，用肩膀直线撞过去
                {
                    for (int i = 0; i < 2; i++) { sP[i].t = -40f; eB[i].t = -130f; sR[i].t = i == 0 ? 30f : -30f; }
                    lean.t = 28f; hPitch.t = 25f;
                    if (ultT > 0.2f && ultT < 0.85f) { vel = ultDir * 1.0f; SBFx.Trail(Root.position + Vector3.up * H * 0.5f, new Color(1f, .6f, .25f)); }
                    else vel *= 0.8f;
                    if (!ultHit && ultT > 0.2f && UltTouch(H * 0.7f, out var d)) { ultHit = true; onUltHit?.Invoke(this, 2.6f, ultDir); }
                    if (ultT > 1.1f) EndUlt();
                    break;
                }
                case "tumbler":   // 不倒旋风：像陀螺一样转着撞过去，一路连打
                {
                    yaw += 1100f * dt;
                    for (int i = 0; i < 2; i++) { sP[i].t = -90f; sR[i].t = i == 0 ? -85f : 85f; eB[i].t = 0f; }
                    var d0 = foe ? foe.Root.position - Root.position : ultDir; d0.y = 0;
                    vel = Vector3.Lerp(vel, d0.normalized * 0.35f, dt * 4f);
                    ultTick -= dt;
                    if (ultTick <= 0 && UltTouch(H * 0.8f, out var d)) { ultTick = 0.32f; onUltHit?.Invoke(this, 0.5f, d); }
                    SBFx.Trail(Root.position + Vector3.up * H * 0.4f, new Color(1f, .4f, .3f));
                    if (ultT > 2.0f) EndUlt();
                    break;
                }
                default: EndUlt(); break;
            }
        }

        float HipsH => hipsH;
    }
}