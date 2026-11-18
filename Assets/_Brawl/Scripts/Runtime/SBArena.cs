using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>桌上的八角笼直播现场（全部程序生成）：
    /// - 八角形台子 + 深色帆布台面 + 台面中间的标志；八面铁丝网（着色器画菱形网眼）+ 黑色立柱和防撞垫；
    /// - 外圈两三层看台和一圈小观众（合成一张网格，跳动 / 举手在着色器里，打中、KO 时更激动），靠玩家那一面空出来不挡视线；
    /// - 头顶一圈灯架、四束聚光灯光柱、观众席里随机闪的闪光灯；
    /// - 吊在正中的大屏：两边名字、血条、回合。
    /// 坐标：Arena 的原点在台面中心，+z 指向远离玩家的那边。</summary>
    public class SBArena : MonoBehaviour
    {
        public Material lit, glow, fence, crowdMat, ledAsset;
        public float radius = 0.27f;            // 八角形外接圆半径（米）
        public float fenceH = 0.085f;

        public float Apothem => radius * Mathf.Cos(Mathf.PI / 8f);
        Material padRail, padPost;

        /// <summary>八角形的侧面围板：8 块竖直面，UV 沿周长连续平铺（u 按 高度×1.79 一格，贴图横竖比）。</summary>
        static Mesh Apron(float R, float bottom, float top)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            float tile = (top - bottom) * 1.79f, u = 0;
            for (int i = 0; i < 8; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f;
                var p0 = new Vector3(Mathf.Sin(a0) * R, 0, Mathf.Cos(a0) * R); var p1 = new Vector3(Mathf.Sin(a1) * R, 0, Mathf.Cos(a1) * R);
                var nn = new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am));
                float len = (p1 - p0).magnitude, u1 = u + len / tile;
                int b = v.Count;
                v.Add(p0 + Vector3.up * top); v.Add(p1 + Vector3.up * top); v.Add(p1 + Vector3.up * bottom); v.Add(p0 + Vector3.up * bottom);
                uv.Add(new Vector2(u, 1)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u, 0));
                for (int k = 0; k < 4; k++) n.Add(nn);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);   // 双面（保证从外面看得见）
                u = u1;
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        static readonly int ExciteId = Shader.PropertyToID("_SB_Excite");
        float excite;
        readonly List<Transform> flashes = new();
        readonly List<float> flashT = new();
        Transform screen; TextMesh nameL, nameR, roundTxt; Transform barL, barR, mL, mR;
        readonly SBNet[] nets = new SBNet[8];
        // 顶上护栏的弹性：8 条边各一个弹簧（往外鼓多少）。两头钉在立柱上不动，中间鼓得最多；网的上沿、压条、标志字跟着一起动
        readonly float[] railOff = new float[8], railVel = new float[8], railPush = new float[8];
        class Flex { public Mesh mesh; public Vector3[] baseV, cur; public int[] side; public float[] w; }
        readonly List<Flex> flex = new(); readonly List<(Transform t, Vector3 p0, int side, float w)> flexT = new();
        bool railMoving;
        (int side, float w) RailWeight(Vector3 l)
        {
            float ang = Mathf.Atan2(l.x, l.z); int s = ((Mathf.RoundToInt(ang / (Mathf.PI / 4f)) % 8) + 8) % 8;
            float a0 = (s - 0.5f) * Mathf.PI / 4f, a1 = (s + 0.5f) * Mathf.PI / 4f;
            var c0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * radius; var c1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * radius;
            var d = c1 - c0; float t = Mathf.Clamp01(Vector3.Dot(new Vector3(l.x, 0, l.z) - c0, d) / d.sqrMagnitude);
            float hw = Mathf.Clamp01((l.y - 0.02f) / Mathf.Max(0.01f, fenceH - 0.02f));
            return (s, Mathf.Sin(t * Mathf.PI) * hw);
        }
        void AddFlex(Mesh m)
        {
            if (!m) return;
            var f = new Flex { mesh = m, baseV = m.vertices }; f.cur = (Vector3[])f.baseV.Clone(); f.side = new int[f.baseV.Length]; f.w = new float[f.baseV.Length];
            for (int i = 0; i < f.baseV.Length; i++) { var (s, w) = RailWeight(f.baseV[i]); f.side[i] = s; f.w[i] = w; }
            m.MarkDynamic(); flex.Add(f);
        }
        static Vector3 SideN(int s) => new(Mathf.Sin(s * Mathf.PI / 4f), 0, Mathf.Cos(s * Mathf.PI / 4f));
        /// <summary>撞了某一边的护栏：冲量（米/秒）。</summary>
        public void RailHit(Vector3 world, float impulse)
        {
            var (s, _) = RailWeight(transform.InverseTransformPoint(world));
            railVel[s] += impulse; railMoving = true;
        }
        /// <summary>顶住护栏 / 网：world 处、粗 r 的东西顶出了笼壁多少，这一边就被顶出去多少。</summary>
        public void RailPress(Vector3 world, float r)
        {
            var l = transform.InverseTransformPoint(world); var (s, w) = RailWeight(l);
            float pen = Vector3.Dot(new Vector3(l.x, 0, l.z), SideN(s)) + r - (Apothem - 0.003f);
            if (pen > 0) { railPush[s] = Mathf.Max(railPush[s], Mathf.Min(pen * 0.8f, 0.01f)); railMoving = true; }
        }
        void TickRail(float dt)
        {
            if (!railMoving) return;
            dt = Mathf.Min(dt, 1f / 30f); bool any = false;
            for (int s = 0; s < 8; s++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float h = dt / 2f;
                    railVel[s] += ((railPush[s] - railOff[s]) * 380f - railVel[s] * 7f) * h;   // 弹簧 + 阻尼（偏软、会来回晃两三下）
                    railOff[s] = Mathf.Clamp(railOff[s] + railVel[s] * h, -0.006f, 0.016f);
                }
                railPush[s] = 0;
                if (Mathf.Abs(railOff[s]) > 2e-5f || Mathf.Abs(railVel[s]) > 2e-4f) any = true; else { railOff[s] = 0; railVel[s] = 0; }
                if (nets[s]) nets[s].lean = railOff[s];
            }
            foreach (var f in flex)
            {
                for (int i = 0; i < f.baseV.Length; i++) f.cur[i] = f.baseV[i] + SideN(f.side[i]) * (railOff[f.side[i]] * f.w[i]);
                f.mesh.vertices = f.cur; f.mesh.RecalculateBounds();
            }
            foreach (var (t, p0, s, w) in flexT) if (t) t.localPosition = p0 + SideN(s) * railOff[s] * w;
            railMoving = any;
        }
        public Transform DoorT { get; private set; }

        public void Build()
        {
            RenderSettings.reflectionIntensity = 0.18f;
            float R = radius, a = Apothem;
            Material dark = M(new Color(.09f, .09f, .1f)), mat = M(new Color(.16f, .17f, .2f)), pad = M(new Color(.08f, .08f, .09f)),
                     red = M(new Color(.62f, .12f, .1f)), blue = M(new Color(.12f, .22f, .55f)), line = M(new Color(.85f, .82f, .75f)),
                     wood = M(new Color(.2f, .16f, .13f)), steel = M(new Color(.25f, .25f, .28f));
            // 台子：八角柱（侧面深色）+ 台面
            Mesh(Octagon(R + 0.02f, -0.035f, 0f, true), dark, "台子");
            // 台子侧面：黑漆描金八角纹围板（沿周长平铺）
            var apron = new Material(lit) { color = Color.white }; apron.mainTexture = Resources.Load<Texture2D>("UI/apron"); apron.SetFloat("_Smoothness", 0.3f);
            Mesh(Apron(R + 0.0205f, -0.035f, -0.0005f), apron, "围板");
            // 护栏、立柱：黑皮菱格软包 + 铜钉
            var padTex = Resources.Load<Texture2D>("UI/pad");
            if (padTex)
            {
                padRail = new Material(lit) { color = Color.white }; padRail.mainTexture = padTex; padRail.mainTextureScale = new Vector2(10f, 0.6f); padRail.SetFloat("_Smoothness", 0.5f);
                padPost = new Material(lit) { color = Color.white }; padPost.mainTexture = padTex; padPost.mainTextureScale = new Vector2(1.5f, 3f); padPost.SetFloat("_Smoothness", 0.5f);
                pad = padRail;
            }
            // 台面：印花台布（墨黑帆布 + 朱红金色八角回纹边），平面投影 UV，八角边对齐铁丝网
            var matTex = Resources.Load<Texture2D>("UI/mat");
            if (matTex) { mat = new Material(lit) { color = new Color(1.12f, 1.1f, 1.08f) }; mat.mainTexture = matTex; mat.SetFloat("_Smoothness", 0.15f); }
            Mesh(Octagon(R, 0.0005f, 0.0005f, false), mat, "台面");
            // 台面中间的八角线 + 标志
            void __(Material m) { } __(line);
            // 台面中央的徽章直接印在台布上（两只木偶对打 + 云纹八角）
            // 八面铁丝网（真有粗细、会弹）+ 程序建模的立柱 + 一整圈斜接的软包护栏和底边（转角不重叠、不漏缝）
            var wireMat = new Material(lit) { color = new Color(.05f, .05f, .055f) }; wireMat.SetFloat("_Metallic", 0f); wireMat.SetFloat("_Smoothness", 0.3f);   // 黑色包胶铁丝
            wireMat.SetFloat("_EnvironmentReflections", 0f); wireMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            var brass = new Material(lit) { color = new Color(.72f, .54f, .27f) }; brass.SetFloat("_Metallic", 0.7f); brass.SetFloat("_Smoothness", 0.42f);   // 做旧黄铜
            var lacq = new Material(lit) { color = new Color(.05f, .045f, .045f) }; lacq.SetFloat("_Smoothness", 0.45f);
            var padRing = padRail ?? pad;
            if (padRail) { padRing = new Material(padRail); padRing.mainTextureScale = Vector2.one; }
            for (int i = 0; i < 8; i++)
            {
                float mid = (i + 1) * Mathf.PI / 4f;
                var c = new Vector3(Mathf.Sin(mid), 0, Mathf.Cos(mid)) * a;
                float side = 2f * R * Mathf.Sin(Mathf.PI / 8f);
                var ng = new GameObject("铁丝网"); ng.transform.SetParent(transform, false);
                ng.transform.localPosition = c + Vector3.up * 0.007f;
                ng.transform.localRotation = Quaternion.LookRotation(c.normalized);
                var net = ng.AddComponent<SBNet>(); net.Build(side - 0.016f, fenceH - 0.008f, wireMat);
                nets[(i + 1) % 8] = net;
                // 每面网上下各一条细铜压条（把网钉在护栏上）
                foreach (float yy in new[] { 0.0105f, fenceH - 0.0015f })
                {
                    if (yy > 0.05f) { var sp = new SBGeo(); sp.M = Matrix4x4.TRS(c + c.normalized * 0.0016f + Vector3.up * yy, Quaternion.LookRotation(c.normalized), Vector3.one);
                        for (int q = 0; q < 12; q++) sp.Box(new Vector3(-(side - 0.018f) / 2f + (q + 0.5f) * (side - 0.018f) / 12f, 0, 0), new Vector3((side - 0.018f) / 12f + 0.0002f, 0.0011f, 0.0011f));   // 分 12 段，跟着护栏弯
                        var sg2 = sp.Make(transform, "上压条", brass); AddFlex(sg2.GetComponent<MeshFilter>().sharedMesh); continue; }
                    var strip = Box(transform, c + c.normalized * 0.0016f + Vector3.up * yy, new Vector3(side - 0.018f, 0.0011f, 0.0011f), brass);
                    strip.localRotation = Quaternion.LookRotation(c.normalized);
                }
            }
            // 软包：黑色人造革（光面），红角 / 蓝角立柱用红 / 蓝革；顶上护栏和底边是“枕头”软包 + 压线 + 绑带 + 铜扣，外侧印标志
            var padTex2 = Resources.Load<Texture2D>("UI/pad");
            var vinyl = new Material(lit) { color = new Color(.85f, .85f, .85f) }; vinyl.mainTexture = padTex2; vinyl.mainTextureScale = new Vector2(2.6f, 2.8f); vinyl.SetFloat("_Smoothness", 0.3f);   // 菱格皮革（护栏）：皮革只有柔和的光泽
            var postV = new Material(lit) { color = new Color(.85f, .85f, .85f) }; postV.mainTexture = padTex2; postV.mainTextureScale = new Vector2(1f, 4.2f); postV.SetFloat("_Smoothness", 0.3f);  // 菱格皮革（立柱）
            var strapM = new Material(lit) { color = new Color(.02f, .02f, .022f) }; strapM.SetFloat("_Smoothness", 0.2f);
            var pipeM = new Material(lit) { color = new Color(.16f, .15f, .15f) }; pipeM.SetFloat("_Smoothness", 0.5f);
            var top = SBPad.Rail(R, fenceH + 0.0045f, 0.0088f, 0.0085f, 0.0095f, true, 2, true);
            var go1 = new GameObject("护栏软包"); go1.transform.SetParent(transform, false); go1.AddComponent<MeshFilter>().sharedMesh = top.pad; go1.AddComponent<MeshRenderer>().sharedMaterial = vinyl;
            var bot = SBPad.Rail(R, 0.0054f, 0.0085f, 0.0054f, 0.0095f, false, 1, false);
            var go2 = new GameObject("底边软包"); go2.transform.SetParent(transform, false); go2.AddComponent<MeshFilter>().sharedMesh = bot.pad; go2.AddComponent<MeshRenderer>().sharedMaterial = vinyl;
            AddFlex(top.pad);
            foreach (var o in new[] { top, bot })
            {
                var sg = new GameObject("绑带"); sg.transform.SetParent(transform, false); sg.AddComponent<MeshFilter>().sharedMesh = o.strap; sg.AddComponent<MeshRenderer>().sharedMaterial = strapM;
                if (o.trim) { var tg = new GameObject("压线与铜扣"); tg.transform.SetParent(transform, false); tg.AddComponent<MeshFilter>().sharedMesh = o.trim; tg.AddComponent<MeshRenderer>().sharedMaterial = pipeM; }
                if (o == top) { AddFlex(o.strap); AddFlex(o.trim); }
            }
            // 护栏外侧印的标志（金色字，一段“双偶”一段 TWIN STRINGS 交替）
            for (int k = 0; k < top.logos.Count; k++)
            {
                var (pos, nn) = top.logos[k];
                var lt = SBUI.Text(transform, k % 2 == 0 ? "双偶" : "TWIN STRINGS", k % 2 == 0 ? 0.0075f : 0.0048f, new Color(.85f, .66f, .32f), SBUI.F.Title, TextAnchor.MiddleCenter, pos);
                lt.transform.localRotation = Quaternion.LookRotation(-nn);
                if (k % 2 == 0) SBUI.ZhFont(lt); else SBUI.EnFont(lt);
                var (fs, fw) = RailWeight(pos); flexT.Add((lt.transform, pos, fs, fw));
            }
            // 立柱：底座法兰压在台面上 → 皮革软包（中间微鼓，三道绑带）→ 铜颈圈 + 铜顶帽
            var redV = new Material(postV) { color = new Color(1f, .16f, .12f) }; var blueV = new Material(postV) { color = new Color(.2f, .38f, 1f) };
            var flange = new SBGeo(); var bands = new SBGeo(); var postStraps = new SBGeo();
            var cols = new[] { new SBGeo(), new SBGeo(), new SBGeo() }; var collars = new[] { new SBGeo(), new SBGeo(), new SBGeo() };
            for (int i = 0; i < 8; i++)
            {
                float ang = (i + 0.5f) * Mathf.PI / 4f;
                var cp = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * R;
                float topY = fenceH + 0.0145f;   // 立柱顶比护栏顶（fenceH + 13 毫米）再高一点
                flange.Lathe(cp, new[] { new Vector2(0.0165f, 0f), new Vector2(0.0165f, 0.0022f), new Vector2(0.0125f, 0.0042f) }, 8, Mathf.PI / 8f);
                // 左边两根红、右边两根蓝、前后四根黑
                int ci = (i == 5 || i == 6) ? 1 : (i == 1 || i == 2) ? 2 : 0;
                SBPad.Post(cols[ci], postStraps, cp, 0.0112f, 0.004f, topY - 0.002f);
                // 护栏和底边接到立柱的地方各包一圈软包套（顶上一圈是连着的）
                collars[ci].Lathe(cp, new[] { new Vector2(0.0122f, fenceH - 0.0055f), new Vector2(0.0128f, fenceH - 0.004f), new Vector2(0.0128f, topY - 0.001f), new Vector2(0.0122f, topY) }, 20, 0, false, false);
                collars[ci].Lathe(cp, new[] { new Vector2(0.0122f, 0.0006f), new Vector2(0.0127f, 0.0016f), new Vector2(0.0127f, 0.0098f), new Vector2(0.0122f, 0.0108f) }, 20, 0, false, false);
                bands.Lathe(cp, new[] { new Vector2(0.0122f, topY - 0.0012f), new Vector2(0.0131f, topY - 0.0004f), new Vector2(0.0131f, topY + 0.0012f), new Vector2(0.0122f, topY + 0.002f) }, 20, 0, false, false);
                bands.Lathe(cp, new[] { new Vector2(0.0122f, topY + 0.002f), new Vector2(0.0104f, topY + 0.0048f), new Vector2(0.007f, topY + 0.0072f), new Vector2(0.0032f, topY + 0.0084f), new Vector2(0f, topY + 0.0087f) }, 20, 0, false, false);
            }
            flange.Make(transform, "立柱底座", lacq);
            cols[0].Make(transform, "立柱", postV); cols[1].Make(transform, "红角立柱", redV); cols[2].Make(transform, "蓝角立柱", blueV);
            postStraps.Make(transform, "立柱绑带", strapM);
            Material Leather(Color c) { var m = new Material(lit) { color = c }; m.SetFloat("_Smoothness", 0.3f); return m; }
            collars[0].Make(transform, "护栏包套", Leather(new Color(.06f, .06f, .065f)));
            collars[1].Make(transform, "红角包套", Leather(new Color(.42f, .06f, .05f)));
            collars[2].Make(transform, "蓝角包套", Leather(new Color(.07f, .13f, .4f)));
            bands.Make(transform, "立柱铜件", brass);
            BuildDoor(lacq, brass);
            BuildSteps(lacq, brass);
            BuildCrowd(wood);
            BuildRig(steel);
            BuildScreen(dark);
        }

        // ───────── 看台 + 观众（UFC 场馆：地毯台阶、黑色踢面、深红座椅、台阶灯带、第一排前面一圈滚动的 LED 环屏）─────────
        // 看台是连续的八角阶梯（转角斜接），只占远离玩家的 5 条边；两端是描金收口墙；观众是剪纸人，站在座椅前面，整体压暗（灯光集中在笼子上）
        readonly List<Vector3> heads = new();
        const float StandA0 = 0.32f, StandD = 0.05f;          // 第一级内沿的边心距、每级进深
        const float Sc = 1.5f;                                // 座椅 / 观众（和台阶一起等比放大）
        static float TierTop(int r) => 0.014f + r * 0.027f;
        void BuildCrowd(Material wood)
        {
            float c8 = Mathf.Cos(Mathf.PI / 8f);
            var carpet = new Material(lit) { color = new Color(.3f, .29f, .29f) }; carpet.mainTexture = Resources.Load<Texture2D>("UI/carpet"); carpet.SetFloat("_Smoothness", 0.08f); carpet.mainTextureScale = new Vector2(1f, 1f);
            var riser = new Material(lit) { color = new Color(.42f, .41f, .41f) }; riser.mainTexture = Resources.Load<Texture2D>("UI/riser"); riser.SetFloat("_Smoothness", 0.2f); riser.mainTextureScale = new Vector2(1f / 1.78f, 1f);
            var seatMat = new Material(lit) { color = new Color(.18f, .03f, .035f) }; seatMat.SetFloat("_Smoothness", 0.22f);   // 绒面座椅
            var seatFrame = new Material(lit) { color = new Color(.05f, .05f, .055f) }; seatFrame.SetFloat("_Metallic", 0.3f); seatFrame.SetFloat("_Smoothness", 0.3f);
            var frameMat = new Material(lit) { color = new Color(.06f, .06f, .065f) }; frameMat.SetFloat("_Metallic", 0.2f); frameMat.SetFloat("_Smoothness", 0.25f);   // 哑光喷涂金属
            var brassM = new Material(lit) { color = new Color(.72f, .54f, .27f) }; brassM.SetFloat("_Metallic", 0.7f); brassM.SetFloat("_Smoothness", 0.42f);   // 做旧黄铜
            var apronM = new Material(lit) { color = new Color(.8f, .78f, .76f) }; apronM.mainTexture = Resources.Load<Texture2D>("UI/apron"); apronM.SetFloat("_Smoothness", 0.5f); apronM.mainTextureScale = new Vector2(1f / 1.79f, 1f);
            var stepGlow = new Material(glow); stepGlow.SetColor("_Color", new Color(1f, .62f, .3f, 0.45f)); stepGlow.SetFloat("_Fade", 0f);
            // 阶梯：台面铺地毯，前面的踢面是黑色金属板（铜压条 + 铆钉）
            var tops = new SBGeo(); var sides = new SBGeo(); var lights = new SBGeo(); var seats = new SBGeo(); var seatF = new SBGeo(); var nosing = new SBGeo();
            for (int r = 0; r < 3; r++)
            {
                float ai = StandA0 + r * StandD, ac = ai + StandD / 2f, top = TierTop(r), below = r == 0 ? -0.035f : TierTop(r - 1);
                tops.OctRing(ac / c8, StandD, top - 0.001f, top, 5, 10, 0.06f, false, false, true, false);
                sides.OctRing(ac / c8, StandD, -0.035f, top, 5, 10, top - below, true, r == 2, false, false, false);
                nosing.OctRing((ai + 0.0012f) / c8, 0.0024f, top - 0.0004f, top + 0.0008f, 5, 10, 0.05f, true, false, true, false);
                lights.OctRing((ai + 0.003f) / c8, 0.0016f, top + 0.0002f, top + 0.0004f, 5, 10, 1f, false, false, true, false);
                // 座椅：每条边一排，靠后摆，面朝笼子；金属底座 + 深红坐垫 + 靠背 + 两侧扶手
                for (int i = 5; i < 10; i++)
                {
                    float mid = (i + 1) * Mathf.PI / 4f; var dir = new Vector3(Mathf.Sin(mid), 0, Mathf.Cos(mid)); var tan = Vector3.Cross(Vector3.up, dir);
                    float side = 2f * (ac / c8) * Mathf.Sin(Mathf.PI / 8f) - 0.02f, sp = 0.022f * Sc;
                    int n = Mathf.FloorToInt(side / sp); var rot = Quaternion.LookRotation(-dir);
                    for (int k = 0; k < n; k++)
                    {
                        float u = -((n - 1) * sp) / 2f + k * sp;
                        var bp = dir * (ai + StandD - 0.012f * Sc) + tan * u + Vector3.up * top;
                        seatF.Box(bp + Vector3.up * 0.0042f * Sc, new Vector3(0.004f, 0.0084f, 0.004f) * Sc, rot);
                        seats.Box(bp + Vector3.up * 0.0094f * Sc - dir * 0.001f, new Vector3(0.016f, 0.004f, 0.013f) * Sc, rot);
                        seats.Box(bp + Vector3.up * 0.0168f * Sc + dir * 0.0068f * Sc, new Vector3(0.016f, 0.0125f, 0.0034f) * Sc, rot * Quaternion.Euler(-8, 0, 0));
                        var bt = bp + Vector3.up * 0.0232f * Sc + dir * 0.0077f * Sc;
                        seats.Cyl(bt - tan * 0.008f * Sc, bt + tan * 0.008f * Sc, 0.0017f * Sc, 10);                                    // 靠背圆顶
                        var fe = bp + Vector3.up * 0.0094f * Sc - dir * 0.0075f * Sc;
                        seats.Cyl(fe - tan * 0.008f * Sc, fe + tan * 0.008f * Sc, 0.002f * Sc, 10);                                     // 坐垫前沿圆边
                        foreach (int sx in new[] { -1, 1 }) seatF.Box(bp + Vector3.up * 0.0125f * Sc + tan * sx * 0.0087f * Sc, new Vector3(0.0012f, 0.0012f, 0.011f) * Sc, rot);
                    }
                }
            }
            tops.Make(transform, "看台地毯", carpet); sides.Make(transform, "看台踢面", riser); nosing.Make(transform, "台阶铜沿", brassM);
            lights.Make(transform, "台阶灯带", stepGlow); seats.Make(transform, "座椅", seatMat); seatF.Make(transform, "座椅架", seatFrame);
            // 最后一排背后的围墙：黑色金属板（铜压条 + 铆钉）；左右四段加高，上沿挂一条 LED 字幕带（像场馆二层的环带屏）；正后方一段保持低，不挡标题墙
            float aBack = StandA0 + 3 * StandD, backTop = TierTop(2) + 0.024f, highTop = TierTop(2) + 0.11f;
            var pil = new SBGeo();
            var back = new SBGeo(); var backTrim = new SBGeo(); var ribbon = new SBGeo(); float ru = 0f;
            var ledSh2 = (ledAsset ? ledAsset.shader : Shader.Find("StringBrawl/Led"));
            for (int i = 5; i < 10; i++)
            {
                bool high = i != 7; float tt = high ? highTop : backTop;
                back.OctRing((aBack + 0.002f) / c8, 0.004f, -0.035f, tt, i, i + 1, tt + 0.035f, true, true, false, false, true);
                backTrim.OctRing((aBack + 0.002f) / c8, 0.007f, tt, tt + 0.003f, i, i + 1, 0.05f, true, true, true, false, true);
                // 外侧（场馆外面那侧）每隔 6 厘米一根竖立柱条 + 底下一道踢脚，不再是一整块平板
                {
                    float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f, ro = (aBack + 0.004f) / c8;
                    var p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * ro; var p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * ro;
                    var on = new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am)); var rotO = Quaternion.LookRotation(on);
                    int np = Mathf.Max(2, Mathf.RoundToInt((p1 - p0).magnitude / 0.06f));
                    for (int q = 1; q < np; q++) { var pp = Vector3.Lerp(p0, p1, q / (float)np) + on * 0.0018f; pil.Box(pp + Vector3.up * ((tt - 0.035f) / 2f), new Vector3(0.006f, tt + 0.035f, 0.0036f), rotO); }
                    pil.Box((p0 + p1) / 2f + on * 0.0015f + Vector3.up * -0.029f, new Vector3((p1 - p0).magnitude, 0.012f, 0.003f), rotO);
                }
                if (high)
                {
                    float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f, ar = (aBack - 0.0005f) / c8;
                    var p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * ar; var p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * ar;
                    var nm = -new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am));
                    float ry0 = highTop - 0.03f, ry1 = highTop - 0.006f, tl = (ry1 - ry0) * 23.7f, len = (p1 - p0).magnitude;
                    // 字幕沿相邻两段连续（u 接着上一段往下走），接缝处不断字
                    ribbon.Quad(p1 + Vector3.up * ry0, p0 + Vector3.up * ry0, p0 + Vector3.up * ry1, p1 + Vector3.up * ry1, nm, new Vector2(ru + len / tl, 0), new Vector2(ru, 1));
                    ru += len / tl;
                }
            }
            var backMat = new Material(riser); backMat.mainTextureScale = new Vector2(1f / 1.78f, 1f);
            back.Make(transform, "看台背墙", backMat); backTrim.Make(transform, "背墙压顶", seatFrame); pil.Make(transform, "背墙立柱条", seatFrame);
            var ribMat = ledSh2 ? new Material(ledSh2) : new Material(glow); ribMat.mainTexture = Resources.Load<Texture2D>("UI/led"); ribMat.SetFloat("_Speed", 0.03f); ribMat.SetFloat("_Bright", 0.95f);
            ribbon.Make(transform, "背墙字幕带", ribMat);
            // 看台两端：顺着台阶斜上去的收口墙——描金黑漆面板、外圈铜包边、斜边一条暖色灯带、底下一道黑色踢脚
            var endW = new SBGeo(); var endT = new SBGeo(); var endL = new SBGeo(); var endK = new SBGeo(); var ledR = new SBGeo(); var ledB = new SBGeo();
            foreach (float va in new[] { 112.5f, 247.5f })
            {
                var vd = new Vector3(Mathf.Sin(va * Mathf.Deg2Rad), 0, Mathf.Cos(va * Mathf.Deg2Rad));
                var tn = Vector3.Cross(Vector3.up, vd); if (Vector3.Dot(tn, Vector3.back) < 0) tn = -tn;   // 朝玩家那侧
                Vector3 P(float ap, float y, float off) => vd * (ap / c8) + Vector3.up * y + tn * off;
                float A0w = StandA0 - 0.006f, A3w = StandA0 + 3 * StandD + 0.006f, yLow = TierTop(0) + 0.034f, yHigh = highTop + 0.004f;   // 斜收口一路升到加高的背墙顶，包住背墙端头
                float L = (A3w - A0w) / c8, Hh = yHigh + 0.035f;
                // 面板：哑光深灰金属板；中间嵌一块点阵灯箱屏（左边红、右边蓝，木偶拳手徽章）
                // 金属板贴图按真实尺寸铺（和背墙同一种：铆钉 + 铜压条），每 6 厘米一格
                float uL = L / 0.06f, vH = (yHigh + 0.035f) / (0.06f / 1.78f);
                endW.Quad(P(A0w, -0.035f, 0.006f), P(A3w, -0.035f, 0.006f), P(A3w, yHigh, 0.006f), P(A0w, yLow, 0.006f), tn, Vector2.zero, new Vector2(uL, vH));
                bool leftEnd = vd.x < 0;
                var fwd = -tn; var right = Vector3.Cross(Vector3.up, fwd); bool flipU = Vector3.Dot(vd, right) < 0;
                float la = A0w + 0.008f, lb = A3w - 0.008f, ly0 = -0.027f, ly1 = yLow - 0.007f;
                var led = leftEnd ? ledR : ledB;
                led.Quad(P(la, ly0, 0.0068f), P(lb, ly0, 0.0068f), P(lb, ly1, 0.0068f), P(la, ly1, 0.0068f), tn, new Vector2(flipU ? 1 : 0, 0), new Vector2(flipU ? 0 : 1, 1));
                // 灯箱黑框
                endK.Box((P(la, ly0, 0.0068f) + P(lb, ly0, 0.0068f)) / 2f + Vector3.down * 0.0012f, new Vector3(0.0016f, 0.0024f, (lb - la) / c8 + 0.004f), Quaternion.LookRotation(vd));
                endK.Box((P(la, ly1, 0.0068f) + P(lb, ly1, 0.0068f)) / 2f + Vector3.up * 0.0012f, new Vector3(0.0016f, 0.0024f, (lb - la) / c8 + 0.004f), Quaternion.LookRotation(vd));
                endT.Quad(P(A0w, -0.035f, 0.0015f), P(A3w, -0.035f, 0.0015f), P(A3w, yHigh, 0.0015f), P(A0w, yLow, 0.0015f), -tn);
                endT.Quad(P(A0w, yLow, 0.0015f), P(A3w, yHigh, 0.0015f), P(A3w, yHigh, 0.006f), P(A0w, yLow, 0.006f), Vector3.up);
                endT.Quad(P(A0w, -0.035f, 0.0015f), P(A0w, yLow, 0.0015f), P(A0w, yLow, 0.006f), P(A0w, -0.035f, 0.006f), -vd);
                // 铜包边：斜顶边、前竖边
                endK.Cyl(P(A0w, yLow + 0.0008f, 0.0062f), P(A3w, yHigh + 0.0008f, 0.0062f), 0.0011f, 8);
                endK.Cyl(P(A0w, -0.035f, 0.0062f), P(A0w, yLow + 0.0008f, 0.0062f), 0.0011f, 8);
                endK.Cyl(P(A0w + 0.004f, -0.031f, 0.0063f), P(A3w - 0.004f, -0.031f, 0.0063f), 0.0008f, 8);
                endL.Quad(P(A0w, yLow - 0.0035f, 0.0064f), P(A3w, yHigh - 0.0035f, 0.0064f), P(A3w, yHigh - 0.0022f, 0.0064f), P(A0w, yLow - 0.0022f, 0.0064f), tn);
            }
            var endMat = new Material(riser); endMat.mainTextureScale = Vector2.one; endW.Make(transform, "看台收口面板", endMat); endT.Make(transform, "看台收口墙", frameMat);
            var ledSh = (ledAsset ? ledAsset.shader : Shader.Find("StringBrawl/Led"));
            foreach (var (g, tex, nm) in new[] { (ledR, "UI/led_red", "红角灯箱"), (ledB, "UI/led_blue", "蓝角灯箱") })
            {
                var lm = ledSh ? new Material(ledSh) : new Material(glow); lm.mainTexture = Resources.Load<Texture2D>(tex); lm.SetFloat("_Speed", 0f); lm.SetFloat("_Bright", 0.85f);
                g.Make(transform, nm, lm);
            }
            endK.Make(transform, "收口铁边", seatFrame); endL.Make(transform, "收口灯带", stepGlow);
            BuildLed(frameMat);
            BuildFlags();
            BuildAudience(c8);
        }

        // 剪纸观众：站在座椅前面；有人举着亮着的手机，有人挥红 / 蓝小旗
        void BuildAudience(float c8)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var cols = new List<Color>(); var uvs = new List<Vector2>();
            var seeds = new List<Vector2>(); var foot = new List<Vector2>(); var tris = new List<int>();
            var rnd = new System.Random(7);
            float Rf() => (float)rnd.NextDouble();
            Color[] paper = { new(.82f, .16f, .12f), new(.82f, .16f, .12f), new(.58f, .08f, .07f), new(.58f, .08f, .07f), new(.14f, .12f, .12f), new(.14f, .12f, .12f), new(.86f, .63f, .25f), new(.9f, .86f, .76f), new(.2f, .32f, .7f) };
            var phones = new List<Vector3>(); var minis = new SBGeo(); var miniB = new SBGeo();
            for (int r = 0; r < 3; r++)
            {
                float ai = StandA0 + r * StandD, top = TierTop(r);
                for (int i = 5; i < 10; i++)
                {
                    float mid = (i + 1) * Mathf.PI / 4f; var dir = new Vector3(Mathf.Sin(mid), 0, Mathf.Cos(mid)); var tan = Vector3.Cross(Vector3.up, dir);
                    float side = 2f * ((ai + 0.012f) / c8) * Mathf.Sin(Mathf.PI / 8f) - 0.034f;
                    float spacing = 0.024f * Sc; int n = Mathf.FloorToInt(side / spacing);
                    for (int k = 0; k < n; k++)
                    {
                        if (Rf() < 0.12f) continue;   // 空座
                        float u = -((n - 1) * spacing) / 2f + k * spacing + (Rf() - 0.5f) * 0.004f;
                        var pos = dir * (ai + 0.011f + Rf() * 0.004f) + tan * u + Vector3.up * top;
                        float sc = (0.9f + Rf() * 0.2f) * Sc, w = 0.024f * sc, hgt = 0.048f * sc;
                        int f = rnd.Next(8); bool flip = Rf() < 0.5f;
                        float u0 = f / 8f + 0.002f, u1 = (f + 1) / 8f - 0.002f; if (flip) (u0, u1) = (u1, u0);
                        var col = (paper[rnd.Next(paper.Length)] * ((0.44f - r * 0.06f) * (0.9f + Rf() * 0.2f))).linear; col.a = 1;
                        float ph = Rf(), ang = Mathf.Atan2(tan.z, tan.x);
                        int b = verts.Count;
                        verts.Add(pos - tan * w / 2); verts.Add(pos + tan * w / 2); verts.Add(pos + tan * w / 2 + Vector3.up * hgt); verts.Add(pos - tan * w / 2 + Vector3.up * hgt);
                        uvs.Add(new Vector2(u1, 0)); uvs.Add(new Vector2(u0, 0)); uvs.Add(new Vector2(u0, 1)); uvs.Add(new Vector2(u1, 1));
                        foot.Add(Vector2.zero); foot.Add(Vector2.zero); foot.Add(new Vector2(hgt, 0)); foot.Add(new Vector2(hgt, 0));
                        for (int q = 0; q < 4; q++) { norms.Add(-dir); cols.Add(col); seeds.Add(new Vector2(ph, ang)); }
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                        var head = pos + Vector3.up * hgt * 0.85f; heads.Add(head);
                        float roll = Rf();
                        if (roll < 0.1f) phones.Add(head + Vector3.up * hgt * 0.22f + tan * (Rf() - 0.5f) * w * 0.6f - dir * 0.002f);   // 举着手机
                        else if (roll < 0.17f)
                        {
                            // 挥小旗：一根小杆 + 一面红或蓝的小旗
                            var hp = head + Vector3.up * hgt * 0.15f + tan * w * 0.45f - dir * 0.002f;
                            minis.Cyl(hp, hp + Vector3.up * 0.022f, 0.0004f, 4);
                            (Rf() < 0.5f ? minis : miniB).Quad(hp + Vector3.up * 0.012f, hp + Vector3.up * 0.012f + tan * 0.012f, hp + Vector3.up * 0.022f + tan * 0.012f, hp + Vector3.up * 0.022f, -dir);
                        }
                    }
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetColors(cols); mesh.SetUVs(0, uvs); mesh.SetUVs(1, seeds); mesh.SetUVs(2, foot); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var cm = new Material(crowdMat); cm.mainTexture = Resources.Load<Texture2D>("UI/crowd");
            Mesh(mesh, cm, "观众");
            var redF = new Material(lit) { color = new Color(.75f, .1f, .08f) }; redF.SetFloat("_Cull", 0f);
            var blueF = new Material(lit) { color = new Color(.12f, .22f, .75f) }; blueF.SetFloat("_Cull", 0f);
            if (!minis.Empty) minis.Make(transform, "观众红旗", redF);
            if (!miniB.Empty) miniB.Make(transform, "观众蓝旗", blueF);
            // 手机屏幕的光：一直亮着的小白点（偶尔闪一下）
            var phoneMat = new Material(glow); phoneMat.SetColor("_Color", new Color(.85f, .92f, 1f, 1.3f)); phoneMat.SetFloat("_Fade", 0f);
            foreach (var p in phones)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false); q.transform.localPosition = p; q.transform.localScale = new Vector3(0.0035f, 0.006f, 1);
                q.GetComponent<Renderer>().sharedMaterial = phoneMat; billboardsArena.Add(q.transform);
            }
            for (int i = 0; i < 34 && heads.Count > 0; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.localPosition = heads[rnd.Next(heads.Count)] + Vector3.up * 0.007f;
                q.transform.localScale = Vector3.one * 0.012f;
                q.GetComponent<Renderer>().material = glow;
                q.SetActive(false);
                flashes.Add(q.transform); flashT.Add(0);
            }
        }
        readonly List<Transform> billboardsArena = new();

        /// <summary>LED 环屏：第一级前沿上方一圈屏（朝笼子滚动字幕），外框黑色金属；桁架下再挂一圈双面环屏（侧面和前面，抬头就能看到）。</summary>
        void BuildLed(Material frameMat)
        {
            float c8 = Mathf.Cos(Mathf.PI / 8f), a = StandA0 - 0.002f, y0 = TierTop(0) + 0.0015f, y1 = y0 + 0.032f;
            var ledShader = (ledAsset ? ledAsset.shader : Shader.Find("StringBrawl/Led"));
            var ledMat = ledShader ? new Material(ledShader) : new Material(glow);
            ledMat.mainTexture = Resources.Load<Texture2D>("UI/led"); ledMat.SetFloat("_Bright", 1.1f);
            var screenG = new SBGeo(); float u = 0, tile = (y1 - y0) * 23.7f;
            for (int i = 5; i < 10; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f;
                var p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * (a / c8); var p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * (a / c8);
                var nm = -new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am));
                float u1 = u + (p1 - p0).magnitude / tile;
                screenG.Quad(p1 + Vector3.up * y0, p0 + Vector3.up * y0, p0 + Vector3.up * y1, p1 + Vector3.up * y1, nm, new Vector2(u1, 0), new Vector2(u, 1));
                u = u1;
            }
            screenG.Make(transform, "LED环屏", ledMat);
            var frame = new SBGeo();
            frame.OctRing((a + 0.0032f) / c8, 0.0055f, TierTop(0) - 0.001f, y1 + 0.002f, 5, 10, 0.05f, false, true, true, false, true);
            frame.Make(transform, "LED环屏外框", frameMat);
            // 桁架环屏：挂在桁架下面左右两侧，里外两面都是屏
            float h = 0.53f - 0.007f - 0.003f, r = radius * 0.95f + 0.004f, ty1 = h, ty0 = h - 0.024f;
            var ring = new SBGeo(); var ringF = new SBGeo(); u = 0; tile = (ty1 - ty0) * 23.7f;
            foreach (int i in new[] { 1, 2, 5, 6 })   // 只挂左右两侧四段：前面不挡标题墙，后面不挡吊挂墙
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * r; var p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * r;
                bool lamp0 = i == 2 || i == 3 || i == 5 || i == 6, lamp1 = (i + 1) == 2 || (i + 1) == 3 || (i + 1) == 5 || (i + 1) == 6;
                var dd = (p1 - p0).normalized;
                if (lamp0) p0 += dd * 0.035f; if (lamp1) p1 -= dd * 0.035f;
                var nOut = Vector3.Cross(p1 - p0, Vector3.up).normalized; if (Vector3.Dot(nOut, p0) < 0) nOut = -nOut;
                float u1 = u + (p1 - p0).magnitude / tile;
                ring.Quad(p0 + nOut * 0.0015f + Vector3.up * ty0, p1 + nOut * 0.0015f + Vector3.up * ty0, p1 + nOut * 0.0015f + Vector3.up * ty1, p0 + nOut * 0.0015f + Vector3.up * ty1, nOut, new Vector2(u1, 0), new Vector2(u, 1));   // 外面：从外往里看字是正的
                ring.Quad(p1 - nOut * 0.0015f + Vector3.up * ty0, p0 - nOut * 0.0015f + Vector3.up * ty0, p0 - nOut * 0.0015f + Vector3.up * ty1, p1 - nOut * 0.0015f + Vector3.up * ty1, -nOut, new Vector2(u1, 0), new Vector2(u, 1));
                ringF.Box((p0 + p1) / 2f + Vector3.up * (ty0 - 0.0012f), new Vector3((p1 - p0).magnitude + 0.003f, 0.0024f, 0.0042f), Quaternion.LookRotation(nOut));
                ringF.Box((p0 + p1) / 2f + Vector3.up * (ty1 + 0.0008f), new Vector3((p1 - p0).magnitude + 0.003f, 0.0016f, 0.0042f), Quaternion.LookRotation(nOut));
                u = u1;
            }
            var ringMat = new Material(ledMat); ringMat.SetFloat("_Speed", -0.05f); ringMat.SetFloat("_Bright", 1.25f);
            ring.Make(transform, "桁架环屏", ringMat); ringF.Make(transform, "桁架环屏框", frameMat);
        }

        /// <summary>看台四面挂旗（左红右蓝）：每侧一根在看台中段后面、一根在看台前端外角；旗子底边高过背墙，飘起来也碰不到墙。
        /// 每侧两根旗杆之间拉一串三角彩旗。旗杆：配重底座、三道铜箍、铜顶珠，顶上一根横杆，旗子整幅挂在横杆下随风飘。</summary>
        void BuildFlags()
        {
            float c8 = Mathf.Cos(Mathf.PI / 8f);
            var poleMat = new Material(lit) { color = new Color(.1f, .1f, .11f) }; poleMat.SetFloat("_Metallic", 0.4f); poleMat.SetFloat("_Smoothness", 0.35f);
            var brs = new Material(lit) { color = new Color(.72f, .54f, .27f) }; brs.SetFloat("_Metallic", 0.7f); brs.SetFloat("_Smoothness", 0.42f);   // 做旧黄铜
            float aBack = StandA0 + 3 * StandD;
            var pole = new SBGeo(); var trim = new SBGeo();
            var tops = new Dictionary<bool, List<Vector3>> { [true] = new(), [false] = new() };
            // (角度, 离中心的距离)：侧面中段、前端外角
            // 两侧各两面，挪到标题墙两侧（从玩家位置看，正好落在标题墙左右外沿之外，不挡字）；底座贴着看台背墙外面
            float Edge(float deg) { float best = 0; for (int k = 0; k < 8; k++) best = Mathf.Max(best, Mathf.Cos((deg - k * 45f) * Mathf.Deg2Rad)); return (aBack + 0.026f) / best; }
            var spots = new List<(float deg, float rad)>();
            foreach (float dg in new[] { 50f, 70f, 90f }) { spots.Add((360f - dg, Edge(360f - dg))); spots.Add((dg, Edge(dg))); }   // 每侧三面
            const float hgt = 0.4f, bw = 0.085f;
            foreach (var (deg, rad) in spots)
            {
                bool left = deg > 180f;
                var dir = new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0, Mathf.Cos(deg * Mathf.Deg2Rad)); var tan = Vector3.Cross(Vector3.up, dir);
                var basePos = dir * rad + Vector3.up * -0.035f;
                pole.Lathe(basePos, new[] { new Vector2(0.011f, 0f), new Vector2(0.011f, 0.004f), new Vector2(0.0075f, 0.007f), new Vector2(0.0035f, 0.009f) }, 16);
                pole.Cyl(basePos + Vector3.up * 0.008f, basePos + Vector3.up * hgt, 0.0019f, 12);
                foreach (float yb in new[] { 0.012f, hgt * 0.5f, hgt - 0.03f }) trim.Lathe(basePos + Vector3.up * yb, new[] { new Vector2(0.0028f, -0.0025f), new Vector2(0.0028f, 0.0025f) }, 12);
                trim.Lathe(basePos + Vector3.up * hgt, new[] { new Vector2(0.0024f, 0f), new Vector2(0.0045f, 0.002f), new Vector2(0.0045f, 0.004f), new Vector2(0.0024f, 0.006f), new Vector2(0.004f, 0.0085f), new Vector2(0f, 0.013f) }, 16);
                var cy = basePos + Vector3.up * (hgt - 0.008f) - dir * 0.0045f;
                pole.Cyl(basePos + Vector3.up * (hgt - 0.008f), cy, 0.0011f, 8);
                pole.Cyl(cy - tan * (bw / 2f + 0.006f), cy + tan * (bw / 2f + 0.006f), 0.0013f, 10);
                foreach (int sx in new[] { -1, 1 }) trim.Lathe(cy + tan * sx * (bw / 2f + 0.007f) + Vector3.down * 0.0025f, new[] { new Vector2(0f, 0f), new Vector2(0.0025f, 0.0015f), new Vector2(0.0025f, 0.0035f), new Vector2(0f, 0.005f) }, 10);
                tops[left].Add(basePos + Vector3.up * (hgt + 0.003f) + dir * 0.008f);   // 彩旗绳系在横杆之上、旗杆后面（离旗面 1.4 厘米），旗子飘起来也碰不到
                var tex = Resources.Load<Texture2D>(left ? "UI/flag_red" : "UI/flag_blue");
                var fm = new Material(lit) { color = new Color(.66f, .64f, .62f) }; fm.mainTexture = tex; fm.SetFloat("_Smoothness", 0.25f); fm.SetFloat("_Cull", 0f);
                fm.SetFloat("_EnvironmentReflections", 0f); fm.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                var fg = new GameObject(left ? "红旗" : "蓝旗"); fg.transform.SetParent(transform, false);
                fg.transform.localPosition = cy - tan * (bw / 2f) + Vector3.down * 0.0012f - dir * 0.0016f;
                fg.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                if (Vector3.Dot(fg.transform.right, transform.TransformDirection(tan)) < 0) fg.transform.localPosition = cy + tan * (bw / 2f) + Vector3.down * 0.0012f - dir * 0.0016f;
                var flag = fg.AddComponent<SBFlag>(); flag.w = bw; flag.h = bw * 16f / 9f; flag.amp = 0.007f; flag.topHung = true; flag.Build(fm);
            }
            pole.Make(transform, "旗杆", poleMat); trim.Make(transform, "旗杆铜件", brs);
            // 三角彩旗串：两根旗杆之间一根下垂的细绳，挂一串小三角旗（左边红 / 黑 / 金，右边蓝 / 黑 / 金）
            var rope = new SBGeo(); var colsG = new[] { new SBGeo(), new SBGeo(), new SBGeo(), new SBGeo() };   // 红、蓝、黑、金
            foreach (bool left in new[] { true, false })
            {
                for (int seg = 0; seg + 1 < tops[left].Count; seg++) {
                var a = tops[left][seg]; var b = tops[left][seg + 1];
                int n = Mathf.Max(6, Mathf.RoundToInt((b - a).magnitude / 0.022f));
                Vector3 Pt(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.022f);
                for (int k = 0; k < 16; k++) rope.Cyl(Pt(k / 16f), Pt((k + 1) / 16f), 0.0004f, 4, false);
                var outN = Vector3.Cross(b - a, Vector3.up).normalized; if (Vector3.Dot(outN, (a + b) * 0.5f) > 0) outN = -outN;   // 朝笼子
                for (int k = 2; k < n - 1; k++)   // 两头各空一格：离旗杆近的地方不挂，免得碰到横杆和旗子
                {
                    float t0 = (k - 0.38f) / n, t1 = (k + 0.38f) / n;
                    var p0 = Pt(t0); var p1 = Pt(t1); var tip = (p0 + p1) * 0.5f + Vector3.down * 0.016f;
                    int ci = (k % 3 == 0) ? 3 : (k % 3 == 1 ? (left ? 0 : 1) : 2);
                    colsG[ci].Tri(p0, p1, tip, outN);
                }
                }
            }
            rope.Make(transform, "彩旗绳", poleMat);
            Material Cloth(Color c) { var m = new Material(lit) { color = c }; m.SetFloat("_Smoothness", 0.15f); m.SetFloat("_Cull", 0f); return m; }
            var names = new[] { "彩旗红", "彩旗蓝", "彩旗黑", "彩旗金" };
            var clr = new[] { new Color(.55f, .08f, .06f), new Color(.08f, .15f, .5f), new Color(.06f, .06f, .065f), new Color(.6f, .45f, .18f) };
            for (int i = 0; i < 4; i++) if (!colsG[i].Empty) { var g = colsG[i].Make(transform, names[i], Cloth(clr[i])); g.AddComponent<SBSway>().amp = 0.6f; }
        }

        // 靠玩家那一面（-z 方向，左右各 55°）不放观众，免得挡住视线
        static bool Gap(float ang) { float d = Mathf.DeltaAngle(ang * Mathf.Rad2Deg, 180f); return Mathf.Abs(d) < 55f; }

        static void AddBox(List<Vector3> v, List<Vector3> n, List<Color> c, List<Vector2> s, List<int> t, Vector3 center, Quaternion rot, Vector3 size, Color col, Vector2 seed)
        {
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(d, u);
                int b = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 0 || k == 3) ? -1 : 1, sw = k < 2 ? -1 : 1;
                    var lp = (d + u * su + w * sw) * 0.5f;
                    v.Add(center + rot * Vector3.Scale(lp, size)); n.Add(rot * d); c.Add(col); s.Add(seed);
                }
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
        }

        // ───────── 灯架：八角方形桁架（合成一张网格）+ 程序建模的舞台灯 + 台面光斑 + 桁架下一圈灯带 + 往上的吊索 ─────────
        // 后面三个角不挂灯（不挡吊挂墙）；灯光集中打在笼子里，场馆其余部分是暗的（UFC 的感觉）
        void BuildRig(Material steel)
        {
            float h = 0.53f, r = radius * 0.95f, t = 0.014f;
            var trussMat = new Material(lit) { color = new Color(.1f, .1f, .11f) }; trussMat.SetFloat("_Metallic", 0.35f); trussMat.SetFloat("_Smoothness", 0.3f);   // 粉末喷涂桁架
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var c0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)); var c1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                Vector3 Cn(Vector3 c, int k) => c * (r + ((k & 1) == 0 ? -t / 2 : t / 2)) + Vector3.up * (h + ((k & 2) == 0 ? -t / 2 : t / 2));
                for (int k = 0; k < 4; k++) Bar(V, N, T, Cn(c0, k), Cn(c1, k), 0.0016f);
                int seg = 9; int[,] faces = { { 0, 1 }, { 2, 3 }, { 0, 2 }, { 1, 3 } };
                for (int f = 0; f < 4; f++)
                    for (int q = 0; q < seg; q++)
                    {
                        float u0 = q / (float)seg, u1 = (q + 1) / (float)seg;
                        int ka = faces[f, q % 2], kb = faces[f, (q + 1) % 2];
                        Bar(V, N, T, Vector3.Lerp(Cn(c0, ka), Cn(c1, ka), u0), Vector3.Lerp(Cn(c0, kb), Cn(c1, kb), u1), 0.0009f);
                    }
            }
            var tm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; tm.SetVertices(V); tm.SetNormals(N); tm.SetTriangles(T, 0); tm.RecalculateBounds();
            Mesh(tm, trussMat, "桁架");
            // 桁架底下一圈冷白灯带（像场馆灯光罩的光边）
            var ringGlow = new Material(glow); ringGlow.SetColor("_Color", new Color(.85f, .9f, 1f, 0.55f)); ringGlow.SetFloat("_Fade", 0f);
            var rg = new SBGeo(); rg.M = Matrix4x4.Rotate(Quaternion.Euler(0, 22.5f, 0));
            rg.OctRing(r / Mathf.Cos(Mathf.PI / 8f) * Mathf.Cos(Mathf.PI / 8f), 0.004f, h - t / 2f - 0.0008f, h - t / 2f - 0.0006f, 0, 8, 1f, false, false, false, true);
            rg.Make(transform, "桁架灯带", ringGlow);
            var cable = new Material(lit) { color = new Color(.06f, .06f, .06f) };
            var poolMat = new Material(glow); poolMat.SetColor("_Color", new Color(1f, .9f, .75f, 0.3f)); poolMat.SetFloat("_Fade", 1f); spots.Add(poolMat);
            var body = new SBGeo(); var brs = new SBGeo(); var lens = new SBGeo();
            var lampMat = new Material(lit) { color = new Color(.08f, .08f, .085f) }; lampMat.SetFloat("_Metallic", 0.4f); lampMat.SetFloat("_Smoothness", 0.35f);
            var brassM = new Material(lit) { color = new Color(.72f, .54f, .27f) }; brassM.SetFloat("_Metallic", 0.7f); brassM.SetFloat("_Smoothness", 0.42f);   // 做旧黄铜
            var lensMat = new Material(glow); lensMat.SetColor("_Color", new Color(1f, .93f, .8f, 1.6f)); lensMat.SetFloat("_Fade", 0f);
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f; var c = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0));
                if (i % 2 == 1) Box(transform, c * r + Vector3.up * (h + 0.2f), new Vector3(0.0014f, 0.4f, 0.0014f), cable);
                if (i == 0 || i == 1 || i == 7 || i == 4) continue;   // 背后三个角、玩家这侧不挂灯：不挡吊挂墙和视线
                var p0 = c * r + Vector3.up * (h - t / 2f);
                var target = -c * radius * 0.22f;
                var lampPos = p0 + Vector3.down * 0.026f;
                var dir = (target - lampPos).normalized;
                // 舞台灯（本地 y = 灯口方向，x = 水平的转轴），放大 1.6 倍让头显里看得清：
                // 桁架上的 C 形夹 → 吊杆 → U 形支架（两头锁紧旋钮）→ 灯筒（后盖一圈散热片、四道散热环）→ 色纸框 → 铜灯口 → 发光镜片 → 四片外翻遮光板；外加一根保险钢丝
                var Q = Quaternion.LookRotation(dir, c) * Quaternion.Euler(90, 0, 0);
                var LM = Matrix4x4.TRS(lampPos, Q, Vector3.one * 1.6f);
                body.M = LM; brs.M = LM; lens.M = LM;
                var U = Quaternion.Inverse(Q) * Vector3.up;
                var X = Vector3.right;
                body.Cyl(-X * 0.0092f, X * 0.0092f, 0.0011f, 8);                                                       // 转轴
                foreach (int sx in new[] { -1, 1 })
                {
                    body.Cyl(X * sx * 0.0088f, X * sx * 0.0088f + U * 0.0105f, 0.001f, 8);                             // 支架两臂
                    var save2 = brs.M; brs.M = LM * Matrix4x4.TRS(X * sx * 0.0098f, Quaternion.FromToRotation(Vector3.up, X * sx), Vector3.one);
                    brs.Lathe(Vector3.zero, new[] { new Vector2(0.0022f, 0f), new Vector2(0.0022f, 0.0012f), new Vector2(0.0016f, 0.0018f), new Vector2(0.0016f, 0.0026f), new Vector2(0f, 0.0028f) }, 10);   // 锁紧旋钮
                    brs.M = save2;
                }
                body.Cyl(-X * 0.0088f + U * 0.0105f, X * 0.0088f + U * 0.0105f, 0.001f, 8);                           // 支架横梁
                body.Cyl(U * 0.0105f, U * 0.0148f, 0.0012f, 8);                                                        // 吊杆
                body.Box(U * 0.0158f, new Vector3(0.0042f, 0.0026f, 0.0042f), Quaternion.FromToRotation(Vector3.up, U));   // C 形夹
                // 灯筒：后盖（圆顶）+ 筒身 + 四道散热环
                body.Lathe(Vector3.zero, new[] { new Vector2(0f, -0.0145f), new Vector2(0.0028f, -0.0142f), new Vector2(0.0046f, -0.0128f), new Vector2(0.0052f, -0.012f), new Vector2(0.0066f, -0.0095f), new Vector2(0.0066f, 0.0078f) }, 20, 0, false, false);
                for (int f = 0; f < 4; f++) body.Lathe(Vector3.zero, new[] { new Vector2(0.0066f, -0.0088f + f * 0.0022f), new Vector2(0.0072f, -0.0086f + f * 0.0022f), new Vector2(0.0072f, -0.0078f + f * 0.0022f), new Vector2(0.0066f, -0.0076f + f * 0.0022f) }, 20, 0, false, false);
                for (int f = 0; f < 12; f++)
                {
                    var q = Quaternion.Euler(0, f * 30f, 0);
                    body.Box(q * new Vector3(0, -0.0132f, 0.0038f), new Vector3(0.0004f, 0.0022f, 0.0034f), q);                                    // 后盖散热片
                }
                // 色纸框 + 铜灯口 + 镜片
                body.Box(new Vector3(0, 0.0088f, 0), new Vector3(0.016f, 0.0014f, 0.016f));
                brs.Lathe(Vector3.zero, new[] { new Vector2(0.0064f, 0.0095f), new Vector2(0.0074f, 0.0099f), new Vector2(0.0074f, 0.0114f), new Vector2(0.0058f, 0.0114f) }, 20, 0, false, false);
                lens.Lathe(Vector3.zero, new[] { new Vector2(0.0058f, 0.0106f), new Vector2(0.004f, 0.0109f), new Vector2(0f, 0.011f) }, 20, 0, false, false);
                for (int f = 0; f < 4; f++)
                {
                    var q = Quaternion.Euler(0, f * 90f + 45f, 0);
                    body.Box(q * new Vector3(0, 0.0138f, 0.0078f), new Vector3(0.0105f, 0.0062f, 0.0005f), q * Quaternion.Euler(30f, 0, 0));    // 遮光板
                    brs.Cyl(q * new Vector3(-0.0045f, 0.0112f, 0.0076f), q * new Vector3(0.0045f, 0.0112f, 0.0076f), 0.0005f, 6);               // 遮光板铰链
                }
                // 保险钢丝：从灯筒后部绕到吊杆
                brs.Cyl(new Vector3(0, -0.006f, -0.0068f), U * 0.0148f + new Vector3(0, 0, -0.001f), 0.00035f, 4);
                body.M = Matrix4x4.identity; brs.M = Matrix4x4.identity; lens.M = Matrix4x4.identity;
                if (i == 2 || i == 6)
                {
                    var cone = new GameObject("光柱"); cone.transform.SetParent(transform, false);
                    float len = (target - lampPos).magnitude;
                    cone.AddComponent<MeshFilter>().sharedMesh = Cone(0.006f, 0.065f, len);
                    cone.AddComponent<MeshRenderer>().material = SpotMat();
                    cone.transform.localPosition = lampPos + dir * 0.019f;
                    cone.transform.localRotation = Quaternion.FromToRotation(Vector3.down, dir);
                }
                var pool = new GameObject("光斑"); pool.transform.SetParent(transform, false);
                pool.AddComponent<MeshFilter>().sharedMesh = Disc(0.09f);
                pool.AddComponent<MeshRenderer>().sharedMaterial = poolMat;
                pool.transform.localPosition = target + Vector3.up * 0.0011f;
                pool.transform.localScale = new Vector3(1f, 1f, 1.3f);
                pool.transform.localRotation = Quaternion.LookRotation(new Vector3(-c.x, 0, -c.z));
            }
            var haze = new GameObject("光雾"); haze.transform.SetParent(transform, false);
            haze.AddComponent<MeshFilter>().sharedMesh = Cone(r * 0.75f, radius * 0.98f, h - 0.02f);
            var hazeMat = new Material(glow); hazeMat.SetColor("_Color", new Color(.9f, .92f, 1f, 0.05f)); hazeMat.SetFloat("_Fade", 1f); cones.Add(hazeMat);
            haze.AddComponent<MeshRenderer>().sharedMaterial = hazeMat; haze.transform.localPosition = Vector3.up * (h - 0.012f);
            body.Make(transform, "舞台灯", lampMat); brs.Make(transform, "舞台灯铜圈", brassM); lens.Make(transform, "舞台灯镜片", lensMat);
        }

        /// <summary>一根细方管（a → b），4 个面，加进合并网格。</summary>
        static void Bar(List<Vector3> V, List<Vector3> N, List<int> T, Vector3 a, Vector3 b, float r)
        {
            var d = (b - a).normalized; var s1 = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized; var s2 = Vector3.Cross(d, s1);
            Vector3[] o = { s1 + s2, s1 - s2, -s1 - s2, -s1 + s2 };
            for (int k = 0; k < 4; k++)
            {
                var o0 = o[k] * r; var o1 = o[(k + 1) % 4] * r; var n = (o[k] + o[(k + 1) % 4]).normalized;
                int bi = V.Count;
                V.Add(a + o0); V.Add(a + o1); V.Add(b + o1); V.Add(b + o0);
                for (int q = 0; q < 4; q++) N.Add(n);
                T.Add(bi); T.Add(bi + 2); T.Add(bi + 1); T.Add(bi); T.Add(bi + 3); T.Add(bi + 2);
            }
        }

        /// <summary>圆盘：中心 uv.y = 1、边缘 0（配合发光着色器的渐隐 = 中间亮边缘暗的光斑）。</summary>
        static Mesh Disc(float r)
        {
            int seg = 32; var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new(0.5f, 1) }; var col = new List<Color> { Color.white }; var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2f; v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); uv.Add(new Vector2(0.5f, 0)); col.Add(Color.white); }
            for (int i = 1; i <= seg; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        Material SpotMat() { var m = new Material(glow); m.SetColor("_Color", new Color(1f, .93f, .78f, 0.18f)); m.SetFloat("_Fade", 1f); cones.Add(m); return m; }
        readonly List<Material> cones = new();
        readonly Dictionary<Material, Color> coneBase = new();
        readonly List<Material> spots = new();
        float spotK = 1f;
        /// <summary>光柱强弱：菜单时调暗（不挡海报板），打比赛时全亮。</summary>
        public float SpotTarget = 1f;

        static Mesh Cone(float r0, float r1, float len)
        {
            int seg = 16; var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r0); uv.Add(new Vector2(i / (float)seg, 1)); col.Add(Color.white);
                v.Add(d * r1 + Vector3.down * len); uv.Add(new Vector2(i / (float)seg, 0)); col.Add(Color.white);
            }
            for (int i = 0; i < seg; i++) { int b = i * 2; t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        // ───────── 大屏（海报风：墨黑底 + 朱红边，两条斜切名字条、斜血条、金色怒气条、胜场菱形） ─────────
        void BuildScreen(Material dark)
        {
            screen = new GameObject("大屏").transform; screen.SetParent(transform, false); screen.localPosition = new Vector3(0, 0.33f, 0);
            foreach (float x in new[] { -0.1f, 0.1f }) Box(screen, new Vector3(x, 0.075f, 0.004f), new Vector3(0.0015f, 0.075f, 0.0015f), dark);   // 吊杆
            face = new GameObject("屏面").transform; face.SetParent(screen, false);
            SBUI.Slab(face, new Vector3(0.002f, -0.002f, 0), 0.28f, 0.086f, 0.01f, SBUI.Red, 0.003f, 0.08f, 1);
            SBUI.Slab(face, Vector3.zero, 0.27f, 0.076f, -0.006f, SBUI.Ink, 0.002f, 0.06f, 2);
            var grey = new Color(.2f, .2f, .22f);
            for (int s = 0; s < 2; s++)
            {
                float sg = s == 0 ? -1 : 1;
                var nt = SBUI.Slab(face, new Vector3(sg * 0.075f, 0.022f, 0), 0.104f, 0.026f, sg * -0.01f, s == 0 ? SBUI.Red : SBUI.Cobalt, 0.001f, 0.12f, 3 + s);
                nt.localRotation = Quaternion.Euler(0, 0, sg * 2.5f);
                SBUI.Slab(face, new Vector3(sg * 0.077f, 0.019f, 0), 0.104f, 0.026f, sg * -0.01f, SBUI.Cream, 0.0015f, 0.12f, 9 + s).localRotation = Quaternion.Euler(0, 0, -sg * 1.5f);
                var n = SBUI.Text(face, "", 0.016f, SBUI.Cream, SBUI.F.Title, TextAnchor.MiddleCenter, new Vector3(sg * 0.075f, 0.021f, -0.0012f));
                if (s == 0) nameL = n; else nameR = n;
                // 血条：灰底 + 填充（锚在外侧，往中间缩）
                SBUI.Slab(face, new Vector3(sg * 0.07f, -0.006f, 0), 0.12f, 0.013f, sg * -0.006f, grey, 0.001f, 0.15f, 20 + s);
                var piv = new GameObject("血").transform; piv.SetParent(face, false); piv.localPosition = new Vector3(sg * 0.13f, -0.006f, -0.0005f);
                var fill = SBUI.Slab(piv, new Vector3(-sg * 0.06f, 0, 0), 0.12f, 0.013f, sg * -0.006f, SBUI.Cream, 0, 0.15f, 20 + s);
                // 怒气条
                SBUI.Slab(face, new Vector3(sg * 0.07f, -0.021f, 0), 0.12f, 0.005f, sg * -0.003f, grey, 0.001f);
                var mp = new GameObject("怒气").transform; mp.SetParent(face, false); mp.localPosition = new Vector3(sg * 0.13f, -0.021f, -0.0005f);
                var mf = SBUI.Slab(mp, new Vector3(-sg * 0.06f, 0, 0), 0.12f, 0.005f, sg * -0.003f, SBUI.Gold, 0);
                if (s == 0) { barL = piv; fillL = fill.GetComponent<Renderer>(); mL = mp; mfL = mf.GetComponent<Renderer>(); }
                else { barR = piv; fillR = fill.GetComponent<Renderer>(); mR = mp; mfR = mf.GetComponent<Renderer>(); }
                for (int k = 0; k < 2; k++)
                {
                    var d = SBUI.Slab(face, new Vector3(sg * (0.016f + k * 0.012f), -0.031f, -0.0005f), 0.008f, 0.008f, 0, grey, 0);
                    d.localRotation = Quaternion.Euler(0, 0, 45);
                    (s == 0 ? winL : winR)[k] = d.GetComponent<Renderer>();
                }
            }
            roundTxt = SBUI.Text(face, "", 0.011f, SBUI.Gold, SBUI.F.Title, TextAnchor.MiddleCenter, new Vector3(0, 0.021f, -0.0012f));
            // 屏框：黑漆背板 + 一圈铜边（从背后、侧面看也是一个实物）
            var lacq = new Material(lit) { color = new Color(.07f, .06f, .06f) }; lacq.SetFloat("_Smoothness", 0.7f);
            var brs = new Material(lit) { color = new Color(.72f, .54f, .27f) }; brs.SetFloat("_Metallic", 0.7f); brs.SetFloat("_Smoothness", 0.42f);   // 做旧黄铜
            Box(face, new Vector3(0, 0, 0.009f), new Vector3(0.296f, 0.1f, 0.01f), lacq);
            foreach (var (pos, sz) in new[] { (new Vector3(0, 0.05f, 0.004f), new Vector3(0.3f, 0.004f, 0.006f)), (new Vector3(0, -0.05f, 0.004f), new Vector3(0.3f, 0.004f, 0.006f)),
                                               (new Vector3(-0.149f, 0, 0.004f), new Vector3(0.004f, 0.1f, 0.006f)), (new Vector3(0.149f, 0, 0.004f), new Vector3(0.004f, 0.1f, 0.006f)) })
                Box(face, pos, sz, brs);
            screen.gameObject.SetActive(false);
        }

        Transform face; Renderer fillL, fillR, mfL, mfR; readonly Renderer[] winL = new Renderer[2], winR = new Renderer[2];

        /// <summary>大屏放下来（打比赛）/ 收起来（菜单、教学、结算）。</summary>
        public void ShowScreen(bool on)
        {
            if (screen.gameObject.activeSelf == on) return;
            screen.gameObject.SetActive(on);
            if (on) { var p = face.GetComponent<SBPop>() ?? face.gameObject.AddComponent<SBPop>(); p.slideFrom = Vector3.up * 0.15f; p.from = 1.3f; p.dur = 0.4f; p.enabled = true; }
        }

        /// <summary>每帧：大屏和中间大字转向玩家；观众激动程度回落；闪光灯；铁丝网抖动。</summary>
        public void Tick(Vector3 headPos, float dt)
        {
            excite = Mathf.MoveTowards(excite, 0.15f, dt * 0.35f);
            TickRail(dt);
            Shader.SetGlobalFloat(ExciteId, excite);
            if (!Mathf.Approximately(spotK, SpotTarget))
            {
                spotK = Mathf.MoveTowards(spotK, SpotTarget, dt * 1.5f);
                foreach (var m in spots) m.SetColor("_Color", new Color(1f, .9f, .75f, 0.3f * Mathf.Lerp(0.7f, 1f, spotK)));
                float ck = Mathf.Clamp01((spotK - 0.25f) / 0.75f);
                foreach (var m in cones) { if (!coneBase.TryGetValue(m, out var cb)) { cb = m.GetColor("_Color"); coneBase[m] = cb; } m.SetColor("_Color", new Color(cb.r, cb.g, cb.b, cb.a * ck)); }
            }
            var d = screen.position - headPos; d.y = 0; if (d.sqrMagnitude > 1e-5f) screen.rotation = Quaternion.LookRotation(d);   // 大屏 -z 那面（字和血条在这面）朝向玩家
            foreach (var b in billboardsArena) if (b) b.rotation = Quaternion.LookRotation(b.position - headPos);
            for (int i = 0; i < flashes.Count; i++)
            {
                flashT[i] -= dt;
                if (flashT[i] <= 0 && Random.value < dt * (0.15f + excite * 6f)) flashT[i] = 0.06f;
                flashes[i].gameObject.SetActive(flashT[i] > 0);
                if (flashT[i] > 0) flashes[i].rotation = Quaternion.LookRotation(flashes[i].position - headPos);
            }
        }

        public void Excite(float amount) { excite = Mathf.Clamp01(excite + amount); }

        public void Score(string l, string r, float hl, float hr, string round, int wl, int wr, float ml, float mr)
        {
            hl = Mathf.Clamp01(hl); hr = Mathf.Clamp01(hr); ml = Mathf.Clamp01(ml); mr = Mathf.Clamp01(mr);
            if (nameL.text != l) { nameL.text = l; SBUI.Refont(nameL); SBUI.FitOne(nameL); }
            if (nameR.text != r) { nameR.text = r; SBUI.Refont(nameR); SBUI.FitOne(nameR); }
            if (roundTxt.text != round) { roundTxt.text = round; SBUI.Refont(roundTxt); }
            barL.localScale = new Vector3(Mathf.Max(hl, 0.0001f), 1, 1); barR.localScale = new Vector3(Mathf.Max(hr, 0.0001f), 1, 1);
            fillL.material.color = hl < 0.3f ? Color.Lerp(SBUI.Red, SBUI.Cream, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : SBUI.Cream;
            fillR.material.color = hr < 0.3f ? Color.Lerp(SBUI.Red, SBUI.Cream, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : SBUI.Cream;
            mL.localScale = new Vector3(Mathf.Max(ml, 0.0001f), 1, 1); mR.localScale = new Vector3(Mathf.Max(mr, 0.0001f), 1, 1);
            mfL.material.color = ml >= 1f ? Color.Lerp(SBUI.Gold, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f)) : SBUI.Gold;
            mfR.material.color = mr >= 1f ? Color.Lerp(SBUI.Gold, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f)) : SBUI.Gold;
            var grey = new Color(.2f, .2f, .22f);
            for (int k = 0; k < 2; k++) { winL[k].material.color = k < wl ? SBUI.Gold : grey; winR[k].material.color = k < wr ? SBUI.Gold : grey; }
        }

        /// <summary>把位置夹回笼内（留出 margin），返回夹过的位置（本地 y 不变）。</summary>
        public Vector3 Clamp(Vector3 world, float margin)
        {
            var l = transform.InverseTransformPoint(world);
            float lim = Apothem - margin;
            for (int i = 0; i < 8; i++)
            {
                float ang = (i + 1) * Mathf.PI / 4f; var n = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
                float dd = Vector3.Dot(new Vector3(l.x, 0, l.z), n);
                if (dd > lim) l -= n * (dd - lim);
            }
            return transform.TransformPoint(l);
        }

        /// <summary>撞网：哪一面网抖一下。</summary>
        /// <summary>撞网：哪一面网被撞，在撞击点给网一个往外的冲量（speed = 撞过去的速度）。</summary>
        public void Shake(Vector3 world, float speed = 0.6f)
        {
            var l = transform.InverseTransformPoint(world);
            float ang = Mathf.Atan2(l.x, l.z); int i = Mathf.RoundToInt(ang / (Mathf.PI / 4f));
            i = ((i % 8) + 8) % 8; var n = nets[i]; if (!n) return;
            var ln = n.transform.InverseTransformPoint(world); ln.y = Mathf.Clamp(ln.y, 0.01f, n.h - 0.01f);
            n.Hit(ln, Mathf.Clamp(speed, 0.3f, 1.6f) * 0.85f, 0.028f);
            RailHit(world, Mathf.Clamp(speed, 0.3f, 1.6f) * 0.36f);   // 弹性主要在顶上护栏：被撞的那一边往外一鼓再弹回来
        }

        /// <summary>顶网：world = 顶的点，radius = 顶的东西的粗细；从里面顶往外凸，从外面戳往里凹。</summary>
        public void NetTouch(Vector3 world, float radius, float spread = 0.018f)
        {
            foreach (var n in nets)
            {
                if (!n) continue;
                var l = n.transform.InverseTransformPoint(world);
                if (!n.InFace(l, 0.004f) || Mathf.Abs(l.z) > 0.05f) continue;
                if (l.z < 0) { float d = l.z + radius; if (d > 0) n.Press(new Vector3(l.x, l.y, Mathf.Min(d, 0.025f)), spread); }
                else { float d = l.z - radius; if (d < 0) n.Press(new Vector3(l.x, l.y, Mathf.Max(d, -0.025f)), spread); }
            }
        }

        // ───────── 笼门（玩家右手边那面）：黑漆门框 + 两个铜合页 + 一个门闩 ─────────
        void BuildDoor(Material lacq, Material brass)
        {
            float mid = 3 * Mathf.PI / 4f; var dir = new Vector3(Mathf.Sin(mid), 0, Mathf.Cos(mid));
            var door = new GameObject("笼门").transform; door.SetParent(transform, false);
            door.localPosition = dir * (Apothem + 0.0025f); door.localRotation = Quaternion.LookRotation(dir); DoorT = door;
            float dw = 0.085f, dh = fenceH - 0.004f;
            foreach (float x in new[] { -dw / 2f, dw / 2f }) Box(door, new Vector3(x, dh / 2f + 0.004f, 0), new Vector3(0.004f, dh, 0.004f), lacq);
            Box(door, new Vector3(0, dh * 0.5f + 0.004f, 0), new Vector3(dw, 0.003f, 0.003f), lacq);   // 中间横档
            foreach (float y in new[] { 0.018f, dh - 0.012f })
            {
                Box(door, new Vector3(-dw / 2f - 0.003f, y, 0.001f), new Vector3(0.006f, 0.01f, 0.003f), brass);      // 合页
                Cyl(door, new Vector3(-dw / 2f - 0.006f, y, 0.001f), new Vector3(0.0035f, 0.006f, 0.0035f), brass);
            }
            Box(door, new Vector3(dw / 2f + 0.004f, dh * 0.55f, 0.002f), new Vector3(0.012f, 0.007f, 0.004f), brass);   // 门闩
            Box(door, new Vector3(dw / 2f + 0.011f, dh * 0.55f, 0.002f), new Vector3(0.004f, 0.012f, 0.005f), lacq);
        }

        // ───────── 门外的台阶：独立的三级台阶（实心台阶体 + 两侧斜梁 + 防滑胶垫 + 铜踏口 + 两侧铜扶手），从桌面上到台子 ─────────
        void BuildSteps(Material lacq, Material brass)
        {
            float mid = 3 * Mathf.PI / 4f; var dir = new Vector3(Mathf.Sin(mid), 0, Mathf.Cos(mid));
            float a0 = (radius + 0.02f) * Mathf.Cos(Mathf.PI / 8f);
            var M0 = Matrix4x4.TRS(dir * a0, Quaternion.LookRotation(dir), Vector3.one);   // 本地：z 朝外、x 横向、y 向上，原点在台子边上台面高度
            float w = 0.056f, d = 0.0135f, rise = 0.035f / 4f;
            var body = new SBGeo { M = M0 }; var rub = new SBGeo { M = M0 }; var brs = new SBGeo { M = M0 }; var red = new SBGeo { M = M0 };
            for (int j = 0; j < 3; j++)
            {
                float top = -rise * (j + 1), z0 = j * d, z1 = (j + 1) * d;
                body.Box(new Vector3(0, (top - 0.035f) / 2f, (z0 + z1) / 2f), new Vector3(w, top + 0.035f, d));                         // 台阶体
                red.Box(new Vector3(0, top - rise * 0.5f, z1 - 0.0004f), new Vector3(w - 0.006f, 0.0016f, 0.0008f));                     // 踢面上一道朱红线
                rub.Box(new Vector3(0, top + 0.0004f, (z0 + z1) / 2f - 0.0008f), new Vector3(w - 0.008f, 0.0008f, d - 0.0035f));        // 防滑胶垫
                brs.Cyl(new Vector3(-w / 2f + 0.001f, top - 0.0004f, z1 - 0.0006f), new Vector3(w / 2f - 0.001f, top - 0.0004f, z1 - 0.0006f), 0.0009f, 8);   // 铜踏口
            }
            // 两侧斜梁：顺着台阶斜下去的黑漆板，上沿一条铜边
            foreach (int sx in new[] { -1, 1 })
            {
                float x = sx * (w / 2f + 0.0016f);
                var from = new Vector3(x, -0.004f, 0.001f); var to = new Vector3(x, -0.035f + 0.004f, 3 * d);
                var mid3 = (from + to) / 2f; var len = (to - from).magnitude;
                body.Box(mid3 + Vector3.down * 0.004f, new Vector3(0.0028f, 0.012f, len), Quaternion.LookRotation(to - from));
                brs.Cyl(from + Vector3.up * 0.0022f, to + Vector3.up * 0.0022f, 0.0008f, 8);
                body.Box(new Vector3(x, -0.0175f - 0.0035f, 3 * d - 0.003f), new Vector3(0.0028f, 0.02f, 0.006f));
                // 扶手：上下两根立柱 + 斜扶手 + 顶上铜珠
                Vector3 pb = new(sx * (w / 2f + 0.0045f), -0.035f + 0.009f, 3 * d - 0.003f), pt = new(sx * (w / 2f + 0.0045f), 0f, 0.002f);
                float hh = 0.032f;
                brs.Cyl(pb + Vector3.down * 0.009f, pb + Vector3.up * hh, 0.0011f, 10);
                brs.Cyl(pt, pt + Vector3.up * hh, 0.0011f, 10);
                brs.Cyl(pb + Vector3.up * hh, pt + Vector3.up * hh, 0.0012f, 10);
                brs.Cyl(pb + Vector3.up * hh * 0.5f, pt + Vector3.up * hh * 0.5f, 0.0008f, 8);
                foreach (var p in new[] { pb, pt })
                    brs.Lathe(p + Vector3.up * hh, new[] { new Vector2(0.0012f, 0f), new Vector2(0.0022f, 0.0012f), new Vector2(0.0022f, 0.0024f), new Vector2(0f, 0.0036f) }, 12);
            }
            body.Make(transform, "台阶", lacq);
            rub.Make(transform, "台阶胶垫", M(new Color(.13f, .13f, .14f)));
            red.Make(transform, "台阶红线", M(new Color(.62f, .1f, .08f)));
            brs.Make(transform, "台阶铜件", brass);
        }

        // ───────── 小工具 ─────────
        Material M(Color c) => new Material(lit) { color = c };
        Transform Box(Transform p, Vector3 pos, Vector3 s, Material m) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(p, false); g.transform.localPosition = pos; g.transform.localScale = s; g.GetComponent<Renderer>().material = m; return g.transform; }
        Transform Cyl(Transform p, Vector3 pos, Vector3 s, Material m) { var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(p, false); g.transform.localPosition = pos; g.transform.localScale = s; g.GetComponent<Renderer>().material = m; return g.transform; }
        void Mesh(Mesh mesh, Material m, string name) { var g = new GameObject(name); g.transform.SetParent(transform, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().material = m; }

        /// <summary>八角形：top 高度的台面多边形；solid 时再加一圈侧面到 bottom。顶点按外接圆半径 R，顶点在 22.5° + k·45°（平边朝前后左右）。</summary>
        static Mesh Octagon(float R, float bottom, float top, bool solid)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>();
            v.Add(new Vector3(0, top, 0)); n.Add(Vector3.up);
            for (int i = 0; i < 8; i++) { float a = (i + 0.5f) * Mathf.PI / 4f; v.Add(new Vector3(Mathf.Sin(a) * R, top, Mathf.Cos(a) * R)); n.Add(Vector3.up); }
            for (int i = 0; i < 8; i++) { t.Add(0); t.Add(1 + i); t.Add(1 + (i + 1) % 8); }
            if (solid)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f, am = (i + 1f) * Mathf.PI / 4f;
                    var nn = new Vector3(Mathf.Sin(am), 0, Mathf.Cos(am));
                    int b = v.Count;
                    v.Add(new Vector3(Mathf.Sin(a0) * R, top, Mathf.Cos(a0) * R)); v.Add(new Vector3(Mathf.Sin(a1) * R, top, Mathf.Cos(a1) * R));
                    v.Add(new Vector3(Mathf.Sin(a1) * R, bottom, Mathf.Cos(a1) * R)); v.Add(new Vector3(Mathf.Sin(a0) * R, bottom, Mathf.Cos(a0) * R));
                    for (int k = 0; k < 4; k++) n.Add(nn);
                    t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
                }
            }
            var uv = new List<Vector2>(); const float S = 0.52f;
            foreach (var p in v) uv.Add(new Vector2(p.x / S + 0.5f, p.z / S + 0.5f));
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }
}
