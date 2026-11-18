using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>整局流程：标题 → 练习场（第一次玩强制，之后可选）→ 选木偶 → VS → 回合（先 KO 对手两回合赢）→ 结算 → 下一位（共 5 位）→ 擂台之王。
    /// - 标题：海报板放下来，笼子里两只木偶在比划；网上挂着“开打 / 练习 / 中文·EN”三块木牌，手掌移过去捏一下；
    /// - 练习场：对着木人桩，一步一招（走位、左拳、右拳、重拳、踢、防守、下蹲、跳、必杀），做对了才进下一步，海报板上有手势图；
    /// - 选木偶：六个木偶站在笼里，手掌移到谁头上谁就跳一下、海报板换成它的海报卡、能力和必杀，捏一下选中；
    /// - 比赛：你的木偶吊在手下，五根线从指尖连到木偶；对手吊在十字木架下，被 KO 时线断；
    /// - 手不见了就暂停（木偶停住，场中出“暂停”牌）。
    /// 编辑器里 autoPilot = true：自动走完整套流程（截图验证用）。</summary>
    public class SBMatch : MonoBehaviour
    {
        public SBHands hands;
        public Transform head;
        public Material litTemplate, unlitTemplate, glowMat, fenceMat, crowdMat, textMat, shadowMat, paperMat, ledMat, outlineMat;
        public bool autoPilot;
        public const float PuppetH = 0.16f;
        const int Bouts = 5;

        SBArena arena; SBStage stage;
        SBPuppet me, cpu;
        SBRival ai; SBAutoPilot ap; SBMusic music;
        readonly SBGestures gest = new();
        readonly LineRenderer[] myLines = new LineRenderer[5], cpuLines = new LineRenderer[5];
        Transform cpuBar;
        readonly List<SBPuppet> picks = new();
        bool placed, wasPinch, iWon, handSeen;
        int useRight = -1, hover = -1, ladder, winsMe, winsCpu, round, ultUses;
        SBKit myKit;
        float lostT, showT;
        // 走位用相对位移：手放哪都行（手肘撑在桌上也行），手移动的距离 × Gain = 木偶走的距离
        const float Gain = 1.6f; bool anchored; Vector3 palmA, pupA;
        // 弹网：顶在网上蓄力（0..1），松开被弹出去
        const float NetStretch = 0.04f; float netCharge, netHold; Vector3 netN;
        SBPilot debugPilot;   // 编辑器测试：指定对手是谁（换场也不变）
        int handPref = 2;   // 操纵手：1 左手 2 右手（默认右手；设置里切换，存在本机）
        bool userPaused; float minGap = 9f;   // 调试：两个木偶身子表面之间出现过的最小间隙（负 = 叠在一起了）
        const float BarH = 0.44f;   // 对面提线木架离台面多高：在记分屏上方，名牌不压任何东西
        Transform belt, podium; Vector3 beltHome; Quaternion beltRot; float beltT; float champT, champConf, champCheer; bool champTickets;
        Material myLineMat;
        enum St { Title, Tutorial, Lobby, Select, Intro, RoundStart, Fight, KO, Result, Champion, Table, Settings }
        // 擂台榜：先定对手（匹配 / 点名），再选自己的木偶
        SBPilot oppPilot; SBKit oppKit; int cur = 1, matchTarget, rankBefore, rankAfter; float matchT, matchTick; bool lobbyAp;
        float placeT, awayT, stillT; Vector3 lastPalm;
        readonly List<SBRival> showAIs = new();
        // 观战（精彩对局）：两位操偶师真打一场，你坐在其中一位这边看他怎么操纵；随时可以“换边”到另一位那边
        bool watching, watchRig, watchOT; float watchRoundT; int watchSide = 1, watchCur; SBRival ai2; Transform meBar; SBPilotHand pilotHand; SBBout watchBout; float shownSayT; string champWho, champSub;
        readonly List<SBBout> bouts = new();
        // 闯关（夺金腰带之路）：选一只木偶，把另外五只一只一只打过去，最后一战是和自己一模一样的木偶；全赢了领金腰带。这个模式里血更厚
        bool guardWas, showTips = true;   // 对战中的玩法小抄：默认开着，戳“收起”木牌关掉（记在本机）
        // 闯关前五战里记下你的打法：出什么招、多快、爱不爱防守和躲闪、常用哪几下连着打——最后那只“另一个你”照着打
        int hJab, hHeavy, hKick, hDodge, hUlt; float hGuardT, hFightT; readonly List<(SBGestures.Act a, float t)> hSeq = new();
        void HabitReset() { hJab = hHeavy = hKick = hDodge = hUlt = 0; hGuardT = hFightT = 0; hSeq.Clear(); mirrorPilot = null; }
        void Habit(SBGestures.Act a)
        {
            if (a == SBGestures.Act.Jump || a == SBGestures.Act.Duck) { hDodge++; return; }
            if (SBGestures.IsKick(a)) hKick++; else if (SBGestures.IsHeavy(a)) hHeavy++; else hJab++;
            hSeq.Add((a, Time.time)); if (hSeq.Count > 400) hSeq.RemoveAt(0);
        }
        SBPilot BuildMirror()
        {
            var b = SBPilot.Get("fast"); int n = hJab + hHeavy + hKick;
            if (n < 10 || hFightT < 10f) return b.As("mirror", "另一个你", "Your Double", "和你一模一样，连出招的习惯都像", "The same puppet as yours, down to its habits", 5, 0.8f);
            float hv = hHeavy / (float)n, kk = hKick / (float)n, rate = n / hFightT, gd = hGuardT / hFightT, dg = hDodge / hFightT;
            var tz = new List<string>(); var te = new List<string>();
            if (hv > 0.3f) { tz.Add("爱蓄重招"); te.Add("loves heavies"); }
            if (kk > 0.4f) { tz.Add("爱出腿"); te.Add("kicks a lot"); }
            if (gd > 0.2f) { tz.Add("爱防守"); te.Add("guards a lot"); } else if (gd < 0.04f) { tz.Add("几乎不防守"); te.Add("rarely guards"); }
            if (rate > 1.2f) { tz.Add("出手很快"); te.Add("attacks fast"); } else if (rate < 0.45f) { tz.Add("不轻易出手"); te.Add("waits patiently"); }
            if (dg > 0.12f) { tz.Add("爱躲闪"); te.Add("dodges a lot"); }
            if (tz.Count == 0) { tz.Add("打法很均衡"); te.Add("balanced style"); }
            while (tz.Count > 2) { tz.RemoveAt(tz.Count - 1); te.RemoveAt(te.Count - 1); }
            var p = b.As("mirror", "另一个你", "Your Double", "学了你的打法：\n" + string.Join("\n", tz), "Learned your style:\n" + string.Join("\n", te), 5, 0.8f);   // 一条习惯一行，不在词中间断开
            p.heavy = Mathf.Clamp(hv, 0.03f, 0.6f); p.kick = Mathf.Clamp(kk, 0.03f, 0.6f);
            float gap = Mathf.Clamp(1f / Mathf.Max(rate, 0.2f), 0.45f, 2.2f); p.gapMin = gap * 0.75f; p.gapMax = gap * 1.3f;
            p.aggression = Mathf.Clamp01(0.25f + rate * 0.45f);
            p.guard = Mathf.Clamp(gd * 2.2f, 0.03f, 0.8f); p.dodge = Mathf.Clamp(0.15f + dg * 4f, 0.15f, 0.9f); p.ult = hUlt > 0 ? 0.9f : 0.3f;
            // 你最常连着打的三下，就是它的连招
            var cnt = new Dictionary<string, (int n, SBGestures.Act[] c)>();
            for (int i = 0; i + 2 < hSeq.Count; i++)
            {
                if (hSeq[i + 2].t - hSeq[i].t > 2.2f) continue;
                var c = new[] { hSeq[i].a, hSeq[i + 1].a, hSeq[i + 2].a }; string k = $"{c[0]}{c[1]}{c[2]}";
                cnt[k] = (cnt.TryGetValue(k, out var v) ? v.n + 1 : 1, c);
            }
            var top = new List<(int n, SBGestures.Act[] c)>(cnt.Values); top.Sort((x, y) => y.n.CompareTo(x.n));
            if (top.Count > 0)
            {
                var cs = new List<SBGestures.Act[]>(); for (int i = 0; i < Mathf.Min(3, top.Count); i++) cs.Add(top[i].c);
                p.combos = cs.ToArray(); p.combo = Mathf.Clamp(0.3f + top[0].n * 0.05f, 0.3f, 0.85f);
            }
            Debug.Log($"[SB] 另一个你：出招 {n} 次 / {hFightT:0} 秒，重招 {hv:0.00} 腿 {kk:0.00} 防守 {gd:0.00} 躲闪 {dg:0.00}/秒 连招 {top.Count} 种 → {p.habitZh.Replace("\n", " ")}");
            return p;
        }
        bool arcade, wantArcade, mirrorSaid, champSaid; int arcStage; readonly List<SBKit> arcKits = new(); SBPilot mirrorPilot;
        static readonly string[] ArcPilots = { "mash", "slow", "hot", "zhou", "fox" };   // 前五战的对手由这几位操偶师提线，一位比一位会打
        bool ArcFinal => arcade && arcKits.Count > 0 && arcStage == arcKits.Count - 1;
        // 台词：到点才说的那几句
        readonly List<(float at, System.Action go)> sayQ = new();
        St st = St.Title; float stT, slowT;

        /// <summary>编辑器调试：直接跳到某个画面 / 按下某块木牌（截图验证用，真机不生效）。</summary>
        public void DebugGo(string what)
        {
            if (!Application.isEditor) return;
            if (what.StartsWith("pilot:")) { foreach (var pl in SBPilot.All) if (pl.id == what.Substring(6)) { ai.SetPilot(pl); debugPilot = pl; minGap = 9f; } return; }
            if (what == "heavy" && ap) { ap.heavyMode = !ap.heavyMode; return; }
            if (what.StartsWith("slip:") && ai && ai.Pilot != null) { ai.SetPilot(ai.Pilot.Dodgy(float.Parse(what.Substring(5))), 0); return; }
            if (what == "champ" && ai && ai.Pilot != null) { ai.SetPilot(ai.Pilot.Champion(), 0); return; }   // 编辑器测试：把当前对手换成榜首的打法
            if (what == "intro" && myKit != null) { BeginIntro(); return; }
            if (what.StartsWith("kit:")) { myKit = SBKit.Get(what.Substring(4)); return; }
            if (what.StartsWith("sim:")) { var q = what.Split(':'); SimStart(q[1], q[2], q.Length > 3 ? q[3] : "fox"); return; }
            if (what.StartsWith("simall:"))
            {   // simall:<操偶师>:<每组秒数>：六只木偶 15 组两两对打自动轮一遍，结果一行一组写到 /tmp/sb_sim.txt，最后一行 DONE
                var q = what.Split(':'); simPilot = q[1]; simDur = float.Parse(q[2]); simQ = new List<(string, string)>();
                for (int i = 0; i < SBKit.All.Length; i++) for (int j = i + 1; j < SBKit.All.Length; j++) simQ.Add((SBKit.All[i].id, SBKit.All[j].id));
                simI = -1; System.IO.File.WriteAllText("/tmp/sb_sim.txt", ""); SimNext(); return;
            }
            if (what == "lobby") { BeginLobby(); return; }
            if (what == "board" && st == St.Lobby) { LobbyBoard(); return; }
            if (what == "search" && st == St.Lobby) { LobbySearch(); return; }
            if (what == "resetladder") { SBLadder.Reset(); return; }
            if (what == "pushnet" && ap && arena) { ap.forcePalm = arena.transform.TransformPoint(new Vector3(-0.5f, 0.18f, 0f)); ap.useForce = true; return; }
            if (what == "pullback" && ap) { ap.useForce = false; return; }
            if (what == "kome" && me) { me.TakeHit(-me.Root.forward, 99f); return; }
            if (what == "arcade") { BeginArcade(); return; }
            if (what.StartsWith("arc:") && arcade && arcKits.Count > 0) { arcStage = Mathf.Clamp(int.Parse(what.Substring(4)), 0, arcKits.Count - 1); ArcadeOpp(); BeginIntro(); return; }
            if (what == "champion" && me) { iWon = true; EnterChampion(); }
            else if (what == "select") BeginSelect();
            else if (what == "ko" && cpu) cpu.TakeHit(-cpu.Root.forward, 99f);
            else stage.Press(what);
        }
        public Transform CpuRoot => cpu ? cpu.Root : null;
        public Transform ArenaT => arena ? arena.transform : null;
        public string SimState => simA == null ? "" : $"{simA}：伤害 {simDA:0} 比 {simDB:0}，击倒 {simKA} 比 {simKB}";
        public string DebugState => (arcade ? $"[闯关 第{arcStage + 1}战] " : "") + (watching ? $"[观战 近侧={(watchSide > 0 ? watchBout.a.zh : watchBout.b.zh)} 手最高{handTop:0.000} 手与木偶最近{handGap * 100f:0.0}cm] " : "") + (ai && ai.Pilot != null && cpu ? ai.Pilot.zh + " " : "") + $"{st}/{lb} step={tutStep} done={tutDone} ladder={ladder} {winsMe}:{winsCpu} t={Time.time:0.0}" + (me && cpu ? $" 间距{Vector3.Distance(me.Root.position, cpu.Root.position) * 100f:0.0}cm(身子半径和{(me.BodyR + cpu.BodyR) * 100f:0.0}) 网蓄力{netCharge:0.00} 最小间隙{minGap * 100f:0.0}cm" : "");
        static bool TutDone => PlayerPrefs.GetInt("sb_tut2", 0) == 1;   // 规则加了反击 / 破防：换个标记，老玩家也再走一遍练习场（可以跳过）

        // ───────── 练习场的九步 ─────────
        struct Step { public string zh, en, izh, ien, nzh, nen; public int need; }
        static readonly Step[] Tut =
        {
            new() { zh = "走位", en = "MOVE", izh = "平移手掌，木偶就跟着走。\n走到木人桩跟前。", ien = "Slide your palm.\nThe puppet follows.\nWalk up to the dummy.", need = 1, nzh = "默认用右手，设置里可换左手 · 手肘可以撑在桌上", nen = "Right hand by default (change in Settings). Rest your elbow" },
            new() { zh = "左拳", en = "LEFT JAB", izh = "拇指往下按一下，\n再松开。", ien = "Tap your THUMB down\nand let go.", need = 3, nzh = "打中木人桩 3 次", nen = "Land 3 hits" },
            new() { zh = "右拳", en = "RIGHT JAB", izh = "食指勾一下，\n再松开。", ien = "Flick your INDEX finger.", need = 3, nzh = "打中木人桩 3 次", nen = "Land 3 hits" },
            new() { zh = "重拳", en = "HEAVY", izh = "任意一根手指按住别松 = 蓄力，\n松开 = 重招。蓄得越久越疼。", ien = "HOLD any finger to charge.\nLet go to SMASH.", need = 1, nzh = "拇指 = 左勾拳  食指 = 右重拳  后三指 = 回旋踢", nen = "Thumb: hook · Index: straight · Last three: spin kick" },
            new() { zh = "踢腿", en = "KICK", izh = "中指、无名指、小指\n一起收一下。", ien = "Curl your last THREE\nfingers together.", need = 2, nzh = "腿比手长，远一点也够得着", nen = "Kicks reach further than punches" },
            new() { zh = "防守", en = "GUARD", izh = "木人桩要打你了！\n五指握拳 = 抱头挡住。", ien = "It's swinging at you!\nMake a FIST to block.", need = 2, nzh = "闪红 = 要出手 · 挡太多拳套烧红会被打穿，松拳恢复", nen = "Red flash = incoming · Block too much and your guard breaks" },
            new() { zh = "下蹲", en = "DUCK", izh = "它挥拳的时候，\n手往下一沉 = 蹲下躲开。", ien = "When it punches,\ndrop your hand fast.", need = 1, nzh = "躲开后拳套发金光，马上出手 = 反击", nen = "Dodged? Gloves glow gold: strike NOW to counter" },
            new() { zh = "跳", en = "JUMP", izh = "它扫腿的时候，\n手往上一提 = 跳过去。", ien = "When it kicks,\nlift your hand fast.", need = 1, nzh = "跳起来躲腿，躲开后同样可以反击", nen = "Jump over kicks, then counter" },
            new() { zh = "必杀", en = "ULTIMATE", izh = "怒气满了，提线变成金色。\n拇指食指捏住，\n整只手往下一拽！", ien = "Meter full: strings turn gold.\nPinch thumb + index,\nthen yank your hand DOWN!", need = 1, nzh = "每个木偶的必杀都不一样", nen = "Every puppet has its own ult" },
        };
        static readonly (string zh, string en)[] Nice = { ("好！", "NICE!"), ("漂亮！", "CLEAN!"), ("就是这样！", "THAT'S IT!") };
        bool apGuard; int tutStep, tutDone; float tutWait, tutAtkT, tutRefill, apT;

        void Start()
        {
            SBPuppet.paperMat = paperMat; SBPuppet.glowMat = glowMat;
            SBLines.Preload();
            if (!Application.isEditor) autoPilot = false;   // 真机永远不开自动测试
            else
            {
                // 编辑器里：把运行时异常记到 /tmp/sb_err.txt（自动测试排查用）
                System.IO.File.WriteAllText("/tmp/sb_err.txt", "");
                Application.logMessageReceived += (c, stk, t) => { if (t == LogType.Exception || t == LogType.Error) System.IO.File.AppendAllText("/tmp/sb_err.txt", c + "\n" + stk + "\n----\n"); };
            }
            if (!head && Camera.main) head = Camera.main.transform;
            var ts = textMat ? textMat.shader : Shader.Find("StringBrawl/Text");
            SBUI.Init(ts, unlitTemplate);
            var all = new System.Text.StringBuilder("0123456789/·:：！!？?，。、.,=+- ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'");
            foreach (var sp in Tut) all.Append(sp.zh + sp.izh + sp.nzh);
            foreach (var k in SBKit.All) all.Append(k.zh + k.blurbZh + k.weakZh + k.ultZh);
            all.Append(SBLines.AllText()); all.Append("提示收起按住重招握拳防守手沉=蹲手提=跳左拳右拳踢"); all.Append("距离偏近适中远更多重置榜单已场地离你多按住不放才生效名次从头开始学了你的打法：爱蓄重招、爱出腿爱防守几乎不防守出手很快不轻易出手爱躲闪打法很均衡"); all.Append("闯关排位下一场换木偶最终战另一个你和一模样的木偶，连出招习惯都像打遍六只金腰带到手");
            all.Append("加时出手更重换视角长处短处精彩对局观战观看冠军战金腰带挑战权之战升位战以下犯上赢的人就是擂台之王当场颁发你是这两位在争下一个资格名次挨着谁排前面隔着好几来了换位换边离席视角胜者再看一场带水平对手蓄力冲上去打断落地出拳被破防全力追打要反击退开蹲下出腿防守重招假动作后撤闪开弹网脱身挑衅格挡放大招防住看穿新的卫冕成功守住第");
            all.Append(SBPilot.AllText()); all.Append("这是左拳右踢腿重招按住别松再松开走近一点操纵手默认用设置里可换左右桌面后撤闪开按食指伸到正这排木牌前就行名次卫冕成功守住了不变排位赛带寻找对手中在相近里已等待秒正比赛继续发起出回应接受了应战换一个取消挑战榜他准备第一次交手系统替自己挑人往"); all.Append("蹲跳顶网蓄力松手歇一会儿接着打法走得出步子沉慢重招蓄时不断浑身垮看路数血越少凶脚停高最快站笔直长伸在前面把人挡外头稳如城墙难穿摇晃飞挨了立刻还狠的速度力量臂展抗打防守怒气上一位下就选它暂停继续换人回标题弹射重击风格戳木牌擂台之王恭喜全胜反击趁现在破防被双偶五根手指提线一只木偶上擂台把伸到笼子方掌移网的牌捏下练习场选你会跳它必杀就绪另拉暂停放回第战回合开打胜败再换人标题来轮之王位对全部倒没断场好漂亮就是这样出师跳过住不放速度力量体重说明下一跃砸震圈冲击波群乌鸦啄张翅飞天头顶俯冲脚臂节伸长隔老远拳浑身铁壁动肩膀直线撞陀螺转连");
            SBUI.Prewarm(all.ToString());
            var fxGo = new GameObject("打击效果"); fxGo.transform.SetParent(transform, false);
            fxGo.SetActive(false); var fx = fxGo.AddComponent<SBFx>(); fx.litTemplate = litTemplate; fx.glowMat = glowMat; fxGo.SetActive(true);
            var ag = new GameObject("八角笼"); ag.transform.SetParent(transform, false);
            arena = ag.AddComponent<SBArena>(); arena.ledAsset = ledMat; arena.lit = litTemplate; arena.glow = glowMat; arena.fence = fenceMat; arena.crowdMat = crowdMat;
            arena.Build();
            var sg = new GameObject("界面"); sg.transform.SetParent(transform, false);
            stage = sg.AddComponent<SBStage>(); stage.arena = arena.transform; stage.head = head; stage.Build();
            handPref = PlayerPrefs.GetInt("sb_hand", 2) == 1 ? 1 : 2;
            distIdx = Mathf.Clamp(PlayerPrefs.GetInt("sb_dist", 1), 0, 2);
            showTips = PlayerPrefs.GetInt("sb_tips", 1) == 1;
            music = gameObject.AddComponent<SBMusic>();
            ai = gameObject.AddComponent<SBRival>();
            ai.clamp = p => arena.Clamp(p, 0.03f);
            var lm = new Material(unlitTemplate) { color = new Color(1f, .96f, .88f) };
            myLineMat = new Material(unlitTemplate) { color = new Color(1f, .96f, .88f) };
            for (int i = 0; i < 5; i++) { myLines[i] = Line(myLineMat); cpuLines[i] = Line(lm); }
            cpuBar = MakeBar(); meBar = MakeBar(); pilotHand = SBPilotHand.Build(transform, litTemplate);
            ai2 = gameObject.AddComponent<SBRival>(); ai2.clamp = p => arena.Clamp(p, 0.03f);
            if (autoPilot && Application.isEditor)
            {
                ap = gameObject.AddComponent<SBAutoPilot>(); ap.hands = hands; ap.match = this;
                PlayerPrefs.DeleteKey("sb_tut2");
            }
        }

        LineRenderer Line(Material m)
        {
            var lr = new GameObject("提线").AddComponent<LineRenderer>(); lr.transform.SetParent(transform, false);
            lr.positionCount = 2; lr.widthMultiplier = 0.0007f; lr.material = m; lr.useWorldSpace = true; lr.enabled = false;
            return lr;
        }

        Transform MakeBar()
        {
            // 对面操偶师手里的十字提线架：两根深色漆木条十字交叠（长的横、短的竖），四个端头各一颗黄铜圆帽，正中一枚黄铜圆钮。干净、一眼看得懂
            var bar = new GameObject("对手的十字木架").transform; bar.SetParent(transform, false);
            var wood = new Material(litTemplate) { color = new Color(.2f, .12f, .08f) }; wood.SetFloat("_Smoothness", 0.55f);
            var brass = new Material(litTemplate) { color = new Color(1.05f, .78f, .34f) }; brass.SetFloat("_Metallic", 0.45f); brass.SetFloat("_Smoothness", 0.6f);
            var w = new SBGeo(); w.Box(Vector3.zero, new Vector3(0.096f, 0.006f, 0.009f)); w.Box(new Vector3(0, 0.0045f, 0), new Vector3(0.009f, 0.005f, 0.066f)); w.Make(bar, "木条", wood);
            var bz = new SBGeo();
            foreach (var p in new[] { new Vector3(-0.048f, 0, 0), new Vector3(0.048f, 0, 0), new Vector3(0, 0.0045f, -0.033f), new Vector3(0, 0.0045f, 0.033f) }) bz.Blob(p, Vector3.one * 0.0058f, Quaternion.identity, 2f, 8, 12);
            bz.Lathe(new Vector3(0, 0.007f, 0), new[] { new Vector2(0.0075f, 0f), new Vector2(0.0075f, 0.0015f), new Vector2(0.004f, 0.004f), new Vector2(0f, 0.0045f) }, 16, 0f, false, false);
            bz.Make(bar, "铜件", brass);
            foreach (var r in bar.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bar.gameObject.SetActive(false);
            return bar;
        }

        SBPuppet Spawn(SBKit kit, Vector3 localPos, float yaw)
        {
            var go = new GameObject(kit.zh); go.transform.SetParent(transform, false);
            var p = go.AddComponent<SBPuppet>();
            p.Init(kit, litTemplate, PuppetH);
            p.floorY = arena.transform.position.y;
            p.clamp = w => arena.Clamp(w, Mathf.Max(0.02f, p.BodyR * 0.9f));   // 身子贴到网为止，不穿进网里
            p.limbClamp = w => arena.Clamp(w, 0.004f);                           // 拳头脚尖最多顶进网 4 毫米（网和护栏会被顶得鼓出去）
            p.onFence = OnFence;
            p.onUltHit = OnUltHit;
            p.Place(arena.transform.TransformPoint(localPos), yaw);
            if (shadowMat) { var b = go.AddComponent<SBBlob>(); b.p = p; b.mat = shadowMat; }
            return p;
        }

        void OnFence(SBPuppet p, float speed)
        {
            arena.Shake(p.Root.position + Vector3.up * PuppetH * 0.45f, speed);
            if (st != St.Fight || p.Ulting) return;   // 放必杀时撞网不掉血
            p.FenceDamage(speed * 10f);
            arena.Excite(0.25f);
            SBFx.Hit(p.Root.position + Vector3.up * PuppetH * 0.5f, -p.Velocity, 1.2f, false);
            Debug.Log($"[SB] 撞网 {p.Kit.zh} 速度{speed:0.00} 剩 {p.Health:0}");
        }

        // 八角笼放在头正前方 0.5m、比眼睛低 0.33m（大约桌面 / 手自然下垂再抬一点的高度）；笼的 +z 背对玩家
        void Place()
        {
            var f = head.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            PlaceAt(head.position + f * PlaceDist + Vector3.down * 0.33f, f);   // 笼子中心在正前方 0.62 米（原来 0.5 米太贴脸）；最近的那排木牌离身子约 0.33 米，伸手就能戳到
        }

        void PlaceAt(Vector3 center, Vector3 f)
        {
            placed = true;
            f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            arena.transform.position = center;
            arena.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            // 主光跟着笼子摆：从玩家左前上方打下来（脸和正面有光，右后方落在暗部，有明暗过渡）
            var sun = RenderSettings.sun ? RenderSettings.sun : FindAnyObjectByType<Light>();
            if (sun && sun.type == LightType.Directional)
                sun.transform.rotation = Quaternion.LookRotation(arena.transform.TransformDirection(new Vector3(0.45f, -0.85f, 0.6f)));
        }

        // 场地离你多远（设置里三档可调，存在本机）
        static readonly float[] Dists = { 0.52f, 0.62f, 0.74f }; int distIdx = 1; float PlaceDist => Dists[distIdx];
        int setPage;
        float ArenaYaw => arena.transform.eulerAngles.y;
        Vector3 AP(float x, float z) => arena.transform.TransformPoint(new Vector3(x, 0, z));

        void Update()
        {
            // 开场：等头显的头部追踪真的开始（头不在原点）、稳定 0.8 秒再摆，保证笼子在玩家正前方
            if (!placed && head)
            {
                bool tracking = Application.isEditor || head.localPosition.sqrMagnitude > 1e-4f;
                placeT = tracking ? placeT + Time.unscaledDeltaTime : 0f;
                if (placeT > 0.8f || Time.time > 8f) { Place(); EnterTitle(); }
            }
            if (!placed || !hands) return;
            float udt = Time.unscaledDeltaTime;
            stT += udt;
            if (slowT > 0) { slowT -= udt; if (slowT <= 0) Time.timeScale = 1f; }
            arena.Tick(head.position, udt);

            bool fighting = st == St.Fight, tutoring = st == St.Tutorial;
            // 用哪只手：先看到哪只用哪只；菜单里那只手不见了、另一只在，就换过去
            useRight = handPref == 1 ? 0 : 1;   // 操纵木偶的手由设置决定（左手 / 右手）；戳木牌两只手都行
            var h = useRight < 0 ? null : hands.Get(useRight == 1);
            bool on = h != null && h.tracked;
            var act = on ? gest.Update(h) : SBGestures.Act.None;
            bool pinchDown = on && gest.Pinch && !wasPinch; wasPinch = on && gest.Pinch;
            if (on && !handSeen) { handSeen = true; if (st == St.Title) stage.ShowTitle(true, !TutDone); }
            var picked = stage.Feed(h, on, on && gest.Pinch, pinchDown, hands.Left, hands.Right);
            // 暂停：对战中用指尖戳“暂停”木牌；暂停时换成“继续 / 换人 / 回标题”
            if (watching && swapT > 0) SwapTick();
            if (watching && picked == "swap") { if (st == St.Fight || st == St.RoundStart) WatchSwap(); picked = null; }
            else if (watching && picked == "leave") { BeginLobby(); LobbyWatch(); picked = null; }
            if (picked == "tips") { showTips = !showTips; PlayerPrefs.SetInt("sb_tips", showTips ? 1 : 0); PlayerPrefs.Save(); stage.RemoveTicket("tips"); picked = null; }   // 玩法小抄：开 / 关（木牌重新挂一次，换上新的字）
            if (picked == "pause" && st == St.Fight) { SetPause(true); picked = null; }
            else if (userPaused)
            {
                if (picked == "resume") { SetPause(false); stage.Slam(SBUI.L("继续！", "GO!"), SBUI.Cream, SBUI.Red, 0.04f, 0.7f, -6f); }
                else if (picked == "repick") { SetPause(false); if (arcade) BeginArcade(); else BeginLobby(); }
                else if (picked == "menu") { SetPause(false); EnterTitle(); }
                picked = null;
                fighting = st == St.Fight; tutoring = st == St.Tutorial;
            }
            // 菜单里玩家转身背对笼子超过 2.5 秒：把笼子挪回正前方
            if (false)   // 自动挪场地会让戴头显时场地乱跳：取消，只保留设置里的“摆正”
            {
                var to = arena.transform.position - head.position; to.y = 0; var hf = head.forward; hf.y = 0;
                awayT = Vector3.Angle(hf, to) > 75f ? awayT + udt : 0f;
                if (awayT > 2.5f) { awayT = 0; Place(); if (st == St.Title) EnterTitle(); else BeginSelect(); }
            }
            stage.Tick(udt); SayTick();

            // 手不见了 = 暂停
            lostT = on ? 0 : lostT + udt;
            bool handLost = (fighting || tutoring) && lostT > 0.5f && !watching;   // 观战不用手操纵，手放下也不暂停
            bool paused = handLost || (userPaused && fighting) || swapT > 0;   // 观战换视角的那半秒，比赛停一下
            stage.HandHint(handLost && !userPaused, handPref != 1);
            stage.PauseSign(userPaused && fighting);
            arena.SpotTarget = st == St.Champion ? 1f : stage.BoardShown ? 0.25f : 1f;   // 夺冠：聚光灯打在冠军身上

            switch (st)
            {
                case St.Title: UpdateTitle(picked); break;
                case St.Table: UpdateTable(h, on, picked); break;
                case St.Settings: UpdateSettings(picked); break;
                case St.Tutorial: UpdateTutorial(picked); break;
                case St.Lobby: UpdateLobby(picked); break;
                case St.Select: UpdateSelect(picked); break;
                case St.Intro:
                    // 闯关最后一战：你的木偶看见对面站着一只和自己一模一样的，愣一下说一句
                    if (ArcFinal && !mirrorSaid && stT > 1.1f && me) { mirrorSaid = true; Say(me, "mirror", ViewRight(me.Root.position) * 0.13f + Vector3.up * 0.11f, me.Root, 2.3f); }   // 纸条在身子右边、胸口高度，离对阵牌远
                    if (stT > 3.6f) { stage.HideBoard(); NextRound(); }
                    break;
                case St.RoundStart:
                    if (stT > 1.5f)
                    {
                        st = St.Fight; stT = 0;
                        stage.Slam(SBUI.L("开打！", "FIGHT!"), SBUI.Cream, SBUI.Red, 0.055f, 0.9f, -8f);
                        SBFx.Bell(arena.transform.position); arena.Excite(0.5f);
                    }
                    break;
                case St.KO:
                    // 等我方木偶把这一小局的话说完（纸条收掉）再往下走：下一回合的大字 / 结果牌不会和台词纸条叠在一起，铃声也不盖住台词。最多等到 6 秒
                    if (stT > 2.8f && (stT > 6f || (sayQ.Count == 0 && !stage.Speaking)))
                    {
                        if (watching && (winsMe >= 2 || winsCpu >= 2)) WatchFinish();
                        else if (arcade && (winsMe >= 2 || winsCpu >= 2)) ArcadeFinish();
                        else if (winsMe >= 2 || winsCpu >= 2)
                        {
                            iWon = winsMe >= 2;
                            // 记到擂台榜上：赢了排在前面的人就排到他前面；输给排在后面的人，他排到你前面
                            var rp = SBLadder.Report((debugPilot ?? oppPilot).id, iWon); rankBefore = rp.before; rankAfter = rp.after;
                            Debug.Log($"[SB] 擂台榜 {(iWon ? "胜" : "负")} {(debugPilot ?? oppPilot).zh} 名次 {rankBefore} → {rankAfter}");
                            if (iWon && rankAfter == 1 && rankBefore != 1) EnterChampion(); else EnterResult();
                        }
                        else NextRound();
                    }
                    break;
                case St.Result:
                    if (autoPilot && stT > 3f) picked = arcade ? (iWon ? "next" : "retry") : "lobby";
                    if (picked == "retry") { if (!arcade) oppKit = oppPilot.PickPuppet(); BeginIntro(); }   // 再战同一个人（排位里他可能换一只木偶；闯关里还是这一只）
                    else if (picked == "next" && arcade) { arcStage++; ArcadeOpp(); BeginIntro(); }          // 闯关：下一场
                    else if (picked == "more") { BeginLobby(); LobbyWatch(); }                  // 观战：再看一场
                    else if (picked == "lobby") BeginLobby();
                    else if (picked == "menu") EnterTitle();
                    break;
                case St.Champion:
                    ChampTick(udt);
                    if (picked == "again") { bool w = watching; BeginLobby(); if (w) LobbyWatch(); }
                    else if (picked == "menu") EnterTitle();
                    break;
            }
            // 对战中挂一块“暂停”木牌：在不操纵木偶的那只手一侧，只认指尖戳（打着打着手掌路过不会误碰）
            bool wantPause = (st == St.Fight || st == St.RoundStart) && !userPaused && !watching;
            // 观战中挂两块木牌：“换视角”（换成看另一位操偶师的手）、“离席”
            bool wantWatch = watching && (st == St.Fight || st == St.RoundStart || st == St.KO);
            if (wantWatch && !stage.HasTicket("swap"))
            {
                stage.AddTicket("swap", SBUI.L("换视角", "SWAP"), SBUI.Cobalt, SBUI.Cream, -0.07f, 0.048f, 0, 0.3f, 0, true);
                stage.AddTicket("leave", SBUI.L("离席", "LEAVE"), SBUI.Ink, SBUI.Cream, 0.07f, 0.048f, 0, 0.4f, 0, true);
            }
            else if (!wantWatch && stage.HasTicket("swap")) { stage.RemoveTicket("swap"); stage.RemoveTicket("leave"); }
            // 两块木牌都挂在“不操纵木偶的那只手”这一侧（操纵的那只手一直在另一边动，不会碰到）：外面是暂停，里面是提示
            float sideX = useRight == 0 ? 1f : -1f;
            if (wantPause && !stage.HasTicket("pause")) stage.AddTicket("pause", SBUI.L("暂停", "PAUSE"), SBUI.Ink, SBUI.Cream, sideX * 0.076f, 0.04f, 0, 0.3f, 0, true);
            else if (!wantPause && stage.HasTicket("pause")) stage.RemoveTicket("pause");
            if (wantPause && !stage.HasTicket("tips")) stage.AddTicket("tips", showTips ? SBUI.L("收起", "HIDE") : SBUI.L("提示", "TIPS"), showTips ? SBUI.Cream : SBUI.Gold, SBUI.Ink, sideX * 0.03f, 0.04f, 0, 0.05f, 0, true);
            else if (!wantPause && stage.HasTicket("tips")) stage.RemoveTicket("tips");
            stage.FightTips(showTips && wantPause && st == St.Fight);

            fighting = st == St.Fight; tutoring = st == St.Tutorial;
            music.Fight(st == St.Intro || st == St.RoundStart || st == St.Fight || st == St.KO);
            bool live = (fighting || tutoring) && !paused;
            if (me) me.frozen = !live;
            if (cpu) cpu.frozen = !live;
            ai.active = fighting && !paused;
            ai2.active = watching && fighting && !paused;
            if (watching && me && cpu)
            {
                // 观战加时：一回合打了 45 秒还没分出胜负，双方伤害一起慢慢加到 3 倍（对两边一样），不让一场拖太久
                if (fighting && !paused) watchRoundT += Time.deltaTime;
                float ot = 1f + Mathf.Clamp((watchRoundT - 45f) / 15f, 0f, 2f);
                if (ot > 1f && !watchOT) { watchOT = true; stage.Slam(SBUI.L("加时！出手更重", "OVERTIME! HITS HARDER"), SBUI.Ink, SBUI.Gold, 0.028f, 1.4f, -4f, 0.12f); SBFx.Bell(arena.transform.position); arena.Excite(0.8f); }
                me.dmgOut = cpu.dmgOut = ot;
            }
            if (ap) ap.fingers = !tutoring;

            // 你的木偶：跟手（相对位移）+ 手势
            if (!on || !live || !me) anchored = false;
            if (me && on && !watching)
            {
                if (live)
                {
                    // 锚点：开始控制那一刻手掌在哪、木偶在哪；之后木偶 = 木偶锚点 + 手掌位移 × Gain
                    if (!anchored) { palmA = h.palm; pupA = me.Root.position; anchored = true; netCharge = 0; }
                    var d = h.palm - palmA; d.y = 0;
                    var want = pupA + d * Gain;
                    var c = arena.Clamp(want, 0.03f);
                    var over = want - c; over.y = 0;
                    // 弹网：木偶已经贴到网了手还继续往外顶 = 把网撑开蓄力（最多撑开 4 厘米，再多锚点才跟着滑）
                    float om = over.magnitude;
                    float stretchMax = autoPilot && !(ap && ap.useForce) ? 0f : NetStretch;   // 编辑器里的假手不会往回收手：平时不让它蓄力，免得一直顶在网上
                    if (om > stretchMax) { palmA += over * ((om - stretchMax) / om) / Gain; om = stretchMax; }
                    bool pressing = om > 0.012f && !me.Dashing && !me.KO;
                    if (pressing)
                    {
                        if (netCharge <= 0f) SBFx.Whoosh(me.Root.position, 0.5f);
                        float before = netCharge;
                        netCharge = Mathf.Min(1f, netCharge + udt / 0.7f); netN = over / Mathf.Max(om, 1e-5f); netHold = 0.1f;
                        if (before < 0.4f && netCharge >= 0.4f) SBFx.Ui(me.Root.position, false);   // “咔”一声：够弹了
                    }
                    else if (netCharge > 0f)
                    {
                        netHold -= udt;
                        if (netHold <= 0f)
                        {
                            if (netCharge >= 0.4f && !me.KO)
                            {
                                // 松开：被网弹出去。朝场内弹；对手大致在那个方向就直接弹向对手（停在够得着的距离）
                                var inward = -netN; var mp = me.Root.position; var dir = inward;
                                if (cpu) { var tf = cpu.Root.position - mp; tf.y = 0; if (tf.sqrMagnitude > 1e-6f && Vector3.Dot(tf.normalized, inward) > 0.25f) dir = (inward * 0.4f + tf.normalized).normalized; }
                                var dest = arena.Clamp(mp + dir * (0.03f + 0.035f * netCharge), 0.03f);   // 只弹出去一小步
                                if (cpu)
                                {
                                    var tf = cpu.Root.position - mp; tf.y = 0; float keep = me.Reach * 0.9f + cpu.BodyR;
                                    var along = Vector3.Dot(dest - mp, tf.normalized);
                                    if (tf.magnitude - along < keep) dest = mp + (dest - mp) * Mathf.Clamp01((tf.magnitude - keep) / Mathf.Max(along, 1e-4f));
                                }
                                me.Dash(dest, 0.14f, 0.5f * netCharge);
                                palmA = h.palm; pupA = dest; c = dest; om = 0;
                                arena.Shake(mp + netN * 0.03f, 0.8f); SBFx.Shockwave(mp, 0.028f + 0.018f * netCharge, SBUI.Gold);
                                Debug.Log($"[SB] 弹网冲刺 蓄力{netCharge:0.00}");
                            }
                            netCharge = 0f;
                        }
                    }
                    if (netCharge > 0f && !me.Dashing)
                    {   // 蓄力时：身子往网里陷、网和护栏被顶得鼓出去
                        float deep = 0.008f * netCharge + 0.006f * (om / NetStretch);
                        c = arena.Clamp(want, 0.03f - deep);
                        var bp = me.Root.position + Vector3.up * PuppetH * 0.5f + netN * (0.012f + 0.02f * netCharge);
                        arena.NetTouch(bp, me.BodyR * 1.2f, 0.03f); arena.RailPress(bp, me.BodyR + 0.01f + 0.008f * netCharge);
                    }
                    if (!me.Dashing) me.target = c;
                }
                me.guard = gest.Guard || (apGuard && st == St.Tutorial); me.charge = gest.Charge; me.chargeSide = gest.ChargeSide;
                if (arcade && fighting && live && !ArcFinal) { hFightT += Time.deltaTime; if (me.GuardUp) hGuardT += Time.deltaTime; }
                if (live && act == SBGestures.Act.Ult) { if (me.UltReady) TryUlt(me); }
                else if (live && act != SBGestures.Act.None) { me.Do(act, -1f, gest.Released); if (arcade && fighting && !ArcFinal) Habit(act); Debug.Log($"[SB] 手势 {act} 蓄力{gest.Released:0.00}"); TutAct(act); }
            }
            if (autoPilot && me && fighting && me.UltReady && !watching) TryUlt(me);
            if (watching && me && fighting && me.UltReady && !me.Ulting && Random.value < Time.deltaTime * ai2.UltRate) TryUlt(me);
            if (cpu && fighting && !paused && cpu.UltReady && !cpu.Ulting && Random.value < Time.deltaTime * ai.UltRate) TryUlt(cpu);
            stage.CheatSheet(userPaused && fighting);   // 招式提示只在暂停时出现（占记分屏的位置），对战中不挡视线
            stage.UltHint(me && live && !watching && me.UltReady && !me.Ulting && (fighting || tutStep == 8), ultUses < 2);
            Strings(h, on);
            NetContacts();
            // 观战：近侧这位操偶师刚做了什么决定，写在他提线木架上方的小纸条上
            if (watching && fighting && me && cpu)
            {
                var nb = watchSide > 0 ? ai2 : ai;
                if (nb.LastSayT > shownSayT) { shownSayT = nb.LastSayT; stage.Thought(pilotHand.thoughtAt, nb.SayText(), 0f); }
            }

            if (live && me && cpu)
            {
                if (!me.Ulting && !cpu.Ulting && !me.KO && !cpu.KO) minGap = Mathf.Min(minGap, Vector3.Distance(me.Root.position, cpu.Root.position) - me.BodyR - cpu.BodyR);
                Resolve(me, cpu, "我");
                Resolve(cpu, me, "对手");
                if (fighting && (me.KO || cpu.KO))
                {
                    bool cpuDown = cpu.KO;
                    if (cpuDown) winsMe++; else winsCpu++;
                    var winner = cpuDown ? me : cpu; winner.victory = true;   // 赢的一方举手庆祝，到下一回合开始为止
                    MatchLines(winner, cpuDown ? cpu : me, winsMe >= 2 || winsCpu >= 2);   // 每一小局都说一句：整场分出胜负说“赢了 / 输了”，还没完说“再来 / 不服”
                    st = St.KO; stT = 0;
                    stage.Slam("K.O.!", SBUI.Cream, SBUI.Red, 0.075f, 1.3f, -10f);   // 大字只留到慢动作结束：之后轮到木偶说话，纸条和大字不同时出现（木偶站在擂台靠里时，纸条会被大字压住）
                    arena.Excite(1f); SBFx.Cheer(arena.transform.position);
                    Time.timeScale = 0.35f; slowT = 1.2f;
                    Debug.Log($"[SB] KO {(cpuDown ? cpu.Kit.zh : me.Kit.zh)} 倒下 比分 {winsMe}:{winsCpu}");
                }
            }
            if (me && cpu && (st == St.Fight || st == St.RoundStart || st == St.KO))
                arena.Score(SBUI.En ? me.Kit.en : me.Kit.zh, SBUI.En ? cpu.Kit.en : cpu.Kit.zh, me.Health / 100f, cpu.Health / 100f,
                            SBUI.L($"第{Mathf.Max(1, round)}回合", $"R{Mathf.Max(1, round)}"), winsMe, winsCpu, me.Meter / 100f, cpu.Meter / 100f);
            // 怒气满：你的提线变成金色、一闪一闪
            // 你的提线也跟着状态变色（你从木偶背后看不到它的拳套，线是一直看得见的）：防守中 = 拳套的颜色；被破防 = 发灰
            if (me) myLineMat.color = me.UltReady ? Color.Lerp(new Color(1f, .75f, .2f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f))
                                    : me.GuardBroken ? new Color(.45f, .43f, .42f) : me.GuardUp ? Color.Lerp(me.Kit.glove, Color.white, 0.25f) * 1.25f : new Color(1f, .96f, .88f);   // 防守中 = 线绷成拳套的颜色
            if (me && !watching) { bool gu = me.GuardUp; if (gu && !guardWas) SBFx.Ui(me.Root.position, false); guardWas = gu; }   // 防守举起来的那一下“嗒”一声
        }

        // 网的接触：木偶贴着网时身子和拳头把网顶出去；手指从外面戳网会把网戳进去
        void NetContacts()
        {
            void Pup(SBPuppet p)
            {
                if (!p || !p.Ready) return;
                arena.NetTouch(p.Root.position + Vector3.up * p.H * 0.5f, p.BodyR, 0.022f);
                arena.NetTouch(p.FistL.position, p.H * 0.06f, 0.012f);
                arena.NetTouch(p.FistR.position, p.H * 0.06f, 0.012f);
                // 靠在网上 / 拳头打到网：顶上的护栏跟着往外鼓
                arena.RailPress(p.Root.position + Vector3.up * p.H * 0.5f, p.BodyR + 0.004f);
                arena.RailPress(p.FistL.position, p.H * 0.04f); arena.RailPress(p.FistR.position, p.H * 0.04f);
            }
            Pup(me); Pup(cpu); foreach (var q in picks) Pup(q);
            foreach (var hh in new[] { hands.Left, hands.Right })
                if (hh.tracked) for (int i = 0; i < 5; i++) arena.NetTouch(hh.tips[i], 0.008f, 0.012f);
        }

        void TryUlt(SBPuppet p)
        {
            if (!p.UltReady || p.Ulting) return;
            p.StartUlt();
            if (p == me && !watching) { ultUses++; if (arcade && !ArcFinal) hUlt++; }
            stage.Slam(SBUI.L(p.Kit.ultZh, p.Kit.ultEn) + SBUI.L("！", "!"), SBUI.Ink, SBUI.Gold, st == St.Tutorial ? 0.03f : 0.045f, 1.1f, p == me ? -6f : 6f, st == St.Tutorial ? 0.1f : 0.17f);
            arena.Excite(0.8f);
            Time.timeScale = 0.5f; slowT = 0.3f;
            Debug.Log($"[SB] 必杀 {p.Kit.zh} {p.Kit.ultZh}");
        }

        void OnUltHit(SBPuppet a, float power, Vector3 dir)
        {
            var b = a.foe; if (!b || b.KO || (st != St.Fight && st != St.Tutorial)) return;
            bool blocked = b.TakeHit(dir, power, 0f, 0f, false, false);
            SBFx.Hit(b.Head.position, dir, power, blocked);
            arena.Excite(0.3f);
            b.AddMeter(6f);
            if (st == St.Tutorial && a == me) TutCount(8);
            Debug.Log($"[SB] 必杀打中 {a.Kit.zh}→{b.Kit.zh} 力度{power:0.0} {(blocked ? "被挡" : "")} 对方剩 {b.Health:0}");
        }

        // ───────── 标题 ─────────
        void EnterTitle()
        {
            st = St.Title; stT = 0; arcade = false; sayQ.Clear(); stage.ClearSpeech();
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            arena.ShowScreen(false);
            stage.ShowTitle(handSeen, !TutDone);
            TitleTickets();
            SpawnExhibition();
        }

        void SpawnExhibition()
        {
            // 笼子里两只木偶真打一场表演赛（和标题海报一样：秤砣对纸鸢）：各配一个电脑大脑，打中有效果，但不会倒
            var a = Spawn(SBKit.Get("weight"), new Vector3(-0.06f, 0, 0.03f), ArenaYaw + 90f);
            var b = Spawn(SBKit.Get("kite"), new Vector3(0.06f, 0, 0.03f), ArenaYaw - 90f);
            a.opponent = b.Root; b.opponent = a.Root; a.foe = b; b.foe = a;
            picks.Add(a); picks.Add(b);
            foreach (var (x, y) in new[] { (a, b), (b, a) })
            {
                var r = new GameObject("表演赛大脑").AddComponent<SBRival>(); r.transform.SetParent(transform, false);
                r.self = x; r.foe = y; r.clamp = p => arena.Clamp(p, 0.05f); r.SetLevel(2); r.active = true;
                showAIs.Add(r);
            }
        }

        void TitleTickets()
        {
            // 四块木牌都挂在正面这一排、两根笼柱之间（原来两头的牌子伸到斜边上，压着笼柱）
            const float w = 0.036f, step = 0.039f;
            stage.AddTicket("story", SBUI.L("闯关", "STORY"), SBUI.Red, SBUI.Cream, -2f * step, w, 0, 0.3f);       // 打遍六只木偶夺金腰带（主线）
            stage.AddTicket("fight", SBUI.L("排位", "RANK"), SBUI.Cobalt, SBUI.Cream, -1f * step, w, 0, 0.36f);    // 擂台榜
            stage.AddTicket("train", SBUI.L("练习", "TRAIN"), SBUI.Jade, SBUI.Cream, 0f, w, 0, 0.42f);
            stage.AddTicket("settings", SBUI.L("设置", "SETUP"), SBUI.Cream, SBUI.Ink, 1f * step, w, 0, 0.48f);
            stage.AddTicket("quit", SBUI.L("退出", "QUIT"), SBUI.Ink, SBUI.Cream, 2f * step, w, 0.7f, 0.54f);
        }

        // 编辑器查平衡用：两只木偶由同一位操偶师操纵对打，统计各自打出的总伤害和击倒次数
        string simA; float simDA, simDB, simHA, simHB; int simKA, simKB;
        List<(string a, string b)> simQ; int simI; float simEnd, simDur; string simPilot;
        void SimNext()
        {
            simI++;
            if (simQ == null || simI >= simQ.Count) { System.IO.File.AppendAllText("/tmp/sb_sim.txt", "DONE\n"); simQ = null; return; }
            SimStart(simQ[simI].a, simQ[simI].b, simPilot); simEnd = Time.unscaledTime + simDur;
        }
        void SimStart(string a, string b, string pilot)
        {
            st = St.Title; stT = 0; ClearFighters(); stage.ClearTickets(); stage.HideBoard();
            var pa = Spawn(SBKit.Get(a), new Vector3(-0.06f, 0, 0.03f), ArenaYaw + 90f); var pb = Spawn(SBKit.Get(b), new Vector3(0.06f, 0, 0.03f), ArenaYaw - 90f);
            pa.opponent = pb.Root; pb.opponent = pa.Root; pa.foe = pb; pb.foe = pa; picks.Add(pa); picks.Add(pb);
            foreach (var (x, y) in new[] { (pa, pb), (pb, pa) })
            {
                var r = new GameObject("对打大脑").AddComponent<SBRival>(); r.transform.SetParent(transform, false);
                r.self = x; r.foe = y; r.clamp = p => arena.Clamp(p, 0.05f); r.SetPilot(SBPilot.Get(pilot)); r.active = true; showAIs.Add(r);
            }
            simA = a + " 对 " + b; simDA = simDB = 0; simKA = simKB = 0; simHA = simHB = 100f; Time.timeScale = 2.4f;
        }

        // 表演赛：真的结算打中（有打击效果和观众反应），血少了就回满，不会倒
        void ShowFight()
        {
            if (simA != null && picks.Count == 2 && picks[0] && picks[1])
            {
                if (simQ != null && Time.unscaledTime >= simEnd) { System.IO.File.AppendAllText("/tmp/sb_sim.txt", $"{simQ[simI].a} {simQ[simI].b} {simDA:0} {simDB:0}\n"); SimNext(); return; }
                Resolve(picks[0], picks[1], "表演"); Resolve(picks[1], picks[0], "表演");
                if (slowT <= 0) Time.timeScale = 2.4f;
                simDB += Mathf.Max(0, simHA - picks[0].Health); simDA += Mathf.Max(0, simHB - picks[1].Health);   // A 打出的伤害 = B 掉的血
                foreach (var p in picks) if (p.Health <= 0.5f) { if (p == picks[0]) simKB++; else simKA++; picks[0].ResetHealth(); picks[1].ResetHealth(); picks[0].Place(AP(-0.06f, 0.03f), ArenaYaw + 90f); picks[1].Place(AP(0.06f, 0.03f), ArenaYaw - 90f); break; }
                simHA = picks[0].Health; simHB = picks[1].Health;
                return;
            }
            if (picks.Count == 2 && picks[0] && picks[1])
            {
                Resolve(picks[0], picks[1], "表演");
                Resolve(picks[1], picks[0], "表演");
                foreach (var p in picks) if (p.Health < 40f && !p.KO) p.ResetHealth();
            }
        }

        void UpdateTitle(string picked)
        {
            ShowFight();
            if (autoPilot && stT > 2f && Time.time > 6f) picked = "fight";
            if (picked == "story") { wantArcade = true; if (TutDone) BeginArcade(); else BeginTutorial(); }
            else if (picked == "fight") { wantArcade = false; if (TutDone) BeginLobby(); else BeginTutorial(); }
            else if (picked == "train") BeginTutorial();
            else if (picked == "settings") EnterSettings(0);
            else if (picked == "quit") { Debug.Log("[SB] 退出"); Application.Quit(); }
        }

        // ───────── 设置：放桌上 / 语言 / 摆正 / 返回（标题页的表演赛继续打）─────────
        // 设置分两页（正面那排最多挂五块木牌）：第一页管场地——桌面 / 摆正 / 远近；第二页管操作和别的——操纵手 / 语言 / 重置榜单
        void EnterSettings(int page = -1)
        {
            if (page >= 0) setPage = page;
            st = St.Settings; stT = 0;
            stage.ClearTickets();
            stage.ShowSettings(setPage);
            const float w = 0.0365f, step = 0.0395f;
            if (setPage == 0)
            {
                stage.AddTicket("table", SBUI.L("桌面", "TABLE"), SBUI.Cobalt, SBUI.Cream, -2 * step, w, 0, 0.2f);
                stage.AddTicket("center", SBUI.L("摆正", "CENTER"), SBUI.Jade, SBUI.Cream, -step, w, 0, 0.26f);
                stage.AddTicket("dist", distIdx == 0 ? SBUI.L("偏近", "NEAR") : distIdx == 1 ? SBUI.L("适中", "MID") : SBUI.L("偏远", "FAR"), SBUI.Gold, SBUI.Ink, 0f, w, 0, 0.32f);
                stage.AddTicket("more", SBUI.L("更多", "MORE"), SBUI.Cream, SBUI.Ink, step, w, 0, 0.38f);
                stage.AddTicket("back", SBUI.L("返回", "BACK"), SBUI.Ink, SBUI.Cream, 2 * step, w, 0, 0.44f);
            }
            else
            {
                const float w2 = 0.045f, step2 = 0.049f;
                stage.AddTicket("hand", handPref == 1 ? SBUI.L("左手", "LEFT") : SBUI.L("右手", "RIGHT"), SBUI.Red, SBUI.Cream, -1.5f * step2, w2, 0, 0.2f);
                stage.AddTicket("lang", SBUI.En ? "中文" : "EN", SBUI.Cream, SBUI.Ink, -0.5f * step2, w2, 0, 0.26f, SBUI.En ? 1 : 2);
                stage.AddTicket("reset", SBUI.L("重置榜", "RESET"), SBUI.Cobalt, SBUI.Cream, 0.5f * step2, w2, 1f, 0.32f);   // 要按住 1 秒：免得误碰把名次清掉
                stage.AddTicket("setback", SBUI.L("返回", "BACK"), SBUI.Ink, SBUI.Cream, 1.5f * step2, w2, 0, 0.38f);
            }
        }

        void UpdateSettings(string picked)
        {
            ShowFight();
            if (picked == "table") EnterTable();
            else if (picked == "lang") { SBUI.ToggleLang(); EnterSettings(); }
            else if (picked == "hand")
            {
                handPref = handPref == 1 ? 2 : 1; PlayerPrefs.SetInt("sb_hand", handPref); PlayerPrefs.Save(); anchored = false;
                EnterSettings();
                stage.Slam(handPref == 1 ? SBUI.L("操纵手：左手", "HAND: LEFT") : SBUI.L("操纵手：右手", "HAND: RIGHT"), SBUI.Ink, SBUI.Gold, 0.028f, 1.1f, -4f, 0.12f);
            }
            else if (picked == "center") { Place(); EnterSettings(); stage.Slam(SBUI.L("摆正了", "CENTERED"), SBUI.Ink, SBUI.Gold, 0.03f, 1f, -4f, 0.12f); }
            else if (picked == "dist")
            {   // 远近：偏近 → 适中 → 偏远，按一下换一档，场地马上挪过去
                distIdx = (distIdx + 1) % 3; PlayerPrefs.SetInt("sb_dist", distIdx); PlayerPrefs.Save();
                Place(); EnterSettings();
                stage.Slam(distIdx == 0 ? SBUI.L("距离：偏近", "DISTANCE: NEAR") : distIdx == 1 ? SBUI.L("距离：适中", "DISTANCE: MID") : SBUI.L("距离：偏远", "DISTANCE: FAR"), SBUI.Ink, SBUI.Gold, 0.028f, 1.1f, -4f, 0.12f);
            }
            else if (picked == "more") EnterSettings(1);
            else if (picked == "setback") EnterSettings(0);
            else if (picked == "reset") { SBLadder.Reset(); EnterSettings(); stage.Slam(SBUI.L("榜单已重置", "LADDER RESET"), SBUI.Ink, SBUI.Gold, 0.028f, 1.2f, -4f, 0.12f); Debug.Log("[SB] 榜单已重置"); }
            else if (picked == "back") { st = St.Title; stT = 0; stage.ClearTickets(); TitleTickets(); stage.ShowTitle(handSeen, !TutDone); }
        }

        // ───────── 放到桌上：手掌平放在桌面、停一秒，笼子就落在那里 ─────────
        void EnterTable()
        {
            st = St.Table; stT = 0; stillT = 0;
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            stage.ShowTable();
            stage.AddTicket("back", SBUI.L("返回", "BACK"), SBUI.Cream, SBUI.Ink, 0.066f, 0.055f, 0, 0.3f);
        }

        void UpdateTable(SBHands.Hand h, bool on, string picked)
        {
            if (picked == "back") { EnterTitle(); return; }
            if (!on) { stillT = 0; return; }
            float sp = (h.palm - lastPalm).magnitude / Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            lastPalm = h.palm;
            bool low = h.palm.y < head.position.y - 0.18f;
            // 别把“去戳返回牌”当成放手：手离返回牌 8 厘米以外才算
            stillT = (sp < 0.035f && low) ? stillT + Time.unscaledDeltaTime : 0f;
            if (stillT > 1f)
            {
                var f = head.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = arena.transform.forward; f.Normalize();
                var c = h.palm + f * 0.16f; c.y = h.palm.y - 0.022f;   // 手掌厚度约 2 厘米
                PlaceAt(c, f);
                SBFx.Bell(c);
                EnterTitle();
                stage.Slam(SBUI.L("放好了！", "PLACED!"), SBUI.Ink, SBUI.Gold, 0.035f, 1.2f, -5f, 0.12f);
                Debug.Log($"[SB] 放到桌上 高度 {c.y:0.000}");
            }
        }

        // ───────── 练习场 ─────────
        void BeginTutorial()
        {
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            st = St.Tutorial; stT = 0;
            me = Spawn(SBKit.Get("scarecrow"), new Vector3(0, 0, -0.12f), ArenaYaw);
            cpu = Spawn(SBKit.Dummy, new Vector3(0, 0, 0.09f), ArenaYaw + 180f);
            me.opponent = cpu.Root; cpu.opponent = me.Root; me.foe = cpu; cpu.foe = me;
            cpuBar.gameObject.SetActive(true); cpuBar.position = cpu.Root.position + Vector3.up * BarH;
            arena.ShowScreen(false);
            stage.AddTicket("skip", SBUI.L("跳过", "SKIP"), SBUI.Ink, SBUI.Cream, 0.066f, 0.058f, 0.8f, 0.5f);
            stage.Slam(SBUI.L("练习场", "TRAINING"), SBUI.Cream, SBUI.Jade, 0.035f, 1.4f, -5f, 0.1f);
            TutEnter(0);
        }

        void TutEnter(int k)
        {
            tutStep = k; tutDone = 0; tutWait = 0; tutAtkT = autoPilot ? 3.5f : 1.6f; tutRefill = 0; apT = autoPilot ? 3f : 0;
            var d = Tut[k];
            stage.ShowTutorial(k, SBUI.L(d.zh, d.en), SBUI.L(d.izh, d.ien), d.need, SBUI.L(d.nzh, d.nen));
            me.ResetHealth(); cpu.ResetHealth();
            if (k == 8) me.AddMeter(100f);
            Debug.Log($"[SB] 练习第 {k + 1} 步 {d.zh}");
        }

        void TutCount(int k)
        {
            if (st != St.Tutorial || k != tutStep || tutWait > 0) return;
            tutDone++;
            stage.TutProgress(tutDone);
            arena.Excite(0.3f);
            if (tutDone >= Tut[k].need)
            {
                tutWait = 1.4f;
                var n = Nice[k % Nice.Length];
                stage.Slam(SBUI.L(n.zh, n.en), SBUI.Ink, SBUI.Gold, 0.03f, 1.2f, -6f, 0.1f);
                SBFx.Cheer(arena.transform.position);
            }
            else SBFx.Ui(cpu.Root.position, false);
        }

        void TutFinish()
        {
            PlayerPrefs.SetInt("sb_tut2", 1); PlayerPrefs.Save();
            if (wantArcade) BeginArcade(); else BeginLobby();
            stage.Slam(SBUI.L("出师！", "YOU'RE READY!"), SBUI.Cream, SBUI.Red, 0.035f, 1.6f, -6f, 0.1f);
            SBFx.Cheer(arena.transform.position);
        }

        void UpdateTutorial(string picked)
        {
            apGuard = false;
            if (autoPilot && stT > 90f) picked = "skip";
            if (picked == "skip") { TutFinish(); return; }
            float dt = Time.deltaTime;
            if (tutWait > 0) { tutWait -= Time.unscaledDeltaTime; if (tutWait <= 0) { if (tutStep + 1 < Tut.Length) TutEnter(tutStep + 1); else TutFinish(); return; } }
            if (!me || !cpu) return;
            // 不让谁真的倒下
            if (me.Health < 45f && !me.Ulting) me.ResetHealth();
            if (cpu.Health < 45f) cpu.ResetHealth();
            var mp = me.Root.position; var cp = cpu.Root.position;
            var away = cp - mp; away.y = 0; float dist = away.magnitude; away = dist > 1e-4f ? away / dist : arena.transform.forward;
            bool attacking = tutStep >= 5 && tutStep <= 7;
            float cReach = cpu.Reach;
            cpu.target = attacking ? arena.Clamp(mp + away * cReach * 0.9f, 0.03f) : AP(0, 0.09f);
            if (attacking && tutWait <= 0)
            {
                tutAtkT -= dt;
                if (tutAtkT <= 0 && dist < cReach + cpu.H * 0.45f && cpu.ActiveAttack == SBGestures.Act.None)
                {
                    var a = tutStep == 7 ? SBGestures.Act.Kick : (Random.value < 0.5f ? SBGestures.Act.JabL : SBGestures.Act.JabR);
                    cpu.Do(a, 0.9f);
                    tutAtkT = 2.4f;
                }
            }
            if (tutStep == 0 && dist < me.Reach + me.H * 0.35f) TutCount(0);
            if (tutStep == 8 && tutDone == 0 && !me.Ulting && !me.UltReady) { tutRefill += dt; if (tutRefill > 1f) { me.AddMeter(100f); tutRefill = 0; } }

            // 编辑器自动测试：按这一步要求的招式做
            if (autoPilot && tutWait <= 0)
            {
                apT -= dt;
                switch (tutStep)
                {
                    case 1: case 2: case 3: case 4:
                        if (apT <= 0) { var ta = tutStep == 1 ? SBGestures.Act.JabL : tutStep == 2 ? SBGestures.Act.JabR : tutStep == 3 ? SBGestures.Act.Heavy : SBGestures.Act.Kick; me.Do(ta); TutAct(ta); apT = 0.7f; }
                        break;
                    case 5: apGuard = true; break;
                    case 6: if (cpu.ActiveAttack != SBGestures.Act.None && !cpu.WindingUp) me.Do(SBGestures.Act.Duck); break;
                    case 7: if (SBGestures.IsKick(cpu.ActiveAttack) && !cpu.WindingUp) me.Do(SBGestures.Act.Jump); break;
                    case 8: if (me.UltReady) TryUlt(me); break;
                }
            }
        }

        // ───────── 选角色：木牌按钮左右切换，选中的那位走到最前面、脚下亮起一块台 ─────────
        int sel; Vector3[] selBase = new Vector3[0]; Transform selDisc; Material selDiscM, selOutline, selMask; int apSelN;
        void BeginSelect()
        {
            st = St.Select; stT = 0; hover = -1; apSelN = 0;
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            arena.ShowScreen(false);
            var kits = new List<SBKit>(SBKit.All);   // 六只都能选，和对手选同一只也行（那时对手那只换一副蓝拳套，好分清）
            int n = kits.Count;
            selBase = new Vector3[n]; sel = 0;
            for (int i = 0; i < n; i++)
            {
                // 一字排开站在台子中线偏前；选中的那位往前走出队列一大步，完全离开队伍，不和左右、身后的木偶挤
                float u = n > 1 ? i / (float)(n - 1) * 2f - 1f : 0f;
                var lp = new Vector3(u * 0.185f, 0, -0.03f);
                var p = Spawn(kits[i], lp, ArenaYaw + 180f + u * 14f);
                p.frozen = true;
                picks.Add(p); selBase[i] = p.Root.position;   // 不再给每个木偶头顶挂名牌（会挡住后面的属性牌），名字看属性牌上的大字
                if (myKit != null && kits[i] == myKit) sel = i;
            }
            // 选中的那位用一圈金色描边标出来（每个角色一个描边材质实例，颜色统一）
            if (outlineMat && !selOutline)
            {
                // 占位：不出颜色，只记下木偶占了哪些像素（比较 = 总是通过 8，通过后 = 写入 2，只画正面）
                selMask = new Material(outlineMat) { renderQueue = 2005 }; selMask.SetFloat("_Width", 0f); selMask.SetFloat("_SComp", 8f); selMask.SetFloat("_SPass", 2f); selMask.SetFloat("_CMask", 0f); selMask.SetFloat("_CullM", 2f);
                // 描边：撑大一圈，只画在“不是木偶”的像素上（比较 = 不等于 6）→ 只剩最外面一圈黑边
                selOutline = new Material(outlineMat) { renderQueue = 2006 }; selOutline.SetColor("_Color", new Color(.05f, .04f, .04f)); selOutline.SetFloat("_Width", 0.0023f); selOutline.SetFloat("_SComp", 6f); selOutline.SetFloat("_SPass", 0f); selOutline.SetFloat("_CMask", 15f); selOutline.SetFloat("_CullM", 0f);   // 墨黑色描边（和界面的墨色一致）
            }
            {   // 四块木牌：上一位 / 就选它 / 下一位 / 返回（闯关回标题，排位回擂台榜）
                const float w = 0.045f, step = 0.049f;
                stage.AddTicket("prev", SBUI.L("上一位", "PREV"), SBUI.Cream, SBUI.Ink, -1.5f * step, w, 0, 0.2f);
                stage.AddTicket("pick", SBUI.L("就选它", "PICK"), SBUI.Red, SBUI.Cream, -0.5f * step, w, 0, 0.27f);
                stage.AddTicket("next", SBUI.L("下一位", "NEXT"), SBUI.Cream, SBUI.Ink, 0.5f * step, w, 0, 0.34f);
                stage.AddTicket("selback", SBUI.L("返回", "BACK"), SBUI.Ink, SBUI.Cream, 1.5f * step, w, 0, 0.41f);
            }
            SetSel(sel, true);
        }

        void SetSel(int i, bool quiet = false)
        {
            if (sel >= 0 && sel < picks.Count && picks[sel]) picks[sel].SetOutline(null, null);
            sel = i; var p = picks[sel]; p.SetOutline(selMask, selOutline);
            p.frozen = false; p.Do(SBGestures.Act.Jump); p.frozen = true;
            stage.ShowSelect(p.Kit, sel, picks.Count, oppPilot != null && oppKit != null ? SBUI.L($"对手：{oppPilot.zh} · {oppKit.zh}", $"VS {oppPilot.en} · {oppKit.en}") : null);
            stage.HighlightFollow(sel);
            if (selDiscM) selDiscM.color = Color.Lerp(p.Kit.glove, SBUI.Gold, 0.45f) * 1.25f;
            if (!quiet) SBFx.Ui(p.Root.position, false);
            stage.ClearSpeech();                                                 // 换了一只：上一只的话收掉，同时只有一张纸条
            int tok = ++selSayN;
            // 刚进选人页（quiet）：页面先摆好，隔半秒台上第一只也自己报个到——不然只有换到别的木偶才听得到台词
            if (quiet) sayQ.Add((Time.unscaledTime + 0.6f, () => { if (st == St.Select && tok == selSayN && sel < picks.Count && picks[sel]) SayPick(picks[sel]); }));
            // 被选中：往前一步，说一句。纸条挂在它身子右边、肩膀高度（不放头顶：高个子的头顶离后面的属性牌太近）
            if (!quiet) SayPick(p);
        }
        int selSayN;
        void SayPick(SBPuppet p) => Say(p, "pick", ViewRight(p.Root.position) * 0.1f + Vector3.up * Mathf.Min(p.HeadTop.position.y - p.Root.position.y - 0.01f, 0.17f), p.Root, 2.2f);

        void UpdateSelect(string picked)
        {
            int n = picks.Count; if (n == 0) return;
            if (autoPilot) { if (apSelN < 3 && stT > 1.2f + apSelN * 0.9f) { picked = "next"; apSelN++; } else if (stT > 5.5f) picked = "pick"; }
            if (picked == "selback") { if (arcade) EnterTitle(); else BeginLobby(); return; }
            if (picked == "prev") SetSel((sel + n - 1) % n);
            else if (picked == "next") SetSel((sel + 1) % n);
            else if (picked == "pick")
            {
                myKit = picks[sel].Kit;
                SBFx.Bell(arena.transform.position);
                if (arcade) { arcStage = 0; arcKits.Clear(); HabitReset(); ArcadeOpp(); }
                BeginIntro(); return;
            }
            // 选中的往玩家这边走出来一步、放大；其余退回原位
            var toMe = arena.transform.TransformDirection(Vector3.back);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f);
            for (int i = 0; i < n; i++)
            {
                if (!picks[i]) continue; bool on = i == sel;
                var want = selBase[i] + (on ? toMe * 0.095f : Vector3.zero);
                var rp = picks[i].Root.position; want.y = rp.y;
                picks[i].Root.position = Vector3.Lerp(rp, want, k);
                picks[i].Root.localScale = Vector3.Lerp(picks[i].Root.localScale, Vector3.one * (on ? 1.1f : 0.94f), k);
            }
            if (selDisc && picks[sel])
            {
                var dp = picks[sel].Root.position; dp.y = arena.transform.position.y + 0.0012f; selDisc.position = dp;
                float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f);
                selDisc.localScale = new Vector3(0.085f * pulse, 0.0012f, 0.085f * pulse);
            }
        }

        // ───────── 出场 / 回合 ─────────
        // ───────── 大厅：匹配对手 / 去挑战榜发起挑战 ─────────
        enum Lb { Home, Search, Found, Board, Call, Watch }
        Lb lb; float lbT, lbDur, busyAt; string busyName = ""; bool foundFromMatch;

        void BeginLobby()
        {
            bool keepShow = st == St.Title && picks.Count == 2;   // 从标题过来：笼子里的表演赛接着打
            st = St.Lobby; stT = 0; lobbyAp = false; arcade = false; sayQ.Clear(); stage.ClearSpeech();
            stage.ClearFollow(); arena.ShowScreen(false);
            if (!keepShow) { ClearFighters(); SpawnExhibition(); }
            LobbyHome();
        }

        // 大厅首页：榜单停在你自己这一块，两个入口——匹配、挑战榜
        void LobbyHome()
        {
            lb = Lb.Home; lbT = 0; stage.ClearTickets();
            cur = SBLadder.MyRank; stage.ShowLadder(cur);
            stage.AddTicket("match", SBUI.L("匹配", "MATCH"), SBUI.Red, SBUI.Cream, -0.075f, 0.042f, 0, 0.15f);
            stage.AddTicket("board", SBUI.L("挑战榜", "LADDER"), SBUI.Cobalt, SBUI.Cream, -0.023f, 0.054f, 0, 0.2f);
            stage.AddTicket("watch", SBUI.L("观战", "WATCH"), SBUI.Gold, SBUI.Ink, 0.029f, 0.042f, 0, 0.25f);
            stage.AddTicket("back", SBUI.L("返回", "BACK"), SBUI.Cream, SBUI.Ink, 0.075f, 0.042f, 0, 0.3f);
        }

        // 挑战榜：上下翻，翻到谁就能向谁发起挑战
        void LobbyBoard(int at = 0)
        {
            lb = Lb.Board; lbT = 0; stage.ClearTickets();
            int me = SBLadder.MyRank; cur = at > 0 ? at : me > 1 ? me - 1 : 2;
            stage.ShowLadder(cur);
            const float w = 0.045f, step = 0.049f;
            stage.AddTicket("up", SBUI.L("往上", "UP"), SBUI.Cream, SBUI.Ink, -1.5f * step, w, 0, 0.15f);
            stage.AddTicket("go", SBUI.L("挑战", "FIGHT"), SBUI.Red, SBUI.Cream, -0.5f * step, w, 0, 0.2f);
            stage.AddTicket("down", SBUI.L("往下", "DOWN"), SBUI.Cream, SBUI.Ink, 0.5f * step, w, 0, 0.25f);
            stage.AddTicket("home", SBUI.L("返回", "BACK"), SBUI.Ink, SBUI.Cream, 1.5f * step, w, 0, 0.3f);
        }

        // 匹配：像真的在找人——要等几秒；有时先找到的那位正在比赛中，接着找
        void LobbySearch()
        {
            lb = Lb.Search; lbT = 0; lbDur = Random.Range(2.4f, 4.8f);
            matchTarget = SBLadder.Match();
            busyAt = Random.value < 0.35f ? Random.Range(0.8f, lbDur - 1.5f) : -1f;
            int r; do r = Random.Range(1, SBLadder.Count + 1); while (r == matchTarget || SBLadder.IdAt(r) == SBLadder.You);
            var bp = SBLadder.PilotAt(r); busyName = SBUI.En ? bp.en : bp.zh;
            stage.ClearTickets(); stage.ShowSearching(SBUI.L("寻找对手", "FINDING A RIVAL"));
            stage.AddTicket("cancel", SBUI.L("取消", "CANCEL"), SBUI.Cream, SBUI.Ink, 0f, 0.058f, 0, 0.3f);
        }

        // 找到了 / 对方接受了挑战：亮出这个人和他这一场带的木偶（三只常用的里随机）
        void LobbyFound(bool fromMatch)
        {
            lb = Lb.Found; lbT = 0; foundFromMatch = fromMatch;
            oppPilot = SBLadder.PilotAt(cur); oppKit = oppPilot.PickPuppet();
            stage.ClearTickets(); stage.ShowFound(oppPilot, cur, oppKit);
            stage.Slam(fromMatch ? SBUI.L("找到对手！", "RIVAL FOUND!") : SBUI.L("对方接受了挑战！", "CHALLENGE ACCEPTED!"), SBUI.Ink, SBUI.Gold, 0.028f, 1.3f, -5f, 0.14f);
            SBFx.Bell(arena.transform.position); arena.Excite(0.5f);
            Debug.Log($"[SB] {(fromMatch ? "匹配到" : "挑战")} 第 {cur} 名 {oppPilot.zh}（带 {oppKit.zh}）");
            stage.AddTicket("accept", SBUI.L("应战", "FIGHT"), SBUI.Red, SBUI.Cream, -0.066f, 0.058f, 0, 0.5f);
            if (fromMatch) stage.AddTicket("again", SBUI.L("换一个", "ANOTHER"), SBUI.Cobalt, SBUI.Cream, 0f, 0.062f, 0, 0.58f);
            stage.AddTicket(fromMatch ? "home" : "toboard", SBUI.L("返回", "BACK"), SBUI.Cream, SBUI.Ink, fromMatch ? 0.066f : 0.03f, 0.058f, 0, 0.66f);
        }

        void UpdateLobby(string picked)
        {
            ShowFight();
            lbT += Time.unscaledDeltaTime; int n = SBLadder.Count;
            string dots = new string('.', 1 + (int)(lbT * 2.5f) % 3);
            switch (lb)
            {
                case Lb.Home:
                    if (autoPilot && lbT > 2f) picked = "match";
                    if (picked == "match") LobbySearch();
                    else if (picked == "board") LobbyBoard();
                    else if (picked == "watch") LobbyWatch();
                    else if (picked == "back") EnterTitle();
                    break;
                case Lb.Watch:
                    if (picked == "up") { if (watchCur > 0) { watchCur--; stage.ShowBouts(bouts, watchCur); } }
                    else if (picked == "down") { if (watchCur < bouts.Count - 1) { watchCur++; stage.ShowBouts(bouts, watchCur); } }
                    else if (picked == "home") LobbyHome();
                    else if (picked == "see") BeginWatch(bouts[watchCur]);
                    break;
                case Lb.Search:
                    if (picked == "cancel") { LobbyHome(); break; }
                    bool busy = busyAt > 0 && lbT > busyAt && lbT < busyAt + 1.3f;
                    stage.SearchStatus(SBUI.L("寻找中", "SEARCHING") + dots,
                        busy ? SBUI.L($"{busyName} 正在比赛中，继续寻找", $"{busyName} is in a bout. Still looking")
                             : SBUI.L($"在名次相近的操偶师里找 · 已等待 {(int)lbT} 秒", $"Looking near your rank · {(int)lbT}s"));
                    if (lbT >= lbDur) { cur = matchTarget; LobbyFound(true); }
                    break;
                case Lb.Call:
                    if (picked == "cancel") { LobbyBoard(cur); break; }
                    { var cp = SBLadder.PilotAt(cur); stage.SearchStatus(SBUI.En ? cp.en : cp.zh, SBUI.L("已发出挑战，等待对方回应", "Challenge sent. Waiting for a reply") + dots); }
                    if (lbT >= lbDur) LobbyFound(false);
                    break;
                case Lb.Found:
                    if (autoPilot && lbT > 1.8f) picked = "accept";
                    if (picked == "accept") { SBFx.Bell(arena.transform.position); BeginSelect(); }
                    else if (picked == "again") LobbySearch();
                    else if (picked == "home") LobbyHome();
                    else if (picked == "toboard") LobbyBoard(cur);
                    break;
                case Lb.Board:
                    if (picked == "up") { if (cur > 1) { cur--; stage.ShowLadder(cur); } }
                    else if (picked == "down") { if (cur < n) { cur++; stage.ShowLadder(cur); } }
                    else if (picked == "home") LobbyHome();
                    else if (picked == "go")
                    {
                        if (SBLadder.PilotAt(cur) == null) { stage.Slam(SBUI.L("这是你自己", "That's you"), SBUI.Cream, SBUI.Ink, 0.024f, 1f, 3f, 0.1f); break; }
                        // 发起挑战：等对方回应一两秒
                        lb = Lb.Call; lbT = 0; lbDur = Random.Range(1.3f, 2.4f);
                        stage.ClearTickets(); stage.ShowSearching(SBUI.L("发起挑战", "CHALLENGE"));
                        stage.AddTicket("cancel", SBUI.L("取消", "CANCEL"), SBUI.Cream, SBUI.Ink, 0f, 0.058f, 0, 0.3f);
                    }
                    break;
            }
        }

        // ───────── 台词 ─────────
        Vector3 ViewRight(Vector3 at) { var v = at - head.position; v.y = 0; v = v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; return Vector3.Cross(Vector3.up, v); }
        // 木偶说一句：字幕纸条跟着它，有配音就从它身上发声
        void Say(SBPuppet p, string ev, Vector3 off, Transform target = null, float dur = 2.4f)
        {
            if (!p) return;
            var (text, idx) = SBLines.Pick(p.Kit.id, ev); if (text == null) return;
            var clip = SBLines.Clip(p.Kit.id, ev, idx);
            if (clip) { SBFx.Voice(clip, p.Head.position, p.GetHashCode()); dur = Mathf.Max(dur, clip.length + 0.6f); }   // 字幕至少留到这句话说完
            stage.Speak(target ? target : p.Root, text, off, dur);
            Debug.Log($"[SB] 台词 {p.Kit.zh}（{ev}{(clip ? "，有配音" : "")}）：{text}");
        }
        // 一小局打完（final = 整场也分出了胜负）：“K.O.!”大字收掉之后才说（赢了先说，输了再晚一点）。纸条挂在各自身子外侧、胸口高度（比场中大字和结果牌都低，互相不碰）
        void MatchLines(SBPuppet w, SBPuppet l, bool final)
        {
            // 只有我们自己的木偶说话：赢了说赢的那句，输了说输的那句；对手不出声，观战时谁也不说
            if (watching || !me) return;
            var right = ViewRight(arena.transform.position); bool won = w == me; var other = won ? l : w;
            float side = Vector3.Dot(me.Root.position - other.Root.position, right) >= 0 ? 1f : -1f;
            sayQ.Add((Time.unscaledTime + (won ? 1.4f : 1.7f), () => Say(me, final ? (won ? "win" : "lose") : (won ? "rwin" : "rlose"), right * side * 0.115f + Vector3.up * (won ? 0.1f : 0.06f), me.Root, 2.2f)));
        }
        void SayTick()
        {
            for (int i = sayQ.Count - 1; i >= 0; i--) if (Time.unscaledTime >= sayQ[i].at) { var g = sayQ[i].go; sayQ.RemoveAt(i); g(); }
        }

        // ───────── 闯关：夺金腰带之路 ─────────
        void BeginArcade()
        {
            arcade = true; watching = false; oppPilot = null; oppKit = null; arcStage = 0; arcKits.Clear();
            BeginSelect();
            arcade = true;
        }
        // 这一战打谁：前五战是另外五只木偶（按图鉴顺序），最后一战是和你一模一样的那只
        void ArcadeOpp()
        {
            if (arcKits.Count == 0) { foreach (var k in SBKit.All) if (k.id != myKit.id) arcKits.Add(k); arcKits.Add(SBKit.Get(myKit.id)); }
            if (ArcFinal || mirrorPilot == null) mirrorPilot = BuildMirror();   // 走到最后一战时，按前面记下的打法现做
            oppKit = arcKits[arcStage];
            oppPilot = ArcFinal ? mirrorPilot : SBPilot.Get(ArcPilots[Mathf.Min(arcStage, ArcPilots.Length - 1)]);
        }
        // 闯关的一战打完：赢了去下一战（最后一战赢了领金腰带）；输了原地再战
        void ArcadeFinish()
        {
            iWon = winsMe >= 2;
            Debug.Log($"[SB] 闯关 第 {arcStage + 1} 战 {(iWon ? "胜" : "负")}");
            if (iWon && ArcFinal) { EnterChampion(); return; }
            st = St.Result; stT = 0;
            arena.ShowScreen(false);
            SBKit nk = iWon ? arcKits[arcStage + 1] : null; SBPilot np = iWon ? (arcStage + 1 == arcKits.Count - 1 ? mirrorPilot : SBPilot.Get(ArcPilots[Mathf.Min(arcStage + 1, ArcPilots.Length - 1)])) : null;
            stage.ShowResult(iWon, myKit, nk, false, np, iWon ? SBUI.L($"第 {arcStage + 2} 战：{nk.zh}", $"BOUT {arcStage + 2}: {nk.en}") : null);
            if (iWon) { SBFx.Cheer(arena.transform.position); arena.Excite(1f); }
            if (iWon) stage.AddTicket("next", SBUI.L("下一场", "NEXT"), SBUI.Red, SBUI.Cream, -0.036f, 0.062f, 0, 0.5f);
            else stage.AddTicket("retry", SBUI.L("再战", "REMATCH"), SBUI.Red, SBUI.Cream, -0.036f, 0.062f, 0, 0.5f);
            stage.AddTicket("menu", SBUI.L("回标题", "TITLE"), SBUI.Cream, SBUI.Ink, 0.036f, 0.058f, 0, 0.6f);
        }

        // ───────── 观战：精彩对局 ─────────
        // 现在约好的三场：榜首两位的冠军战（你是榜首时，是争夺挑战你的资格）、中段一场相邻名次的升位战、一场隔着好几名的以下犯上
        void MakeBouts()
        {
            bouts.Clear();
            var ps = new List<SBPilot>();
            for (int r = 1; r <= SBLadder.Count; r++) { var p = SBLadder.PilotAt(r); if (p != null) ps.Add(p); }
            if (ps.Count < 12) return;
            SBBout B(SBPilot a, SBPilot b, string tz, string te, string lz, string le, bool title = false)
                => new SBBout { a = a, b = b, ka = a.PickPuppet(), kb = b.PickPuppet(), tagZh = tz, tagEn = te, lineZh = lz, lineEn = le, title = title };
            bool top = SBLadder.Rank(ps[0].id) == 1;
            bouts.Add(top ? B(ps[0], ps[1], "冠军战", "TITLE BOUT", "赢的人就是擂台之王，金腰带当场颁发", "The winner is champion and takes the gold belt", true)
                          : B(ps[0], ps[1], "挑战权之战", "CONTENDERS", "你是擂台之王。这两位在争下一个挑战你的资格", "You hold the title. These two fight for the next shot at you"));
            int i = Random.Range(3, 8);
            bouts.Add(B(ps[i], ps[i + 1], "升位战", "RANK BOUT", "两人名次挨着，谁赢谁排前面", "Neighbours on the ladder. The winner takes the higher spot"));
            int j, up, guard = 0;
            do { j = Random.Range(9, ps.Count); up = Mathf.Max(2, j - Random.Range(3, 6)); } while ((up == i || up == i + 1 || j == i || j == i + 1) && ++guard < 20);
            bouts.Add(B(ps[up], ps[j], "以下犯上", "UPSET BID", $"{ps[j].zh}隔着好几名来挑战，赢了两人换位", $"{ps[j].en} challenges from far below. A win swaps their places"));
        }

        // 精彩对局看板：上下翻，翻到哪一场就能进去看
        void LobbyWatch()
        {
            lb = Lb.Watch; lbT = 0; stage.ClearTickets();
            MakeBouts(); watchCur = 0;
            if (bouts.Count == 0) { LobbyHome(); return; }
            stage.ShowBouts(bouts, watchCur);
            const float w = 0.045f, step = 0.049f;
            stage.AddTicket("up", SBUI.L("往上", "UP"), SBUI.Cream, SBUI.Ink, -1.5f * step, w, 0, 0.15f);
            stage.AddTicket("see", SBUI.L("观看", "WATCH"), SBUI.Red, SBUI.Cream, -0.5f * step, w, 0, 0.2f);
            stage.AddTicket("down", SBUI.L("往下", "DOWN"), SBUI.Cream, SBUI.Ink, 0.5f * step, w, 0, 0.25f);
            stage.AddTicket("home", SBUI.L("返回", "BACK"), SBUI.Ink, SBUI.Cream, 1.5f * step, w, 0, 0.3f);
        }

        // 开始看一场：两只木偶各由一位操偶师的“大脑”操纵，头顶各一副提线木架；a 先在你这一侧
        void BeginWatch(SBBout b)
        {
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            watching = true; watchSide = 1; watchBout = b; shownSayT = Time.time;
            me = Spawn(b.ka, new Vector3(0, 0, -0.1f), ArenaYaw);
            cpu = Spawn(b.kb.id == b.ka.id ? b.kb.WithGlove(new Color(.14f, .3f, .78f)) : b.kb, new Vector3(0, 0, 0.1f), ArenaYaw + 180f);
            me.opponent = cpu.Root; cpu.opponent = me.Root; me.foe = cpu; cpu.foe = me;
            ai.self = cpu; ai.foe = me; ai.SetPilot(b.b, 0);
            ai2.self = me; ai2.foe = cpu; ai2.SetPilot(b.a, 0);
            winsMe = winsCpu = 0; round = 0;
            st = St.Intro; stT = 0;
            arena.ShowScreen(false);
            stage.ShowWatchVS(b.a, b.ka, b.b, b.kb, SBUI.En ? b.tagEn : b.tagZh, b.title);
            arena.Excite(0.7f); SBFx.Cheer(arena.transform.position);
            watchRig = true; pilotHand.Snap();
            cpuBar.position = cpu.Root.position + Vector3.up * BarH; meBar.position = me.Root.position + Vector3.up * BarH;
            Debug.Log($"[SB] 观战 {b.tagZh}：第 {SBLadder.Rank(b.a.id)} 名 {b.a.zh}（{b.ka.zh}）对 第 {SBLadder.Rank(b.b.id)} 名 {b.b.zh}（{b.kb.zh}）");
        }

        // 两位操偶师的名牌：被关注的那位挂在他的手上方（放大），另一位挂在提线木架上方
        void WatchTags()
        {
            stage.ClearFollow();
            string na = SBUI.En ? watchBout.a.en : watchBout.a.zh, nb = SBUI.En ? watchBout.b.en : watchBout.b.zh;
            stage.FollowTag(watchSide > 0 ? pilotHand.nameAt : meBar, na, SBUI.Red, watchSide > 0 ? 0f : 0.032f);
            stage.FollowTag(watchSide < 0 ? pilotHand.nameAt : cpuBar, nb, SBUI.Cobalt, watchSide < 0 ? 0f : 0.032f);
        }

        // 换视角：像转播切机位——“操偶手”换到另一位操偶师的木偶上方，你看到的就是他怎么动手指；两只木偶同时绕擂台中心对调位置，被关注的那只换到你这一侧
        void WatchSwap()
        {
            if (!me || !cpu || me.KO || cpu.KO || me.Ulting || cpu.Ulting || swapT > 0) return;
            watchSide = -watchSide;
            var c = arena.transform.position;
            // 不是一下子对调：两只木偶绕擂台中心转半圈（0.45 秒，像转台转过去），这段时间比赛停一下
            swapT = 0.0001f; swapFrom[0] = me.Root.position; swapFrom[1] = cpu.Root.position; swapYaw[0] = me.Root.eulerAngles.y; swapYaw[1] = cpu.Root.eulerAngles.y;
            WatchTags(); shownSayT = Time.time;
            var near = watchSide > 0 ? watchBout.a : watchBout.b;
            stage.Slam(SBUI.L($"{near.zh} 的视角", $"{near.en}'s side"), SBUI.Cream, watchSide > 0 ? SBUI.Red : SBUI.Cobalt, 0.024f, 1f, watchSide > 0 ? -4f : 4f, 0.1f);
            SBFx.Whoosh(c, 0.8f);
            Debug.Log($"[SB] 观战换边 近侧 {near.zh}");
        }

        float swapT; readonly Vector3[] swapFrom = new Vector3[2]; readonly float[] swapYaw = new float[2];
        void SwapTick()
        {
            if (!me || !cpu) { swapT = 0; return; }
            swapT += Time.unscaledDeltaTime; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(swapT / 0.45f)), ang = 180f * k;
            var c = arena.transform.position; var q = Quaternion.Euler(0, ang, 0); var ps = new[] { me, cpu };
            for (int i = 0; i < 2; i++) { var o = swapFrom[i] - c; o.y = 0; var w = c + q * o; ps[i].Place(new Vector3(w.x, c.y, w.z), swapYaw[i] + ang); }
            meBar.position = me.Root.position + Vector3.up * BarH; cpuBar.position = cpu.Root.position + Vector3.up * BarH;
            if (swapT >= 0.45f) swapT = 0;
        }

        // 一场看完：两人按结果换位；冠军战的赢家上领奖台领金腰带，其余的出结果牌
        void WatchFinish()
        {
            bool aWon = winsMe >= 2; var b = watchBout;
            var win = aWon ? b.a : b.b; var lose = aWon ? b.b : b.a;
            var rp = SBLadder.PilotBout(win.id, lose.id);
            string wn = SBUI.En ? win.en : win.zh;
            Debug.Log($"[SB] 观战结束 {win.zh} 胜 {lose.zh} 名次 {rp.before} → {rp.after}");
            if (b.title)
            {
                if (!aWon) (me, cpu) = (cpu, me);   // 领奖的是赢家
                champWho = wn; champSub = rp.before == 1 ? SBUI.L("卫冕成功\n第 1 名", "Title\ndefended") : SBUI.L("新的\n擂台之王", "New\nchampion");
                EnterChampion();
                return;
            }
            watchRig = false; stage.ClearFollow();   // 结果牌放下来：操偶手、木架、名牌都收掉，不挡牌子
            st = St.Result; stT = 0;
            arena.ShowScreen(false);
            string line = rp.after != rp.before ? SBUI.L($"名次 {rp.before} → {rp.after}", $"RANK {rp.before} → {rp.after}") : SBUI.L($"守住了第 {rp.after} 名", $"Held rank {rp.after}");
            stage.ShowResult(true, aWon ? b.ka : b.kb, null, false, null, line, SBUI.L("胜者 · " + wn, "WINNER · " + wn));
            SBFx.Cheer(arena.transform.position); arena.Excite(1f);
            stage.AddTicket("more", SBUI.L("再看一场", "ANOTHER"), SBUI.Red, SBUI.Cream, -0.066f, 0.058f, 0, 0.5f);
            stage.AddTicket("lobby", SBUI.L("擂台榜", "LADDER"), SBUI.Cobalt, SBUI.Cream, 0.0f, 0.062f, 0, 0.6f);
            stage.AddTicket("menu", SBUI.L("回标题", "TITLE"), SBUI.Cream, SBUI.Ink, 0.066f, 0.058f, 0, 0.7f);
        }

        void BeginIntro()
        {
            ClearFighters(); stage.ClearTickets(); stage.ClearFollow();
            if (oppPilot == null) oppPilot = SBLadder.PilotAt(SBLadder.Match()) ?? SBPilot.All[0];
            if (oppKit == null) oppKit = oppPilot.PickPuppet();
            var opp = oppKit;
            me = Spawn(myKit, new Vector3(0, 0, -0.1f), ArenaYaw);
            // 对手和你同一只：换一副蓝拳套好分清；闯关最后那只“另一个你”戴黑拳套
            cpu = Spawn(opp.id == myKit.id ? opp.WithGlove(ArcFinal ? new Color(.1f, .09f, .1f) : new Color(.14f, .3f, .78f)) : opp, new Vector3(0, 0, 0.1f), ArenaYaw + 180f);
            me.opponent = cpu.Root; cpu.opponent = me.Root; me.foe = cpu; cpu.foe = me;
            var pilot = debugPilot ?? oppPilot;
            cpu.dmgOut = 1f; cpu.dmgIn = 1f;   // 伤害和血量对谁都一样：对手强不强，只看他应对得对不对
            if (arcade) { me.dmgIn = 0.7f; cpu.dmgIn = ArcFinal ? 0.6f : 0.7f; }   // 闯关：双方血都更厚（挨打只掉七成）；最后那只再厚一点
            mirrorSaid = false; champSaid = false; sayQ.Clear(); stage.ClearSpeech();
            // 擂台榜第 1 名（不管现在坐在榜首的是哪一位）：换成“擂台之王”的打法，非常非常难赢
            // 榜上第 2–4 名、闯关最后三战：更会躲你的攻击（越靠前 / 越靠后越会躲）
            if (arcade) { float sl = arcStage >= arcKits.Count - 1 ? 0.55f : arcStage == arcKits.Count - 2 ? 0.4f : arcStage == arcKits.Count - 3 ? 0.25f : 0f; if (sl > 0) pilot = pilot.Dodgy(sl); }
            else if (pilot.zh != "")
            {
                int rk = SBLadder.Rank(pilot.id);
                if (rk == 1) pilot = pilot.Champion(); else if (rk == 2) pilot = pilot.Dodgy(0.5f); else if (rk == 3) pilot = pilot.Dodgy(0.4f); else if (rk == 4) pilot = pilot.Dodgy(0.3f);
            }
            ai.self = cpu; ai.foe = me; ai.SetPilot(pilot, arcade ? 0 : SBLadder.Level(pilot.id));
            if (arcade) Debug.Log($"[SB] 闯关 第 {arcStage + 1} / {arcKits.Count} 战 对手：{pilot.zh} 操纵 {opp.zh}"); else
            Debug.Log($"[SB] 排位赛 对手：第 {SBLadder.Rank(pilot.id)} 名 {pilot.zh}（水平 {pilot.tier}，记性 {SBLadder.Level(pilot.id)}）操纵 {opp.zh}");
            winsMe = winsCpu = 0; round = 0;
            st = St.Intro; stT = 0;
            arena.ShowScreen(false);
            if (arcade) stage.ShowVS(myKit, opp, arcStage, arcKits.Count, null, ArcFinal ? (SBUI.En ? mirrorPilot.habitEn : mirrorPilot.habitZh) : null);   // 闯关不写操偶师的名字
            else stage.ShowVS(myKit, opp, SBLadder.Rank(pilot.id), 0, pilot);
            arena.Excite(0.7f); SBFx.Cheer(arena.transform.position);
            cpuBar.gameObject.SetActive(true);
            cpuBar.position = cpu.Root.position + Vector3.up * BarH;

        }

        void NextRound()
        {
            round++;
            if (watching) { if (round == 1) WatchTags(); watchRoundT = 0; watchOT = false; }
            else if (round == 1 && !arcade && ai && ai.Pilot != null && ai.Pilot.zh != "") stage.FollowTag(cpuBar, SBUI.En ? ai.Pilot.en : ai.Pilot.zh, SBUI.Cobalt, 0.032f);   // 开打后，对面提线木架上挂着操偶师的名字
            me.ResetHealth(); cpu.ResetHealth(); me.victory = cpu.victory = false;
            float sd = watching ? watchSide : 1;   // 观战换过边：两人的站位跟着对调
            me.Place(AP(0, -0.1f * sd), ArenaYaw + (sd > 0 ? 0f : 180f));
            cpu.Place(AP(0, 0.1f * sd), ArenaYaw + (sd > 0 ? 180f : 0f));
            st = St.RoundStart; stT = 0;
            arena.ShowScreen(true);
            stage.Slam(SBUI.L($"第 {round} 回合", $"ROUND {round}"), SBUI.Cream, SBUI.Ink, 0.04f, 1.4f, -4f);
        }

        void EnterResult()
        {
            st = St.Result; stT = 0;
            arena.ShowScreen(false);
            string line = rankAfter != rankBefore ? SBUI.L($"名次 {rankBefore} → {rankAfter}", $"RANK {rankBefore} → {rankAfter}")
                        : iWon ? (rankAfter == 1 ? SBUI.L("卫冕成功 · 第 1 名", "Title defended · #1") : SBUI.L($"守住了第 {rankAfter} 名", $"Held rank {rankAfter}"))
                               : SBUI.L($"名次不变 · 第 {rankAfter} 名", $"Still rank {rankAfter}");
            stage.ShowResult(iWon, myKit, null, false, null, line);
            if (iWon) { SBFx.Cheer(arena.transform.position); arena.Excite(1f); }
            stage.AddTicket("retry", SBUI.L("再战", "REMATCH"), SBUI.Red, SBUI.Cream, -0.066f, 0.058f, 0, 0.5f);
            stage.AddTicket("lobby", SBUI.L("擂台榜", "LADDER"), SBUI.Cobalt, SBUI.Cream, 0.0f, 0.062f, 0, 0.6f);
            stage.AddTicket("menu", SBUI.L("回标题", "TITLE"), SBUI.Cream, SBUI.Ink, 0.066f, 0.058f, 0, 0.7f);
            Debug.Log($"[SB] 结算 {(iWon ? "胜" : "败")} 名次 {rankBefore} → {rankAfter}");
        }

        // 夺冠仪式：对手被抬走，冠军走到台中央、面朝你高举金腰带一下一下地跳；彩纸一波一波地落、全场一阵一阵地欢呼
        void EnterChampion()
        {
            st = St.Champion; stT = 0; champT = 0; champConf = 0; champCheer = 0; champTickets = false;
            stage.ClearTickets(); stage.ClearFollow();
            arena.ShowScreen(false);
            if (cpu) { Destroy(cpu.gameObject); cpu = null; }
            if (cpuBar) cpuBar.gameObject.SetActive(false);
            if (meBar) meBar.gameObject.SetActive(false);
            watchRig = false; if (pilotHand) pilotHand.gameObject.SetActive(false);
            if (me)
            {
                // 冠军走上台中央偏前的领奖台，放大一号，面朝你
                var spot = AP(0, -0.05f);
                BuildPodium(spot);
                me.ResetHealth(); me.foe = null; me.Place(spot, ArenaYaw + 180f); me.floorY = arena.transform.position.y + PodiumH;
                me.opponent = head; me.victory = true; me.Root.localScale = Vector3.one * 1.3f;   // 缩放木偶自己（以脚下为中心），不是缩放它外面那层
                if (belt) { Destroy(belt.gameObject); belt = null; }
            }
            if (watching) stage.ShowChampion(me ? me.Kit : myKit, champWho, champSub);
            else if (arcade) stage.ShowChampion(myKit, null, SBUI.L("打遍六只\n金腰带到手", "Beat all six\nThe belt is yours"));
            else stage.ShowChampion(myKit);
            champSaid = false;
            stage.Confetti(140);
            stage.Slam(SBUI.L("擂台之王！", "CHAMPION!"), SBUI.Ink, SBUI.Gold, 0.07f, 3f, -6f);
            SBFx.Bell(arena.transform.position); SBFx.Cheer(arena.transform.position); arena.Excite(1f);
            Debug.Log("[SB] 擂台之王");
        }

        // 领奖台：两级圆台，黑漆台身 + 一圈金边 + 红毯台面
        void BuildPodium(Vector3 world)
        {
            if (podium) Destroy(podium.gameObject);
            podium = new GameObject("领奖台").transform; podium.SetParent(arena.transform, false);
            podium.position = new Vector3(world.x, arena.transform.position.y, world.z);
            var lacq = new Material(litTemplate) { color = new Color(.08f, .07f, .07f) }; lacq.SetFloat("_Smoothness", 0.6f);
            var gold = new Material(litTemplate) { color = new Color(1.1f, .78f, .28f) }; gold.SetFloat("_Metallic", 0.45f); gold.SetFloat("_Smoothness", 0.6f);
            var red = new Material(litTemplate) { color = new Color(.95f, .12f, .1f) }; red.SetFloat("_Smoothness", 0.12f);   // 红绒台面
            var g = new SBGeo(); g.Lathe(Vector3.zero, new[] { new Vector2(0.078f, 0f), new Vector2(0.078f, 0.006f), new Vector2(0.066f, 0.006f), new Vector2(0.066f, PodiumH - 0.0012f) }, 40, 0f, false, false); g.Make(podium, "台身", lacq);
            var e = new SBGeo();
            e.Lathe(Vector3.zero, new[] { new Vector2(0.0785f, 0.0045f), new Vector2(0.0795f, 0.0052f), new Vector2(0.0785f, 0.0062f), new Vector2(0.066f, 0.0062f) }, 40, 0f, false, false);
            e.Lathe(Vector3.zero, new[] { new Vector2(0.0665f, PodiumH - 0.0028f), new Vector2(0.0675f, PodiumH - 0.002f), new Vector2(0.0665f, PodiumH - 0.001f), new Vector2(0.062f, PodiumH - 0.001f) }, 40, 0f, false, false);
            e.Make(podium, "金边", gold);
            var t = new SBGeo(); t.Lathe(Vector3.zero, new[] { new Vector2(0.063f, PodiumH - 0.001f), new Vector2(0.06f, PodiumH), new Vector2(0f, PodiumH) }, 40, 0f, false, false, 3f); t.Make(podium, "红毯", red);
            podium.gameObject.AddComponent<SBPop>().dur = 0.5f;
        }
        const float PodiumH = 0.016f;

        void ChampTick(float udt)
        {
            champT += udt;
            // 腰带降下来、站稳之后，冠军说一句（纸条在木偶右边、腰带下面，不碰腰带）
            if (!champSaid && champT > 2.6f && me && !watching) { champSaid = true; Say(me, "champ", ViewRight(me.Root.position) * 0.17f + Vector3.up * 0.1f, me.Root, 3.2f); }
            // 金腰带：冠军举起双手站稳之后，腰带挂在两根提线上从上面降下来，悬在拳套前面（留出空隙，不和木偶接触），轻轻摆动
            if (me && !belt && champT > 0.7f)
            {
                float sc = me.Root.lossyScale.x; Vector3 a = me.GlovePos(false), b = me.GlovePos(true), rp = me.Root.position;
                var f = head.position - rp; f.y = 0; f = f.sqrMagnitude < 1e-6f ? Vector3.back : f.normalized;
                float gr = me.GloveR * sc;
                float fwd = Mathf.Max(Vector3.Dot(a - rp, f), Vector3.Dot(b - rp, f)) + gr;          // 拳套最靠前的那一点
                float half = Mathf.Clamp(Vector3.Distance(a, b) * 0.5f + 0.012f, 0.05f, 0.1f);       // 两个吊点比两只拳套略宽
                float sag = half * 0.3f, plateR = 0.03f;
                // 高度：吊点和拳套差不多高；矮胖的角色（不倒翁、秤砣）手举不过头，就挂到头顶上方，不挡脸
                float hangY = Mathf.Max((a.y + b.y) * 0.5f + 0.004f, me.HeadTop.position.y + sag + plateR + 0.01f);
                const float len = 0.34f;
                belt = SBBelt.Build(arena.transform, litTemplate, half, len);
                var at = rp + f * (fwd + 0.03f); at.y = hangY + len;                                 // 拳套前面再让出 3 厘米（金牌的厚度 + 空隙 + 跳起来的余量）
                beltHome = at; beltRot = Quaternion.LookRotation(-f, Vector3.up); beltT = 0;
                belt.position = beltHome + Vector3.up * 0.3f; belt.rotation = beltRot;
            }
            if (belt)
            {
                beltT += udt; float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(beltT / 1.1f), 3f);
                belt.position = beltHome + Vector3.up * (1f - k) * 0.3f;
                belt.rotation = beltRot * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.6f) * 1.6f * k);   // 绕提线顶端轻轻摆
            }
            if (champT < 16f)
            {
                if (champT > champConf) { stage.Confetti(40); champConf += 0.8f; }
                if (champT > champCheer) { SBFx.Cheer(arena.transform.position); arena.Excite(1f); champCheer += 2.4f; }
            }
            if (!champTickets && champT > 3.5f)
            {
                champTickets = true;
                stage.AddTicket("again", watching ? SBUI.L("再看一场", "ANOTHER") : SBUI.L("擂台榜", "LADDER"), SBUI.Red, SBUI.Cream, -0.036f, 0.062f, 0, 0f);
                stage.AddTicket("menu", SBUI.L("回标题", "TITLE"), SBUI.Cream, SBUI.Ink, 0.036f, 0.058f, 0, 0.1f);
            }
        }

        void SetPause(bool on)
        {
            userPaused = on; stage.ClearTickets();
            arena.ShowScreen(!on);   // 暂停时记分屏收起来，把位置让给招式提示
            if (on)
            {
                stage.AddTicket("resume", SBUI.L("继续", "RESUME"), SBUI.Red, SBUI.Cream, -0.066f, 0.058f, 0, 0f);
                stage.AddTicket("repick", arcade ? SBUI.L("换木偶", "RE-PICK") : SBUI.L("擂台榜", "LADDER"), SBUI.Cobalt, SBUI.Cream, 0f, 0.062f, 0, 0.08f);
                stage.AddTicket("menu", SBUI.L("回标题", "TITLE"), SBUI.Cream, SBUI.Ink, 0.066f, 0.058f, 0, 0.16f);
                SBFx.Ui(arena.transform.position, true);
            }
            Debug.Log($"[SB] 暂停 {on}");
        }

        void ClearFighters()
        {
            simA = null;
            foreach (var r in showAIs) if (r) Destroy(r.gameObject);
            showAIs.Clear();
            foreach (var p in picks) if (p) Destroy(p.gameObject);
            picks.Clear();
            if (selDisc) { Destroy(selDisc.gameObject); selDisc = null; }
            if (belt) { Destroy(belt.gameObject); belt = null; }
            if (podium) { Destroy(podium.gameObject); podium = null; }
            userPaused = false; netCharge = 0;
            if (me) Destroy(me.gameObject);
            if (cpu) Destroy(cpu.gameObject);
            me = cpu = null;
            if (cpuBar) cpuBar.gameObject.SetActive(false);
            if (meBar) meBar.gameObject.SetActive(false);
            watchRig = false; if (pilotHand) pilotHand.gameObject.SetActive(false);
            swapT = 0; watching = false; if (ai2) { ai2.self = ai2.foe = null; ai2.active = false; }
            Time.timeScale = 1f; slowT = 0;
        }

        // a 打 b：打出去那一段里拳头 / 脚碰到 b 的身体就算打中；蹲下躲拳、跳起躲腿（躲开也会记下来，练习场要用）
        void Resolve(SBPuppet a, SBPuppet b, string who)
        {
            if (!a.AttackLive(out var limb, out var power)) return;
            var atk = a.ActiveAttack;
            bool duck = b.Crouching && !SBGestures.IsKick(atk), hop = b.Lift > 0.012f && SBGestures.IsKick(atk);
            if (duck || hop)
            {
                // 算不算“躲开了”：只看水平方向——这一拳 / 这一脚要是你站着不动本来打得到，就算躲开（蹲下后拳头从头顶过去也算）
                var sp = a.StrikePoint; var bp = b.Root.position;
                if (HDist(bp, a.StrikeOrigin, sp) <= b.BodyR + a.StrikeRad + PuppetH * 0.06f) { a.MarkHit(); OnDodge(b, duck); }
                return;
            }
            // 打没打中：按拳 / 脚实际到的位置判（三维）。
            // 出招的那条肢体（肩 → 拳套、髋 → 脚）实际碰到对手的身子（大腿根到脖子这一段，粗细 = 身子半径）或者头，才算打中；
            // 从头顶上过去、从身边擦过去都不算。两个人贴在一起时，胳膊穿过对手身子也算碰到（不会出现“贴着却打不到”）。
            // 高个打矮个打得到，是因为出拳本身会瞄着对手胸口往下打，不是判定放水。
            {
                float tol = a.StrikeRad + PuppetH * 0.03f * Mathf.Sqrt(a.Kit.reach);
                var hipsP = b.Torso.position; var low = hipsP + (b.Root.position - hipsP) * 0.4f; var headP = b.Head.position;
                bool body = SegSeg(a.StrikeOrigin, a.StrikePoint, low, headP) <= b.BodyR + b.GuardPad + tol;                       // 举着盾：打在盾上也算打中（被挡）
                bool headHit = SegSeg(a.StrikeOrigin, a.StrikePoint, headP, headP) <= Mathf.Max(b.BodyR, b.H * 0.1f) + b.GuardPad + tol;
                if (!body && !headHit) return;
            }
            a.MarkHit();
            var dir = b.Root.position - a.Root.position; dir.y = 0;
            // 闪躲反击：刚躲开对手的招，这一下必中重创（挡不住）
            bool counter = a.CounterReady; if (counter) { power *= 1.8f; a.UseCounter(); }
            // 防守耐久：挡一下掉多少 = 力度；秤砣的重招掉得更多，铁甲将军的防守更耐打
            float drain = 13f * power * (SBGestures.IsHeavy(atk) ? 1f + a.Kit.guardBreak : 1f) / Mathf.Max(0.3f, b.Kit.guard);
            string defWas = b.DebugDef;
            bool blocked = b.TakeHit(dir.normalized, power, SBGestures.IsHeavy(atk) ? a.Kit.guardBreak : 0f, drain, counter);
            a.Landed(blocked);
            bool showFx = st == St.Fight || st == St.Tutorial;
            if (b.ConsumeBreakAway()) { SBFx.Shockwave(b.Root.position, 0.035f, SBUI.Cream); arena.Excite(0.2f); Debug.Log($"[SB] {(b == me ? "我" : "对手")}被连中三下，弹开"); }
            if (a.ConsumeDashHit() && !blocked)
            {
                if (showFx && !counter) stage.Slam(SBUI.L("弹射重击！", "SLINGSHOT!"), SBUI.Ink, SBUI.Gold, 0.03f, 0.8f, a == me ? -6f : 6f, 0.1f);
                arena.Excite(0.3f); Debug.Log($"[SB] {who}弹射重击 {atk}");
            }
            if (counter)
            {
                if (showFx) stage.Slam(SBUI.L("反击！", "COUNTER!"), SBUI.Ink, SBUI.Gold, 0.034f, 0.8f, a == me ? -7f : 7f, 0.1f);
                Time.timeScale = 0.45f; slowT = 0.2f; arena.Excite(0.35f); SBFx.Ui(limb.position, true);
                Debug.Log($"[SB] {who}反击 {atk}");
            }
            if (b.ConsumeBreak())
            {
                if (showFx) stage.Slam(b == me ? SBUI.L("被破防！", "GUARD BROKEN!") : SBUI.L("破防！", "GUARD BREAK!"), SBUI.Cream, SBUI.Red, 0.034f, 0.9f, b == me ? 7f : -7f, 0.1f);
                arena.Excite(0.3f); SBFx.Ui(b.Root.position, true);
                Debug.Log($"[SB] {who}打穿防守");
            }
            SBFx.Hit(limb.position, dir, power, blocked);
            a.AddMeter(blocked ? 4f : 11f * power); b.AddMeter((blocked ? 6f : 7f) * b.Kit.rageGain);
            if (a == me && !watching && !counter && a.Combo >= 2 && (st == St.Fight || st == St.Tutorial))
                stage.Slam(SBUI.L($"{a.Combo} 连击", $"{a.Combo} HITS"), SBUI.Cream, SBUI.Red, 0.022f, 0.6f, a.Combo % 2 == 0 ? 5f : -5f, 0.06f);
            arena.Excite(blocked ? 0.05f : 0.12f * power);
            Debug.Log($"[SB] {who}打中 {atk} {(blocked ? "被挡" : "")} 对方剩 {b.Health:0} ｜挨打方 {defWas}");
            if (st == St.Tutorial)
            {
                if (a == cpu && blocked) TutCount(5);
                if (a == cpu && !blocked && tutStep >= 5 && tutStep <= 7 && tutWait <= 0)
                    stage.Slam(SBUI.L("再来！", "AGAIN!"), SBUI.Cream, SBUI.Ink, 0.022f, 0.8f, 4f, 0.1f);
            }
        }

        /// <summary>空间里两条线段 p1-q1、p2-q2 之间的最近距离（线段退化成点也行）。</summary>
        static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2; float a = d1.sqrMagnitude, e = d2.sqrMagnitude, f = Vector3.Dot(d2, r), s, t;
            if (a <= 1e-10f && e <= 1e-10f) return r.magnitude;
            if (a <= 1e-10f) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-10f) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-10f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); } else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
        }

        /// <summary>水平面上，点 p 到线段 a-b 的距离。</summary>
        static float HDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var P = new Vector2(p.x, p.z); var A = new Vector2(a.x, a.z); var B = new Vector2(b.x, b.z); var ab = B - A;
            float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(P, A + ab * t);
        }

        // 练习场第 2~5 步（左拳 / 右拳 / 重招 / 踢）：做对了动作、又离木人桩够近，就算一次——不再要求判定上“打中”
        float tutHintT;
        void TutAct(SBGestures.Act a)
        {
            if (st != St.Tutorial || tutStep < 1 || tutStep > 4 || !cpu || !me || tutWait > 0) return;
            if (a == SBGestures.Act.Jump || a == SBGestures.Act.Duck || a == SBGestures.Act.Ult || a == SBGestures.Act.None) return;
            bool ok = tutStep == 1 ? a == SBGestures.Act.JabL : tutStep == 2 ? a == SBGestures.Act.JabR : tutStep == 3 ? SBGestures.IsHeavy(a) : SBGestures.IsKick(a);
            bool hint = Time.unscaledTime > tutHintT;
            if (!ok)
            {   // 动作做出来了，但不是这一步要的那个：告诉他刚才出的是什么
                if (hint && tutStep != 3)
                {
                    string zh = a == SBGestures.Act.JabL ? "这是左拳" : a == SBGestures.Act.JabR ? "这是右拳" : SBGestures.IsKick(a) ? "这是踢腿" : "这是重招";
                    string en = a == SBGestures.Act.JabL ? "That's a LEFT jab" : a == SBGestures.Act.JabR ? "That's a RIGHT jab" : SBGestures.IsKick(a) ? "That's a KICK" : "That's a HEAVY";
                    stage.Slam(SBUI.L(zh, en), SBUI.Cream, SBUI.Ink, 0.02f, 1f, 3f, 0.09f); tutHintT = Time.unscaledTime + 2f;
                }
                else if (hint && tutStep == 3) { stage.Slam(SBUI.L("按住别松，再松开", "HOLD, then release"), SBUI.Cream, SBUI.Ink, 0.02f, 1.1f, 3f, 0.09f); tutHintT = Time.unscaledTime + 2.5f; }
                return;
            }
            var d = cpu.Root.position - me.Root.position; d.y = 0;
            if (d.magnitude < me.Reach + cpu.BodyR + PuppetH * 0.5f) TutCount(tutStep);
            else if (hint) { stage.Slam(SBUI.L("再走近一点", "GET CLOSER"), SBUI.Cream, SBUI.Ink, 0.022f, 1f, 3f, 0.09f); tutHintT = Time.unscaledTime + 2f; }
        }

        void OnDodge(SBPuppet b, bool duck)
        {
            Debug.Log($"[SB] {(b == me ? "我" : "对手")}{(duck ? "蹲下" : "跳起")}躲开");
            arena.Excite(0.2f);
            b.GrantCounter(1.2f);   // 躲开了：1.2 秒内下一击是反击
            if (b == me && st == St.Fight) stage.Slam(SBUI.L("趁现在！", "NOW!"), SBUI.Ink, SBUI.Gold, 0.022f, 0.7f, -4f, 0.08f);
            if (b == me && st == St.Tutorial) TutCount(duck ? 6 : 7);
        }

        // 提线：你的木偶连到指尖；对手的木偶连到它头顶的十字木架（KO 时线断）
        Renderer[] handRs; float handTop, handGap = 9f;
        // 观战时的提线：被关注的那位用“操偶手”的五根手指牵线，另一位用十字木架
        void WatchRig()
        {
            if (!pilotHand) return;
            pilotHand.gameObject.SetActive(watchRig);
            void One(SBPuppet p, LineRenderer[] lines, Transform bar, SBRival brain, bool focus)
            {
                bool show = watchRig && p && !p.KO;
                for (int i = 0; i < 5; i++) lines[i].enabled = show;
                bar.gameObject.SetActive(watchRig && p && !focus);
                if (!watchRig || !p) return;
                float rt = brain && brain.self == p ? brain.Rattled : 0f;
                Transform[] ends = { p.WireL ? p.WireL : p.FistL, p.WireR ? p.WireR : p.FistR, p.HeadTop, p.KneeL, p.KneeR };
                if (focus)
                {
                    pilotHand.Tick(p, p == me ? cpu : me, arena.transform.position, head.position, rt, Time.deltaTime);
                    if (show) for (int i = 0; i < 5; i++) { lines[i].SetPosition(0, pilotHand.tips[i].position); lines[i].SetPosition(1, ends[i].position); }
                }
                else
                {
                    bar.position = Vector3.Lerp(bar.position, p.Root.position + Vector3.up * BarH, Time.deltaTime * 8f);
                    bar.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2f) * (6f + rt * 10f) + Mathf.Sin(Time.time * 27f) * rt * 6f, p.Root.eulerAngles.y, Mathf.Sin(Time.time * 1.6f) * (6f + rt * 10f) + Mathf.Sin(Time.time * 31f) * rt * 6f);
                    Vector3[] starts = { new(-0.045f, 0, 0), new(0.045f, 0, 0), new(0, 0, 0.03f), new(-0.02f, 0, -0.03f), new(0.02f, 0, -0.03f) };
                    if (show) for (int i = 0; i < 5; i++) { lines[i].SetPosition(0, bar.TransformPoint(starts[i])); lines[i].SetPosition(1, ends[i].position); }
                }
            }
            One(me, myLines, meBar, ai2, watchSide > 0);
            One(cpu, cpuLines, cpuBar, ai, watchSide < 0);
#if UNITY_EDITOR
            // 编辑器自查：操偶手最高到过哪（对战屏下沿 0.278）、和两只木偶的包围盒最近隔多远（负 = 叠上了）
            if (watchRig && me && cpu && st == St.Fight)
            {
                handRs ??= pilotHand.GetComponentsInChildren<Renderer>();
                var hb = handRs[0].bounds; foreach (var r in handRs) hb.Encapsulate(r.bounds);
                handTop = Mathf.Max(handTop, hb.max.y - arena.transform.position.y);
                // 木偶身上最容易碰到手的几处：头顶、两只拳套、两个膝盖（各按 2 厘米的球算）
                foreach (var p in new[] { me, cpu })
                {
                    var ks = new[] { p.HeadTop.position, p.GlovePos(false), p.GlovePos(true), p.KneeL.position, p.KneeR.position };
                    for (int ki = 0; ki < ks.Length; ki++)
                    {
                        float gp = pilotHand.Nearest(ks[ki]) - 0.02f;
                        if (gp < handGap - 0.003f && gp < 0.01f) Debug.Log($"[SB] 手距自查 {gp * 100f:0.0}cm {p.Kit.zh} 点{ki} 高{ks[ki].y - arena.transform.position.y:0.000} 手高{pilotHand.transform.position.y - arena.transform.position.y:0.000} 关注={(watchSide > 0 ? me : cpu).Kit.zh} 必杀={p.Ulting} 跳={p.Lift:0.000} 倒={p.KO} 回合时间={watchRoundT:0.0}");
                        handGap = Mathf.Min(handGap, gp);
                    }
                }
            }
#endif
        }

        void Strings(SBHands.Hand h, bool on)
        {
            if (watching) { WatchRig(); return; }
            bool mine = me && on;
            for (int i = 0; i < 5; i++) myLines[i].enabled = mine;
            if (mine)
            {
                Transform[] ends = { me.WireL ? me.WireL : me.FistL, me.WireR ? me.WireR : me.FistR, me.HeadTop, me.KneeL, me.KneeR };
                for (int i = 0; i < 5; i++) { myLines[i].SetPosition(0, h.tips[i]); myLines[i].SetPosition(1, ends[i].position); }
            }
            bool theirs = cpu && !cpu.KO;
            for (int i = 0; i < 5; i++) cpuLines[i].enabled = theirs;
            if (cpu)
            {
                var top = cpu.Root.position + Vector3.up * BarH;
                cpuBar.position = Vector3.Lerp(cpuBar.position, top, Time.deltaTime * 8f);
                float rt = ai && ai.self == cpu ? ai.Rattled : 0f;   // 操偶师慌了：木架抖得厉害
                cpuBar.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2f) * (6f + rt * 10f) + Mathf.Sin(Time.time * 27f) * rt * 6f, cpu.Root.eulerAngles.y, Mathf.Sin(Time.time * 1.6f) * (6f + rt * 10f) + Mathf.Sin(Time.time * 31f) * rt * 6f);
                if (theirs)
                {
                    Transform[] ends = { cpu.WireL ? cpu.WireL : cpu.FistL, cpu.WireR ? cpu.WireR : cpu.FistR, cpu.HeadTop, cpu.KneeL, cpu.KneeR };
                    Vector3[] starts = { new(-0.045f, 0, 0), new(0.045f, 0, 0), new(0, 0, 0.03f), new(-0.02f, 0, -0.03f), new(0.02f, 0, -0.03f) };
                    for (int i = 0; i < 5; i++) { cpuLines[i].SetPosition(0, cpuBar.TransformPoint(starts[i])); cpuLines[i].SetPosition(1, ends[i].position); }
                }
            }
        }
    }
}
