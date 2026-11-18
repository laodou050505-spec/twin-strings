using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>木偶角色：所有角色同一套骨架（胯 → 躯干 → 头；肩 → 上臂 → 前臂 → 拳套；髋 → 大腿 → 小腿 → 脚），
    /// 只换“部件”的形状、比例、颜色和挂件，再配一组能力数值。以后换成生成的模型时，按同样的挂点替换部件即可。</summary>
    [System.Serializable]
    public class SBKit
    {
        public enum Shape { Sphere, Box, Capsule, Cylinder }

        [System.Serializable]
        public struct Extra
        {
            public string at;          // 挂在哪：head / torso / hips
            public Shape shape;
            public Vector3 pos, scale, rot;   // 以身高为单位
            public Color color;
            public Extra(string at, Shape s, Vector3 p, Vector3 sc, Color c, Vector3 r = default) { this.at = at; shape = s; pos = p; scale = sc; color = c; rot = r; }
        }

        public string id, zh, en;
        public Color body, trim, glove, skin;
        public Shape headShape = Shape.Sphere, torsoShape = Shape.Capsule;
        public Vector3 headSize = new(0.28f, 0.28f, 0.28f), torsoSize = new(0.34f, 0.24f, 0.26f);
        public float limb = 0.075f, armLen = 1f, legLen = 1f, gloveSize = 1f;
        public float speed = 1f, power = 1f, weight = 1f;     // 走得快不快 / 打得重不重 / 被打飞得远不远（越大越稳）
        public float reach = 1f;                               // 臂展：出招够得多远（打中判定、出招冲步都按它）
        // 角色特性（每个角色只有一条，选人卡上写明）
        public float comboBonus = 0.1f;   // 连击每多一下加多少伤害
        public float rageGain = 1f;       // 挨打时怒气涨多快
        public float blockDmg = 0.15f;    // 防住时还掉多少血（比例）
        public float guardBreak = 0f;     // 重招被挡住时仍打进去多少（砸穿防守）
        public float revenge = 1f;        // 挨打后 2 秒内的下一招伤害倍数（反击）
        public float guard = 1f;          // 防守耐久倍数（越大越难被打穿）
        public float lowHpPower = 0f;     // 残血加伤：血越少打得越凶（稻草人）
        public bool armor;                // 重招蓄力 / 出手时不会被快拳打断（秤砣）
        public float jump = 1f;           // 跳得多高
        public float lunge = 1f;          // 出招时往前冲多远（手短的木偶靠身子撞上去补臂展）
        public bool steady;               // 不倒翁：出招的预备不会被打断（重到能打出硬直的招除外）
        public float recoverK = 1f;       // 收招时间倍数（越大收招越慢，打空了越容易挨打）
        public float closeWeak = 1f;      // 对手贴到身前（比臂展的一半还近）时的伤害倍率（竹竿侠手太长，贴身使不上劲）
        public string weakZh = "", weakEn = "";   // 短处（选人牌上和长处并排写出来）
        // 打法风格：每个角色站姿、节奏都不一样（只影响动作观感和出手快慢）
        public float bobAmp = 1f, bobRate = 7f;   // 架势上下颠的幅度 / 快慢
        public float lean0 = 8f;                  // 站着时身子往前探多少
        public float loose = 1f;                  // 关节松紧（稻草人浑身松垮）
        public float armP = -55f, armE = -105f;   // 架拳：上臂抬多高 / 肘弯多少
        public float sway = 0f;                   // 身子左右摇（不倒翁）
        public float windK = 1f;                  // 出招预备时间倍数
        public string styleZh = "", styleEn = "";   // 打法风格（选人卡上写）
        public Extra[] extras = new Extra[0];
        public string blurbZh, blurbEn;
        public string ultZh, ultEn;   // 必杀技名字（每个角色不一样，效果在 SBPuppet.UltUpdate 里）
        // 生成模型的拼装比例（以身高 H 为单位）：身子高、头高、肩膀在身子上的相对高度、身子往髋下面盖住多少（短裤盖住大腿根）
        public float torsoH = 0.36f, headH = 0.26f, shoulderY = 0.84f, pelvis = 0.14f;
        public float neckSink = 0.08f; // 脑袋往身子里压多少（头高的比例）：领口宽松的角色要压深一点，脖子处不留缝
        public bool noHead;           // 整体身子（秤砣、不倒翁）：没有单独的脑袋

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static readonly SBKit[] All =
        {
            new SBKit { id = "weight", noHead = true, torsoH = 0.52f, shoulderY = 0.7f, pelvis = 0.1f, ultZh = "千斤坠", ultEn = "TON DROP", zh = "秤砣", en = "WEIGHT", body = C("#8a5a34"), trim = C("#d9a441"), glove = C("#2f6b5c"), skin = C("#9c6a3e"),
                torsoShape = Shape.Sphere, torsoSize = new(0.4f, 0.36f, 0.34f), headSize = new(0.26f, 0.26f, 0.26f), limb = 0.075f, armLen = 0.78f, legLen = 0.62f,
                speed = 0.62f, power = 1.3f, weight = 1.2f, reach = 0.85f, guardBreak = 0.5f, guard = 1.1f, rageGain = 0.9f, armor = true, windK = 1.3f, bobAmp = 0.5f, bobRate = 3.5f, lean0 = 12f, armP = -48f, armE = -98f,
                blurbZh = "重炮：走得慢，重招能砸穿防守", blurbEn = "Slow. Heavies smash through guard.", styleZh = "步子沉、出手慢，重招蓄力时打不断", styleEn = "Heavy steps. Heavies can't be interrupted.",
                extras = new[] { new Extra("head", Shape.Cylinder, new(0, 0.15f, 0), new(0.13f, 0.035f, 0.13f), C("#d9a441")),
                                 new Extra("torso", Shape.Cylinder, new(0, 0.02f, 0), new(0.42f, 0.03f, 0.36f), C("#d9a441")) } },
            new SBKit { id = "scarecrow", neckSink = 0.26f, torsoH = 0.27f, headH = 0.34f, shoulderY = 0.84f, ultZh = "乌鸦群", ultEn = "CROW SWARM", zh = "稻草人", en = "SCARECROW", body = C("#d8bd78"), trim = C("#a8352a"), glove = C("#7a5232"), skin = C("#e2cc8e"),
                speed = 1f, power = 1f, weight = 1f, rageGain = 1.6f, guard = 0.9f, lowHpPower = 0.4f, loose = 2f, bobRate = 6f, armP = -42f, armE = -100f,
                blurbZh = "乱拳：挨打时怒气涨得快", blurbEn = "Wild. Rage builds fast.", styleZh = "浑身松垮看不出路数，血越少打得越凶", styleEn = "Floppy and unreadable. Hits harder when hurt.",
                extras = new[] { new Extra("head", Shape.Cylinder, new(0, 0.14f, 0), new(0.36f, 0.012f, 0.36f), C("#7a5a2e")),
                                 new Extra("head", Shape.Cylinder, new(0, 0.19f, 0), new(0.16f, 0.05f, 0.16f), C("#7a5a2e")),
                                 new Extra("torso", Shape.Box, new(0, 0.05f, 0.11f), new(0.2f, 0.16f, 0.02f), C("#a8352a")) } },
            new SBKit { id = "kite", torsoH = 0.36f, headH = 0.28f, shoulderY = 0.87f, ultZh = "纸鸢冲天", ultEn = "SKY DIVE", zh = "纸鸢", en = "KITE", body = C("#f1e9d6"), trim = C("#c63a32"), glove = C("#c63a32"), skin = C("#f7f1e3"),
                torsoShape = Shape.Box, torsoSize = new(0.26f, 0.3f, 0.16f), headSize = new(0.24f, 0.26f, 0.24f), limb = 0.06f, armLen = 1.1f, legLen = 1.1f, gloveSize = 0.85f,
                speed = 1.45f, power = 0.8f, weight = 0.75f, reach = 0.95f, comboBonus = 0.2f, guard = 0.75f, rageGain = 1.2f, jump = 1.6f, windK = 0.8f, bobAmp = 1.6f, bobRate = 10f, lean0 = 4f, armP = -58f, armE = -110f,
                blurbZh = "轻功：连击越打越疼，但不经打", blurbEn = "Fastest. Combos hurt more. Fragile.", styleZh = "脚下不停、跳得高、出手最快", styleEn = "Never still. Jumps high, strikes first.",
                extras = new[] { new Extra("torso", Shape.Box, new(-0.2f, 0.12f, -0.1f), new(0.32f, 0.36f, 0.01f), C("#f7f1e3"), new(0, 30, 20)),
                                 new Extra("torso", Shape.Box, new(0.2f, 0.12f, -0.1f), new(0.32f, 0.36f, 0.01f), C("#f7f1e3"), new(0, -30, -20)),
                                 new Extra("head", Shape.Sphere, new(0, 0.12f, -0.06f), new(0.13f, 0.13f, 0.13f), C("#1a1714")) } },
            new SBKit { id = "bamboo", torsoH = 0.3f, headH = 0.24f, shoulderY = 0.86f, ultZh = "节节高", ultEn = "BAMBOO SHOOT", zh = "竹竿侠", en = "BAMBOO", body = C("#7fa04e"), trim = C("#e2d48a"), glove = C("#3e6a2a"), skin = C("#a3bf6a"),
                torsoShape = Shape.Cylinder, torsoSize = new(0.2f, 0.18f, 0.2f), headSize = new(0.2f, 0.26f, 0.2f), limb = 0.05f, armLen = 1.3f, legLen = 1.25f,
                speed = 0.95f, power = 0.8f, weight = 0.9f, reach = 1.6f, guard = 0.85f, bobAmp = 0.6f, bobRate = 5f, lean0 = 2f, armP = -68f, armE = -88f,
                blurbZh = "长兵：隔老远就打得到，但打得轻", blurbEn = "Hits from afar, but hits light.", styleZh = "站得笔直，长手伸在前面把人挡在外头", styleEn = "Stands tall and keeps you at arm's length.",
                extras = new[] { new Extra("torso", Shape.Cylinder, new(0, 0.1f, 0), new(0.23f, 0.012f, 0.23f), C("#e2d48a")),
                                 new Extra("torso", Shape.Cylinder, new(0, 0.26f, 0), new(0.23f, 0.012f, 0.23f), C("#e2d48a")) } },
            new SBKit { id = "iron", torsoH = 0.29f, headH = 0.27f, shoulderY = 0.82f, ultZh = "铁壁冲锋", ultEn = "IRON CHARGE", zh = "铁甲将军", en = "IRON GENERAL", body = C("#4a4d55"), trim = C("#b8892f"), glove = C("#26272c"), skin = C("#6a6e78"),
                torsoShape = Shape.Box, torsoSize = new(0.42f, 0.32f, 0.3f), headSize = new(0.27f, 0.27f, 0.27f), limb = 0.09f, armLen = 0.95f, legLen = 0.9f, gloveSize = 1.2f,
                speed = 0.62f, power = 1.15f, weight = 1.6f, blockDmg = 0.05f, guard = 1.6f, rageGain = 0.8f, windK = 1.15f, bobAmp = 0.25f, bobRate = 4f, lean0 = 10f, armP = -60f, armE = -125f,
                blurbZh = "铁壁：最慢，防住几乎不掉血", blurbEn = "Slowest. Blocks shrug off damage.", styleZh = "稳如城墙，防守最难打穿", styleEn = "A walking wall. Hardest guard to break.",
                extras = new[] { new Extra("torso", Shape.Sphere, new(-0.24f, 0.3f, 0), new(0.18f, 0.12f, 0.18f), C("#b8892f")),
                                 new Extra("torso", Shape.Sphere, new(0.24f, 0.3f, 0), new(0.18f, 0.12f, 0.18f), C("#b8892f")),
                                 new Extra("head", Shape.Box, new(0, 0.2f, 0), new(0.03f, 0.12f, 0.14f), C("#b8352a")) } },
            new SBKit { id = "tumbler", noHead = true, torsoH = 0.6f, shoulderY = 0.58f, pelvis = 0.04f, ultZh = "不倒旋风", ultEn = "SPIN TOP", zh = "不倒翁", en = "TUMBLER", body = C("#c23b2f"), trim = C("#e8c15a"), glove = C("#d4a03a"), skin = C("#f4e2c8"),
                torsoShape = Shape.Sphere, torsoSize = new(0.46f, 0.44f, 0.42f), headSize = new(0.3f, 0.3f, 0.3f), limb = 0.06f, armLen = 0.62f, legLen = 0.34f,
                speed = 0.8f, power = 0.85f, weight = 1.7f, reach = 0.85f, revenge = 1.6f, guard = 1.2f, rageGain = 1.3f, sway = 9f, bobAmp = 0.4f, bobRate = 3f,
                blurbZh = "不倒：挨一下，下一招伤害翻倍", blurbEn = "Take a hit, next blow doubles.", styleZh = "摇摇晃晃打不飞，挨了打立刻还一下狠的", styleEn = "Wobbles but won't fall. Pays every hit back.",
                extras = new[] { new Extra("torso", Shape.Sphere, new(0, 0.05f, 0.17f), new(0.18f, 0.18f, 0.06f), C("#e8c15a")) } },
        };

        /// <summary>教学用的木人桩：不在选人列表里，木头色、粗圆柱身子、打不倒。</summary>
        public static readonly SBKit Dummy = new SBKit
        {
            id = "dummy", torsoH = 0.28f, headH = 0.24f, shoulderY = 0.84f, zh = "木人桩", en = "DUMMY", ultZh = "", ultEn = "", body = C("#9a6b3f"), trim = C("#5b3a22"), glove = C("#c9b48a"), skin = C("#b5834f"),
            torsoShape = Shape.Cylinder, torsoSize = new(0.3f, 0.3f, 0.3f), headSize = new(0.24f, 0.24f, 0.24f), limb = 0.08f, armLen = 1f, legLen = 0.8f,
            speed = 0.6f, power = 0.45f, weight = 3f, blurbZh = "", blurbEn = "",
            extras = new[] { new Extra("torso", Shape.Cylinder, new(0, 0.05f, 0), new(0.33f, 0.02f, 0.33f), C("#5b3a22")),
                             new Extra("torso", Shape.Cylinder, new(0, 0.2f, 0), new(0.33f, 0.02f, 0.33f), C("#5b3a22")),
                             new Extra("head", Shape.Box, new(0, 0, 0.12f), new(0.1f, 0.03f, 0.02f), C("#c63a32")) }
        };

        // ───────── 平衡表 ─────────
        // 公平 = 每只都有突出的长处，短处也同样明显；可以稍微强一点，但弱点必须摆在明面上。
        // 这里的数值会覆盖上面各角色定义里写的同名数值，调平衡只改这一处。
        static SBKit()
        {
            void T(string id, System.Action<SBKit> f) { foreach (var k in All) if (k.id == id) f(k); }
            // 规矩（用户定的）：
            //   · 六只木偶血量一样（挨同样的一下掉同样多的血）；
            //   · 每只只有一项长处（属性图上只有一个角是尖的），不许样样占便宜；有一项长处，就有明显的短处；
            //   · 其余属性一律是普通水平（1）。
            // 先全部归零成“普通木偶”，再各给一项长处和它的短处。数值用“同一位操偶师操纵、两两对打”的统计调过。
            foreach (var k in All)
            {
                k.speed = k.power = k.weight = k.reach = k.guard = k.rageGain = k.jump = k.revenge = k.windK = k.recoverK = k.lunge = k.closeWeak = 1f;
                k.comboBonus = 0.1f; k.blockDmg = 0.15f; k.guardBreak = 0f; k.lowHpPower = 0f; k.armor = false; k.steady = false;
            }
            // 秤砣｜长处：力量（一拳最重）。短处：慢——走得慢、出手收招都慢
            T("weight", k => { k.power = 1.35f; k.speed = 0.85f; k.windK = 1.2f; k.recoverK = 1.2f; k.lunge = 1.2f; k.reach = 1.15f;   // reach / lunge：它的胳膊天生短（只有普通木偶的八成），补到接近普通   // lunge：手短的木偶出拳时身子多送一点才够得着（补身材，不算长处）
                k.blurbZh = "力量：一拳最重"; k.blurbEn = "POWER: hardest hits";
                k.weakZh = "走得慢\n出手收招都慢"; k.weakEn = "Slow feet, slow wind-up and recovery"; });
            // 稻草人｜长处：怒气（涨得最快，必杀放得最多）。短处：防守不结实
            T("scarecrow", k => { k.rageGain = 1.6f; k.guard = 0.8f;
                k.blurbZh = "怒气：涨得最快\n必杀放得多"; k.blurbEn = "RAGE: fills fastest, more ults";
                k.weakZh = "防守不结实\n容易被打穿"; k.weakEn = "Flimsy guard, breaks easily"; });
            // 纸鸢｜长处：速度（走得最快、出手最快）。短处：单下伤害低、一推就飞
            T("kite", k => { k.speed = 1.25f; k.windK = 0.88f; k.power = 0.55f; k.weight = 0.7f; k.guard = 0.75f;
                k.blurbZh = "速度：走得最快\n出手也最快"; k.blurbEn = "SPEED: fastest feet and hands";
                k.weakZh = "单下伤害最低\n防守薄，一推就飞"; k.weakEn = "Weakest hits. Thin guard. Easily shoved"; });
            // 竹竿侠｜长处：臂展（手最长）。短处：被贴身就使不上劲
            T("bamboo", k => { k.reach = 1.6f; k.closeWeak = 0.6f; k.power = 0.85f;
                k.blurbZh = "臂展：手最长\n隔老远打得到"; k.blurbEn = "REACH: hits from far away";
                k.weakZh = "被贴身使不上劲\n单下伤害偏低"; k.weakEn = "Weak up close. Lighter hits"; });
            // 铁甲将军｜长处：防守（最结实，防住几乎不掉血）。短处：慢——走得慢、收招慢
            T("iron", k => { k.guard = 1.7f; k.blockDmg = 0.05f; k.speed = 0.74f; k.recoverK = 1.2f; k.windK = 1.15f;
                k.blurbZh = "防守：最结实\n防住几乎不掉血"; k.blurbEn = "GUARD: blocks cost almost nothing";
                k.weakZh = "走得慢\n出手收招慢"; k.weakEn = "Slow feet, slow wind-up and recovery"; });
            // 不倒翁｜长处：下盘（推不动，出招不会被打断）。短处：手短、跳不高、走得慢一点
            T("tumbler", k => { k.weight = 1.7f; k.steady = true; k.speed = 1f; k.jump = 0.7f; k.lunge = 1.6f; k.reach = 1.5f;   // reach / lunge：它的胳膊只有普通木偶的六成长，补完仍然比普通木偶短
                k.blurbZh = "下盘：推不动\n挨快拳不晃"; k.blurbEn = "STANCE: can't be shoved, shrugs off jabs";
                k.weakZh = "手短\n跳不高"; k.weakEn = "Short reach. Low jump"; });
            // BALANCE（对打统计调出来的修正，乘在 power 上；只许往下调——往上调就等于又多了一项长处）
            var bal = new Dictionary<string, float> { ["weight"] = 1f, ["scarecrow"] = 1f, ["kite"] = 1f, ["bamboo"] = 1f, ["iron"] = 1f, ["tumbler"] = 1f };
            foreach (var k in All) if (bal.TryGetValue(k.id, out var m)) k.power *= m;
        }

        /// <summary>同一只木偶的另一副打扮（同款对打时给对手用）：只换拳套颜色，其余一样。</summary>
        public SBKit WithGlove(Color c) { var k = (SBKit)MemberwiseClone(); k.glove = c; return k; }

        public static SBKit Get(string id) { foreach (var k in All) if (k.id == id) return k; return All[0]; }
    }
}
