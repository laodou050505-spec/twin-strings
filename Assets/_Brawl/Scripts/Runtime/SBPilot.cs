using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>虚拟操偶师：对面那只木偶也是“有个人在提线”。每个人有水平（tier 1..5）、性子（猛攻 / 观望）、习惯（爱用的连招、会不会假动作、记不记仇）、
    /// 以及像真人一样的毛病（反应慢、会躲错、挨打会上头或者变怂）。数值只描述“这个人怎么打”，木偶本身的属性还是看 SBKit。
    /// 一共 16 位，All 里从最弱排到最强：前面几位很好赢，中间开始要动脑子，最后三位很难打。</summary>
    public class SBPilot
    {
        public string id, zh, en, habitZh, habitEn;
        public int tier = 1;                 // 水平 1..5（显示用）
        public string[] puppets;             // 常用的三只木偶：每场随机带一只
        public float windup = 0.7f;          // 起手预备（秒）：越短越难躲
        public float gapMin = 1.2f, gapMax = 2f;   // 两次进攻之间歇多久
        public float aggression = 0.5f;      // 性子：0 = 观望等你先动，1 = 一直压上来打
        public float guard = 0.3f;           // 进到出拳距离内举防守的倾向
        public float guardSense = 0.3f;      // 防守快被打穿时知不知道松手（0 = 一直举到被打穿）
        public float react = 0.6f;           // 看见你蓄力后多久反应得过来（秒）
        public float dodge = 0.2f;           // 反应过来后选择闪躲（蹲 / 跳）的概率
        public float mistake = 0.3f;         // 手滑：躲错方向、该防没防
        public float heavy = 0.15f, kick = 0.25f;   // 出重招 / 出腿的比例
        public float combo = 0.2f;           // 用连招的概率
        public float feint = 0f;             // 假动作：起手闪红却不出，换一招
        public float adapt = 0f;             // 记仇：你同一招用多了，专门防那一招
        public float punish = 0.2f;          // 抓你收招的空当反打；躲开后会不会反击
        public float showy = 0f;             // 占上风就挑衅（挑衅时全是空当）
        public float tilt = 0.3f;            // 挨打后的情绪：正 = 上头（更冒进、更多失误），负 = 变怂（缩着不敢出手）
        public float ult = 0.5f;             // 怒气满了多急着放必杀
        public bool dash;                    // 被逼到网边会不会弹网脱身
        public bool runaway;                 // 打一下就跑得远远的
        public float sharp = 1f;             // 顶尖高手的额外加成：进攻间隔、反应、失误再乘它（越小越强）
        public float might = 1f, tough = 1f; // 打出去的伤害倍率 / 挨打的伤害倍率（榜尾的人打得轻、不经打；榜首的人打得狠、很经打）
        public float read = 0f;              // 读招：看懂你在干什么并做出正确应对的概率（你蓄力他冲上来打断、你防守他用重招破防、你蹲他踢、你跳他等你落地……）。难度主要看它
        public float evade = 0f;             // 看见你出手就往后撤一步让你打空、再回来反打的概率（只有高手会）
        public SBGestures.Act[][] combos = new SBGestures.Act[0][];

        const SBGestures.Act JL = SBGestures.Act.JabL, JR = SBGestures.Act.JabR, HV = SBGestures.Act.Heavy, HL = SBGestures.Act.HeavyL, KK = SBGestures.Act.Kick, HK = SBGestures.Act.HeavyKick;

        /// <summary>从最弱到最强。</summary>
        public static readonly SBPilot[] All =
        {
            // —— 很好赢：刚学会提线 ——
            new SBPilot { id = "mash", might = 0.8f, tough = 1.2f, zh = "新手小满", en = "ROOKIE MAN", tier = 1, puppets = new[] { "scarecrow", "tumbler", "kite" },
                habitZh = "只会乱按：三下快拳，然后发愣", habitEn = "Mashes three jabs, then freezes",
                windup = 0.8f, gapMin = 1.5f, gapMax = 2.5f, aggression = 0.6f, guard = 0.03f, guardSense = 0f, react = 9f, dodge = 0f, mistake = 0.5f,
                heavy = 0.05f, kick = 0.15f, combo = 0.65f, punish = 0f, tilt = 0.6f, ult = 0.25f, combos = new[] { new[] { JL, JR, JL } } },
            new SBPilot { id = "slow", might = 0.8f, tough = 1.2f, zh = "慢半拍阿满", en = "SLOWPOKE", tier = 1, puppets = new[] { "tumbler", "iron", "weight" },
                habitZh = "缩着防守不敢出手：防守举到被打穿为止", habitEn = "Turtles up until the guard breaks",
                windup = 0.85f, gapMin = 2.2f, gapMax = 3.4f, aggression = 0.12f, guard = 0.6f, guardSense = 0f, react = 0.9f, dodge = 0.08f, mistake = 0.4f,
                heavy = 0.15f, kick = 0.15f, combo = 0f, punish = 0.3f, tilt = -0.7f, ult = 0.2f },
            new SBPilot { id = "big", might = 0.8f, tough = 1.2f, zh = "莽撞的大壮", en = "RECKLESS ZHUANG", tier = 1, puppets = new[] { "bamboo", "weight", "scarecrow" },
                habitZh = "只会冲上来出腿：跳起来就躲得开", habitEn = "Only kicks. Jump over them",
                windup = 0.75f, gapMin = 1.3f, gapMax = 2f, aggression = 0.85f, guard = 0f, guardSense = 0f, react = 9f, dodge = 0f, mistake = 0.45f,
                heavy = 0.05f, kick = 0.75f, combo = 0.3f, punish = 0f, tilt = 0.5f, ult = 0.4f, combos = new[] { new[] { KK, KK } } },
            // —— 有性子了 ——
            new SBPilot { id = "hot", might = 0.9f, tough = 1.1f, zh = "急性子阿烈", en = "HOTHEAD LIE", tier = 2, puppets = new[] { "weight", "scarecrow", "iron" },
                habitZh = "只攻不守，爱蓄重招；越挨打越上头", habitEn = "All attack, loves heavies, tilts when hit",
                windup = 0.62f, gapMin = 0.8f, gapMax = 1.4f, aggression = 0.95f, guard = 0f, guardSense = 0f, react = 9f, dodge = 0f, mistake = 0.3f,
                heavy = 0.4f, kick = 0.15f, combo = 0.35f, punish = 0.1f, tilt = 0.9f, ult = 0.9f, combos = new[] { new[] { JR, HV }, new[] { JL, JR, HL } } },
            new SBPilot { id = "show", might = 0.9f, tough = 1.1f, zh = "爱演的阿宝", en = "SHOWBOAT BAO", tier = 2, puppets = new[] { "kite", "bamboo", "scarecrow" },
                habitZh = "占了上风就招手挑衅，那时候全是空当", habitEn = "Taunts when ahead: wide open",
                windup = 0.65f, gapMin = 1.1f, gapMax = 1.9f, aggression = 0.55f, guard = 0.25f, guardSense = 0.3f, react = 0.7f, dodge = 0.2f, mistake = 0.3f,
                heavy = 0.15f, kick = 0.35f, combo = 0.3f, punish = 0.2f, showy = 1f, tilt = 0.3f, ult = 0.6f, combos = new[] { new[] { KK, JR } } },
            new SBPilot { id = "bean", might = 0.9f, tough = 1.1f, zh = "胆小的豆子", en = "TIMID BEAN", tier = 2, puppets = new[] { "kite", "tumbler", "bamboo" },
                habitZh = "打一下就跑；连着挨打会慌得缩成一团", habitEn = "Hits and runs. Panics under pressure",
                windup = 0.7f, gapMin = 1.4f, gapMax = 2.2f, aggression = 0.4f, guard = 0.35f, guardSense = 0.2f, react = 0.8f, dodge = 0.25f, mistake = 0.3f,
                heavy = 0.05f, kick = 0.2f, combo = 0.1f, punish = 0.3f, tilt = -0.9f, ult = 0.3f, runaway = true, combos = new[] { new[] { JL, JR } } },
            new SBPilot { id = "zhou", might = 0.9f, tough = 1.1f, zh = "一根筋老周", en = "ONE-TRACK ZHOU", tier = 2, puppets = new[] { "weight", "iron", "tumbler" },
                habitZh = "只练了一招重拳：闪红很久才出手，躲开就反击", habitEn = "One move: a slow heavy. Dodge, then counter",
                windup = 0.7f, gapMin = 1.6f, gapMax = 2.4f, aggression = 0.6f, guard = 0.15f, guardSense = 0.2f, react = 0.9f, dodge = 0.05f, mistake = 0.25f,
                heavy = 0.95f, kick = 0f, combo = 0f, punish = 0.2f, tilt = 0.2f, ult = 0.5f },
            // —— 会动脑子 ——
            new SBPilot { id = "fox", zh = "老油条", en = "OLD FOX", tier = 3, puppets = new[] { "bamboo", "kite", "tumbler" },
                habitZh = "假动作骗你出手，专抓你收招的空当", habitEn = "Feints, then punishes your recovery",
                windup = 0.52f, gapMin = 1.0f, gapMax = 1.8f, aggression = 0.42f, guard = 0.4f, guardSense = 0.7f, react = 0.45f, dodge = 0.45f, mistake = 0.15f,
                heavy = 0.2f, kick = 0.25f, combo = 0.3f, feint = 0.45f, adapt = 0.2f, punish = 0.75f, tilt = 0f, ult = 0.5f, combos = new[] { new[] { JL, KK }, new[] { JR, JL, HV } } },
            new SBPilot { id = "copy", zh = "记仇的复读机", en = "GRUDGE", tier = 3, puppets = new[] { "scarecrow", "bamboo", "iron" },
                habitZh = "同一招用多了，它就专门防那一招", habitEn = "Spam one move and it learns to stop it",
                windup = 0.55f, gapMin = 1.0f, gapMax = 1.7f, aggression = 0.55f, guard = 0.3f, guardSense = 0.5f, react = 0.5f, dodge = 0.3f, mistake = 0.2f,
                heavy = 0.2f, kick = 0.25f, combo = 0.3f, adapt = 1f, punish = 0.35f, tilt = 0.2f, ult = 0.5f, combos = new[] { new[] { JL, JR }, new[] { JR, KK } } },
            new SBPilot { id = "cheng", zh = "稳扎稳打的阿秤", en = "PATIENT CHENG", tier = 3, puppets = new[] { "iron", "weight", "tumbler" },
                habitZh = "先防后打：别对着防守猛打，用重招磨穿它", habitEn = "Blocks first, then hits. Break the guard with heavies",
                windup = 0.52f, gapMin = 1.2f, gapMax = 1.9f, aggression = 0.25f, guard = 0.7f, guardSense = 0.8f, react = 0.45f, dodge = 0.2f, mistake = 0.15f,
                heavy = 0.3f, kick = 0.15f, combo = 0.5f, adapt = 0.2f, punish = 0.8f, tilt = -0.2f, ult = 0.5f, combos = new[] { new[] { JL, JR, HV }, new[] { JR, HV } } },
            new SBPilot { id = "xiu", zh = "花拳阿绣", en = "FANCY XIU", tier = 3, puppets = new[] { "kite", "scarecrow", "bamboo" },
                habitZh = "一套四连打很凶，但打完要歇很久", habitEn = "Fierce four-hit strings, long rests after",
                windup = 0.46f, gapMin = 1.4f, gapMax = 2.1f, aggression = 0.75f, guard = 0.2f, guardSense = 0.5f, react = 0.45f, dodge = 0.3f, mistake = 0.15f,
                heavy = 0.15f, kick = 0.3f, combo = 0.95f, punish = 0.3f, showy = 0.4f, tilt = 0.3f, ult = 0.7f, combos = new[] { new[] { JL, JR, JL, KK }, new[] { JR, KK, JL, HK }, new[] { JL, JL, JR, HL } } },
            // —— 高手 ——
            new SBPilot { id = "sis", might = 1.2f, tough = 0.8f, evade = 0.15f, zh = "稳健的师姐", en = "STEADY SIS", tier = 4, puppets = new[] { "iron", "tumbler", "weight" },
                habitZh = "守得稳，躲开你的重招必定反击", habitEn = "Solid guard. Dodges, then counters",
                windup = 0.45f, gapMin = 1.0f, gapMax = 1.6f, aggression = 0.35f, guard = 0.65f, guardSense = 1f, react = 0.3f, dodge = 0.6f, mistake = 0.08f,
                heavy = 0.25f, kick = 0.2f, combo = 0.3f, adapt = 0.4f, punish = 0.9f, tilt = -0.1f, ult = 0.6f, dash = true, combos = new[] { new[] { JL, JR, HV } } },
            new SBPilot { id = "fast", might = 1.2f, tough = 0.8f, evade = 0.2f, zh = "快手小飞", en = "QUICK FEI", tier = 4, puppets = new[] { "kite", "scarecrow", "bamboo" },
                habitZh = "三连打不停手，最后一下总是腿", habitEn = "Triple combos that always end in a kick",
                windup = 0.4f, gapMin = 0.7f, gapMax = 1.2f, aggression = 0.85f, guard = 0.2f, guardSense = 0.6f, react = 0.35f, dodge = 0.4f, mistake = 0.12f,
                heavy = 0.1f, kick = 0.3f, combo = 0.85f, adapt = 0.2f, punish = 0.4f, tilt = 0.4f, ult = 0.7f, dash = true, combos = new[] { new[] { JL, JR, KK }, new[] { JR, JL, JR, HK } } },
            // —— 榜首三位：很难打 ——
            new SBPilot { id = "judge", might = 1.35f, tough = 0.58f, evade = 0.4f, zh = "铁面判官", en = "IRON JUDGE", tier = 5, puppets = new[] { "iron", "weight", "bamboo" },
                habitZh = "几乎不犯错，专等你先出错", habitEn = "Almost never errs. Waits for yours",
                windup = 0.34f, gapMin = 0.9f, gapMax = 1.5f, aggression = 0.3f, guard = 0.6f, guardSense = 1f, react = 0.18f, dodge = 0.8f, mistake = 0.02f,
                heavy = 0.3f, kick = 0.2f, combo = 0.5f, feint = 0.1f, adapt = 0.6f, punish = 1f, tilt = 0f, ult = 0.8f, dash = true, sharp = 0.7f, combos = new[] { new[] { JL, JR, HV }, new[] { JR, HK } } },
            new SBPilot { id = "ghost", might = 1.35f, tough = 0.6f, evade = 0.4f, zh = "鬼手七", en = "GHOST HAND", tier = 5, puppets = new[] { "kite", "bamboo", "scarecrow" },
                habitZh = "假动作一个接一个，闪红不一定真出手", habitEn = "Feint after feint. Red flash may be a lie",
                windup = 0.32f, gapMin = 0.6f, gapMax = 1.1f, aggression = 0.8f, guard = 0.35f, guardSense = 1f, react = 0.22f, dodge = 0.6f, mistake = 0.05f,
                heavy = 0.2f, kick = 0.3f, combo = 0.7f, feint = 0.6f, adapt = 0.5f, punish = 0.8f, tilt = 0.1f, ult = 0.9f, dash = true, sharp = 0.7f,
                combos = new[] { new[] { JL, KK, HL }, new[] { JR, JL, KK }, new[] { JL, JR, JR, HK } } },
            new SBPilot { id = "boss", might = 1.5f, tough = 0.5f, evade = 0.5f, zh = "班主", en = "THE MASTER", tier = 5, puppets = new[] { "weight", "iron", "kite" },
                habitZh = "什么都会，还会学你的打法", habitEn = "Does everything, and learns from you",
                windup = 0.32f, gapMin = 0.7f, gapMax = 1.2f, aggression = 0.6f, guard = 0.5f, guardSense = 1f, react = 0.2f, dodge = 0.75f, mistake = 0.03f,
                heavy = 0.25f, kick = 0.25f, combo = 0.65f, feint = 0.3f, adapt = 0.9f, punish = 0.95f, showy = 0.2f, tilt = 0f, ult = 0.9f, dash = true, sharp = 0.62f,
                combos = new[] { new[] { JL, JR, HV }, new[] { JR, KK, HL }, new[] { JL, JL, HK } } },
        };

        /// <summary>标题页表演赛用的普通对手。</summary>
        public static readonly SBPilot Demo = new SBPilot { id = "demo", zh = "", en = "", tier = 3, windup = 0.58f, gapMin = 0.95f, gapMax = 1.75f, aggression = 0.6f, guard = 0.35f, guardSense = 0.5f,
            react = 0.5f, dodge = 0.25f, mistake = 0.2f, heavy = 0.15f, kick = 0.25f, combo = 0.3f, punish = 0.3f, tilt = 0.2f, combos = new[] { new[] { JL, JR }, new[] { JR, KK } } };

        // 读招水平：榜尾的人基本不看你在干什么；越往上应对得越准；榜首三位几乎每次都应对得对
        static SBPilot()
        {
            var rd = new Dictionary<string, float> { ["mash"] = 0f, ["slow"] = 0.05f, ["big"] = 0f, ["hot"] = 0.1f, ["show"] = 0.2f, ["bean"] = 0.25f, ["zhou"] = 0.15f,
                ["fox"] = 0.5f, ["copy"] = 0.5f, ["cheng"] = 0.55f, ["xiu"] = 0.45f, ["sis"] = 0.78f, ["fast"] = 0.72f, ["judge"] = 0.96f, ["ghost"] = 0.92f, ["boss"] = 0.98f };
            foreach (var p in All) { if (rd.TryGetValue(p.id, out var v)) p.read = v; p.might = 1f; p.tough = 1f; }   // 伤害和耐打对谁都一样，不靠数值压人
            // 榜首两位（班主、鬼手七）：熟练度拉满——每一招都看得懂、应对不出错、出手快、间隔短、收招的空当必抓、防守快被打穿就撤。伤害和血量仍然和你一样
            foreach (var id in new[] { "boss", "ghost" })
            {
                var p = Get(id);
                p.read = 1f; p.mistake = 0f; p.punish = 1f; p.guardSense = 1f; p.adapt = 1f; p.react = 0.1f; p.dodge = 0.92f; p.evade = 0.75f;
                p.windup = 0.26f; p.gapMin = 0.45f; p.gapMax = 0.85f; p.sharp = 0.5f; p.ult = 1f;
            }
            Get("boss").guard = 0.65f; Get("ghost").guard = 0.5f;
            { var j = Get("judge"); j.read = 0.97f; j.sharp = 0.6f; j.react = 0.14f; }
        }

        public float slip = 0f;              // 闪身：看见你出快拳就蹲、出腿就跳，让你打空（躲开后他的下一击你防不住）。榜上前几名和闯关最后几战才有
        /// <summary>同一个人，更会躲：榜上前几名、闯关最后几战用。</summary>
        public SBPilot Dodgy(float v)
        { var p = (SBPilot)MemberwiseClone(); p.slip = Mathf.Max(slip, v); p.evade = Mathf.Max(evade, v * 0.9f); p.dodge = Mathf.Max(dodge, Mathf.Min(0.95f, v + 0.35f)); p.punish = Mathf.Max(punish, 0.6f); return p; }
        public bool champion;                // 擂台榜第 1 名专用的打法（见 Champion()）
        /// <summary>擂台榜第 1 名：不管现在坐在榜首的是谁，都换成“擂台之王”的打法——
        /// 每一招都看得懂、应对不出错、反应几乎是立刻的；防住就还手、打中就追着打、你举防守就用重招砸、你蓄力就上来打断、你的重招全躲开再反击；
        /// 不挑衅、不慌、不留空当。伤害和血量仍然和你一样，难只难在“会打”。</summary>
        public SBPilot Champion()
        {
            var p = (SBPilot)MemberwiseClone(); p.champion = true; p.tier = 5;
            p.read = 1f; p.mistake = 0f; p.react = 0.05f; p.punish = 1f; p.guardSense = 1f; p.adapt = 1f; p.dodge = 0.97f; p.evade = 0.85f;
            p.windup = 0.2f; p.gapMin = 0.3f; p.gapMax = 0.6f; p.sharp = 0.42f; p.ult = 1f; p.dash = true; p.runaway = false;
            p.guard = Mathf.Max(guard, 0.65f); p.combo = Mathf.Max(combo, 0.8f); p.feint = Mathf.Max(feint, 0.35f); p.aggression = Mathf.Max(aggression, 0.6f);
            p.tilt = 0f; p.showy = 0f; p.slip = 0.6f;
            // 这张纸条同时是给玩家的线索：他只有一个空当——你举着防守时他会蓄重招来砸，看见他闪红就蹲下躲开，躲开后的那一下反击他防不住
            p.habitZh = "每一招都接得住，躲开重招才有机会"; p.habitEn = "Blocks everything. Dodge his heavy, then counter";
            return p;
        }

        /// <summary>同样的打法、换一个身份（闯关最后一战的“另一个你”用）。</summary>
        public SBPilot As(string newId, string nzh, string nen, string hzh, string hen, int ntier, float nread)
        { var p = (SBPilot)MemberwiseClone(); p.id = newId; p.zh = nzh; p.en = nen; p.habitZh = hzh; p.habitEn = hen; p.tier = ntier; p.read = nread; return p; }

        public static SBPilot Get(string id) { foreach (var p in All) if (p.id == id) return p; return null; }

        /// <summary>这一场带哪只木偶：三只常用的里随机一只。</summary>
        public SBKit PickPuppet() => SBKit.Get(puppets[Random.Range(0, puppets.Length)]);

        /// <summary>界面要用到的所有字（给字库预热）。</summary>
        public static string AllText()
        {
            var sb = new System.Text.StringBuilder("擂台之王：每一招都接得住，躲开重招才有机会打中追着打防住还手你出手抢先一步操偶师水平习惯的对面擂台榜第名你匹配中到点名挑战上下一位返回常用木偶交手胜负还没过记着仇比次更强这是自己排升降不变守住卫冕成功榜首再战单戳翻或者找");
            foreach (var p in All) sb.Append(p.zh + p.habitZh);
            return sb.ToString();
        }
    }

    /// <summary>擂台榜：16 位操偶师 + 你，按名次排。存在本机，下次打开还在。
    /// 规则：打赢排在你前面的人 → 你排到他前面（中间的人各退一位）；输给排在你后面的人 → 他排到你前面；其余不变。
    /// 被你打败的人会“长记性”（每输一次水平涨一档，最多三档）。操偶师之间名次也会小幅变动：榜首几位一直在争第一。</summary>
    /// <summary>一场精彩对局（观战用）：谁对谁、各带哪只木偶、争的是什么。</summary>
    public class SBBout { public SBPilot a, b; public SBKit ka, kb; public string tagZh, tagEn, lineZh, lineEn; public bool title; }

    public static class SBLadder
    {
        public const string You = "you";
        static List<string> order;                                   // 第 1 名在最前
        static readonly Dictionary<string, int> lv = new(), win = new(), loss = new();   // 相对你：他长了几档记性、你赢过他几次、输给他几次
        public static readonly Dictionary<string, int> Moved = new();   // 最近一次变动：+1 升了 / -1 降了（榜上标一下）

        static void Ensure()
        {
            if (order != null) return;
            order = new List<string>();
            var saved = PlayerPrefs.GetString("sb_ladder", "");
            if (saved != "") foreach (var s in saved.Split(',')) if ((s == You || SBPilot.Get(s) != null) && !order.Contains(s)) order.Add(s);
            for (int i = SBPilot.All.Length - 1; i >= 0; i--) if (!order.Contains(SBPilot.All[i].id)) order.Add(SBPilot.All[i].id);   // 缺的人按实力补到后面
            if (!order.Contains(You)) order.Add(You);                                                                                 // 你从榜尾开始
            foreach (var p in SBPilot.All)
            {
                var r = PlayerPrefs.GetString("sb_rec_" + p.id, "0,0,0").Split(',');
                int a = 0, b = 0, c = 0; if (r.Length == 3) { int.TryParse(r[0], out a); int.TryParse(r[1], out b); int.TryParse(r[2], out c); }
                win[p.id] = a; loss[p.id] = b; lv[p.id] = Mathf.Clamp(c, 0, 3);
            }
        }
        static void Save()
        {
            PlayerPrefs.SetString("sb_ladder", string.Join(",", order));
            foreach (var p in SBPilot.All) PlayerPrefs.SetString("sb_rec_" + p.id, $"{win[p.id]},{loss[p.id]},{lv[p.id]}");
            PlayerPrefs.Save();
        }
        public static void Reset() { PlayerPrefs.DeleteKey("sb_ladder"); foreach (var p in SBPilot.All) PlayerPrefs.DeleteKey("sb_rec_" + p.id); order = null; Moved.Clear(); }

        public static int Count { get { Ensure(); return order.Count; } }
        /// <summary>名次（1 = 榜首）。</summary>
        public static int Rank(string id) { Ensure(); return order.IndexOf(id) + 1; }
        public static int MyRank => Rank(You);
        public static string IdAt(int rank) { Ensure(); return order[Mathf.Clamp(rank, 1, order.Count) - 1]; }
        public static SBPilot PilotAt(int rank) => SBPilot.Get(IdAt(rank));
        public static int Level(string id) { Ensure(); return lv.TryGetValue(id, out var v) ? v : 0; }
        public static (int win, int loss) Record(string id) { Ensure(); return (win.TryGetValue(id, out var a) ? a : 0, loss.TryGetValue(id, out var b) ? b : 0); }

        /// <summary>匹配：在你名次附近找一位，偏向排在你前面的（前一位最常见）；你是榜首就从后面几位里来人挑战。</summary>
        public static int Match()
        {
            Ensure(); int me = MyRank; var cand = new List<int>();
            void Add(int r, int w) { if (r >= 1 && r <= order.Count && r != me) for (int i = 0; i < w; i++) cand.Add(r); }
            Add(me - 1, 4); Add(me - 2, 3); Add(me - 3, 2); Add(me + 1, 1);
            if (me <= 2) { Add(me + 1, 3); Add(me + 2, 2); Add(me + 3, 1); }
            return cand.Count > 0 ? cand[Random.Range(0, cand.Count)] : Mathf.Clamp(me + 1, 1, order.Count);
        }

        /// <summary>打完一场，记账并调名次。返回你原来第几、现在第几。</summary>
        public static (int before, int after) Report(string pilotId, bool youWon)
        {
            Ensure(); Moved.Clear();
            int me = order.IndexOf(You), him = order.IndexOf(pilotId), before = me + 1;
            if (him < 0) return (before, before);
            if (youWon)
            {
                win[pilotId]++; lv[pilotId] = Mathf.Min(3, lv[pilotId] + 1);
                if (him < me) { order.RemoveAt(me); order.Insert(him, You); Moved[You] = 1; Moved[pilotId] = -1; }      // 赢了排在前面的：排到他前面
            }
            else
            {
                loss[pilotId]++;
                if (him > me) { order.RemoveAt(him); order.Insert(me, pilotId); Moved[You] = -1; Moved[pilotId] = 1; }   // 输给排在后面的：他排到你前面
            }
            Drift(); Save();
            return (before, order.IndexOf(You) + 1);
        }

        /// <summary>观战的那一场打完：赢的人如果排在后面，两人对换名次（只换这两个位置，不动你和其他人）。返回赢家原来第几、现在第几。</summary>
        public static (int before, int after) PilotBout(string winId, string loseId)
        {
            Ensure(); Moved.Clear();
            int w = order.IndexOf(winId), l = order.IndexOf(loseId), before = w + 1;
            if (w < 0 || l < 0) return (before, before);
            if (w > l) { (order[w], order[l]) = (order[l], order[w]); Moved[winId] = 1; Moved[loseId] = -1; }
            Save();
            return (before, order.IndexOf(winId) + 1);
        }

        /// <summary>操偶师之间自己也在打：榜首几位一直在争第一（常换位），其余偶尔有一对相邻的换个位置。不动你的名次。</summary>
        static void Drift()
        {
            void Swap(int i) { if (i < 0 || i + 1 >= order.Count || order[i] == You || order[i + 1] == You) return; (order[i], order[i + 1]) = (order[i + 1], order[i]); Moved[order[i]] = 1; Moved[order[i + 1]] = -1; }
            if (Random.value < 0.55f) Swap(Random.Range(0, 3));                         // 前四名里相邻的两位换位
            if (Random.value < 0.3f) Swap(Random.Range(3, order.Count - 1));           // 其余地方偶尔一对
        }
    }
}
