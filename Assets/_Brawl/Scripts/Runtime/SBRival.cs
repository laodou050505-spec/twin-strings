using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>电脑对手 = 一位“虚拟操偶师”在提线（SBPilot）。三层叠在一起：
    /// ① 木偶的底色（SBKit）：秤砣步步紧逼不后退、纸鸢打完就跳开、竹竿侠把人挡在外头、铁甲守着等反击、不倒翁挨一下立刻还手、稻草人残血发狂；
    /// ② 操偶师这个人（SBPilot）：水平高低、猛攻还是观望、爱用的连招、会不会假动作、记不记仇；
    /// ③ 这个人当下的情绪：挨打多了会上头或者变怂，占了上风会嚣张（爱演的还会挑衅）。
    /// 像真人一样不完美：看见你蓄力要过一会儿才反应得过来，会躲错方向，会忘了松开防守。
    /// 出招前一定有预备动作（往后拉、身上闪红），给玩家时间躲。</summary>
    public class SBRival : MonoBehaviour
    {
        public SBPuppet self, foe;
        [System.NonSerialized] public System.Func<Vector3, Vector3> clamp;
        [System.NonSerialized] public bool active;
        public SBPilot Pilot { get; private set; } = SBPilot.Demo;
        /// <summary>这个人现在有多慌（0..1）：对面提线木架会跟着抖。</summary>
        public float Rattled => Mathf.Clamp01(-mood);
        /// <summary>怒气满了每秒有多大概率放必杀。</summary>
        const float Hard = 0.8f;   // 整体难度：进攻间隔、反应时间、失误率都乘它（越小越难）
        int level;                  // 这个人被你打败过几次（长了几档记性，最多 3）
        float K => Hard * Pilot.sharp * Mathf.Pow(0.93f, level);   // 顶尖高手自带加成；每长一档记性再强 7%
        public float UltRate => 0.15f + Pilot.ult * 1.2f;

        float cool = 1.4f, strafePh, guardT, guardLock, backT, tauntT, dashCool, hopCool, guardTick, lastGuardHP = 100f, riposteAt = -1f, evadeCool;
        // 读招：看见你在干什么 → 过一个反应时间 → 做出对应的动作
        enum Read { None, Rush, Break, Kick, AntiAir, AllIn, Retreat, Brace }
        Read intent; float intentAt, lastBlockSay; bool lAtk, lGuard, lCrouch, lAir, lBroken, lCounter, lUlt, lDash, lCharge;
        float mood;                       // 情绪：负 = 被打懵了，正 = 占上风
        float lastHP = 100f, lastFoeHP = 100f, lastHitT = -9f;
        SBGestures.Act lastFoeAct; bool lastFoeRec; float chargeSeenT = -1f, punishUntil = -1f; bool punishRolled;
        readonly Queue<int> seen = new();   // 你最近十招：0 快拳 1 重招 2 腿
        readonly Queue<SBGestures.Act> plan = new(); bool planOn; float planWind, feintAt = -1f; SBGestures.Act feintNext;
        string kid = "";

        public void SetPilot(SBPilot p, int grudge = 0)
        {
            Pilot = p ?? SBPilot.Demo; level = Mathf.Clamp(grudge, 0, 3); champ = Pilot.champion;
            cool = 1.4f; mood = 0; guardT = guardLock = backT = tauntT = 0; seen.Clear(); plan.Clear(); planOn = false; feintAt = -1; chargeSeenT = -1; punishUntil = -1;
            lastHP = lastFoeHP = 100f; lastFoeAct = SBGestures.Act.None; lastFoeRec = false; lastGuardHP = 100f; riposteAt = -1f; guardTick = 0;
        }
        /// <summary>旧接口（标题页表演赛）：普通水平。</summary>
        public void SetLevel(int lv) => SetPilot(SBPilot.Demo);

        float Share(int type) { if (seen.Count < 4) return 0f; int n = 0; foreach (var t in seen) if (t == type) n++; return n / (float)seen.Count; }
        float slipCool;
        bool champ;   // 擂台榜第 1 名的打法：下面几处多出来的“高手才会的事”都看它
        void Say(string what) { LastSay = what; LastSayT = Time.time; if (Pilot.zh != "") Debug.Log($"[SB] 操偶师 {Pilot.zh}：{what}"); }
        /// <summary>最近一次的想法（观战时挂在这位操偶师的提线木架上，让你看到他在想什么）。</summary>
        [System.NonSerialized] public string LastSay; [System.NonSerialized] public float LastSayT = -9f;
        static readonly Dictionary<string, string> sayEn = new()
        {
            ["你放大招，防住"] = "Ult incoming: guard", ["你出手，防住"] = "Foe attacks: guard", ["你落地，出拳"] = "Foe lands: punch", ["你蓄力，冲上去打断"] = "Foe charging: rush in", ["你被破防，全力追打"] = "Guard broken: all in",
            ["你要反击，退开"] = "Counter ready: back off", ["你蹲下，出腿"] = "Foe ducks: kick", ["你防守，重招破防"] = "Foe guards: break it", ["假动作"] = "Feint", ["反击"] = "Counter",
            ["后撤闪开"] = "Step back", ["弹网脱身"] = "Net escape", ["挑衅"] = "Taunt", ["格挡反击"] = "Block and hit back", ["看穿了，闪开"] = "Read it: dodge",
        };
        public string SayText() => LastSay == null ? "" : SBUI.En ? (sayEn.TryGetValue(LastSay, out var e) ? e : "") : LastSay.Replace("你", "对手");

        void Update()
        {
            if (!self || !foe || !self.Ready) return;
            float dt = Time.deltaTime, now = Time.time;
            guardT -= dt; backT -= dt; tauntT -= dt; dashCool -= dt; hopCool -= dt;
            if (!active || self.KO || foe.KO) { self.guard = guardT > 0 && active; plan.Clear(); planOn = false; feintAt = -1; lastHP = self.Health; lastFoeHP = foe.Health; return; }
            kid = self.Kit.id; var P = Pilot;

            var me = self.Root.position; var him = foe.Root.position;
            var away = me - him; away.y = 0; float dist = away.magnitude;
            away = dist < 1e-3f ? Vector3.forward : away / dist;
            float reach = self.Reach;
            bool canHit = dist < reach + self.H * 0.35f * self.Kit.lunge;             // 我够得着他
            bool inDanger = dist < foe.Reach + self.H * 0.4f;        // 他够得着我

            // ───── 看：记下你出了什么招、有没有在蓄力、是不是刚收招 ─────
            var fa = foe.ActiveAttack; bool foeStart = fa != SBGestures.Act.None && lastFoeAct == SBGestures.Act.None;
            if (foeStart) { seen.Enqueue(SBGestures.IsKick(fa) ? 2 : SBGestures.IsHeavy(fa) ? 1 : 0); while (seen.Count > 10) seen.Dequeue(); }
            lastFoeAct = fa;
            if (foe.charge > 0.2f) { if (chargeSeenT < 0) chargeSeenT = now; } else if (fa == SBGestures.Act.None) chargeSeenT = -1f;
            bool foeRec = foe.Recovering; if (foeRec && !lastFoeRec) { punishUntil = now + 0.4f; punishRolled = false; } lastFoeRec = foeRec;
            bool gotHit = self.Health < lastHP - 0.5f, landed = foe.Health < lastFoeHP - 0.5f;
            if (gotHit) { mood -= 0.22f; lastHitT = now; plan.Clear(); planOn = false; feintAt = -1; }
            if (landed) { mood += 0.2f; if (champ) cool = Mathf.Min(cool, 0.04f); }   // 榜首：打中了不歇手，接着打，直到你被弹开
            lastHP = self.Health; lastFoeHP = foe.Health;

            // ───── 情绪 ─────
            mood = Mathf.Clamp(Mathf.MoveTowards(mood, 0f, dt * 0.07f), -1f, 1f);
            float upset = Mathf.Clamp01(-mood), cocky = Mathf.Clamp01(mood);
            float aggr = Mathf.Clamp01(P.aggression + upset * P.tilt * 0.5f + (kid == "scarecrow" && self.Health < 40f ? 0.35f : 0f) + (kid == "weight" ? 0.1f : 0f));
            float err = Mathf.Clamp01(P.mistake * K + upset * Mathf.Abs(P.tilt) * 0.25f);
            float jabS = Share(0), hvS = Share(1), kkS = Share(2);

            // ───── 走位 ─────
            strafePh += dt * (0.7f + self.Kit.speed * 0.4f) * (kid == "kite" ? 1.5f : 1f);
            var sideStep = Vector3.Cross(Vector3.up, away) * Mathf.Sin(strafePh) * self.H * 0.3f;
            float keep = Mathf.Max(reach * 0.95f, self.BodyR + foe.BodyR + self.H * 0.2f);   // 再短的手也不贴在人身上
            if (backT > 0 && kid != "weight") keep = reach * (kid == "kite" ? 2.6f : 2.2f);   // 打完退半步；秤砣不退
            else if (tauntT > 0) keep = reach * 2f;
            else if (aggr < 0.4f && cool > 0.35f && plan.Count == 0) keep = reach * 1.45f;     // 观望的人：站在够不着的地方等你先动
            if (kid == "bamboo" && dist < reach * 0.55f) backT = Mathf.Max(backT, 0.45f);      // 竹竿侠：被贴身就拉开
            var want = him + away * keep + sideStep;
            self.target = clamp != null ? clamp(want) : want;
            // 弹网脱身（高手）：被逼到网边、你又贴着，借网弹到你的侧面
            if (P.dash && dashCool <= 0 && clamp != null && dist < reach * 1.1f && now - lastHitT < 2f && !self.Dashing && self.ActiveAttack == SBGestures.Act.None)
            {
                var outp = me + away * 0.035f;
                if ((clamp(outp) - outp).sqrMagnitude > 1e-6f)
                {
                    var side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? 1f : -1f);
                    self.Dash(me + Vector3.ClampMagnitude(clamp(him + side * reach * 1.3f - away * reach * 0.3f) - me, 0.05f), 0.15f, 0.3f); dashCool = 9f; Say("弹网脱身");
                }
            }

            // ───── 守 ─────
            // 防守快被打穿：懂的人会松手歇一下，不懂的一直举到被打穿
            if (guardT > 0 && self.GuardHP < 30f && Random.value < P.guardSense * dt * 6f) { guardT = 0; guardLock = now + 1.3f; }
            bool idle = self.ActiveAttack == SBGestures.Act.None && plan.Count == 0 && tauntT <= 0;
            float gWant = P.guard + P.adapt * jabS * 0.8f + (kid == "iron" ? 0.25f : 0f) + (kid == "tumbler" ? 0.1f : 0f) + (P.tilt < 0 ? upset * -P.tilt * 0.5f : 0f);
            guardTick -= dt;
            if (guardTick <= 0)
            {
                guardTick = 0.2f;
                if (inDanger && idle && now > guardLock && Random.value < Mathf.Clamp01(gWant * 1.4f)) guardT = Mathf.Max(guardT, 0.3f);
            }
            // 挡住了一下：会打的人立刻还手（格挡反击），不给你接着打的机会
            bool blockedNow = self.GuardHP < lastGuardHP - 0.5f; lastGuardHP = self.GuardHP;
            if (blockedNow && riposteAt < 0 && canHit && (champ || Random.value < Mathf.Max(P.punish * 0.75f, P.read * 0.9f))) riposteAt = now + Mathf.Lerp(0.34f, 0.1f, P.punish) * K;
            // 防守快被打穿：懂的人松手的同时往后撤一步，不站着挨打
            if (guardT > 0 && self.GuardHP < 35f && P.guardSense > 0.6f && backT <= 0) backT = 0.9f;
            if (gotHit && now > guardLock)
            {
                if (kid == "tumbler") cool = Mathf.Min(cool, 0.05f);                                  // 不倒翁：挨一下立刻还手（翻倍）
                else if (champ || Random.value < gWant * 0.9f + 0.05f) guardT = Mathf.Max(guardT, 0.6f);   // 榜首：挨了一下马上抱头，不让你连上第二下
            }
            // 会躲的人（榜上前几名、闯关最后几战）：看见你出快拳就蹲、出腿就跳，让你打空；躲开后他的下一击你防不住。
            // 防守不放下：万一蹲 / 跳慢了半拍，这一下也是打在拳套上
            slipCool -= dt;
            if (foeStart && !SBGestures.IsHeavy(fa) && inDanger && idle && !self.Stunned && slipCool <= 0 && Random.value < P.slip)
            {
                self.Do(SBGestures.IsKick(fa) ? SBGestures.Act.Jump : SBGestures.Act.Duck); slipCool = 0.8f; Say("看穿了，闪开");
            }
            // 高手：看见你出手（没在防守、自己也没在出招），往后撤一步让你打空，紧接着回来反打
            evadeCool -= dt;
            if (foeStart && !SBGestures.IsHeavy(fa) && inDanger && idle && guardT <= 0 && evadeCool <= 0 && !self.Dashing && Random.value < P.evade)
            {
                var back = me + away * 0.045f; self.Dash(clamp != null ? clamp(back) : back, 0.11f, 0f);
                evadeCool = champ ? 0.6f : 1.1f; punishUntil = now + 0.55f; punishRolled = false; Say("后撤闪开");
            }
            // 看见你蓄力：过了反应时间才算“看懂了”，等你松手那一下去躲（蹲躲拳、跳躲腿）；手滑会躲反
            if (foeStart && SBGestures.IsHeavy(fa) && chargeSeenT > 0 && now - chargeSeenT >= P.react * K && inDanger && self.ActiveAttack == SBGestures.Act.None)
            {
                bool isKick = SBGestures.IsKick(fa); float r = Random.value, pd = P.dodge + P.adapt * (isKick ? kkS : hvS) * 0.6f;
                if (r < pd) { bool right = Random.value > err; self.Do(isKick == right ? SBGestures.Act.Jump : SBGestures.Act.Duck); guardT = 0; Say(right ? "看穿了，闪开" : "躲反了"); }
                else if (r < pd + gWant + 0.15f && now > guardLock) guardT = Mathf.Max(guardT, 0.5f);
            }
            // 记仇的人：你老用腿，他就时不时小跳一下
            if (P.adapt > 0.3f && kkS > 0.45f && inDanger && idle && hopCool <= 0 && Random.value < P.adapt * kkS * dt * 1.5f) { self.Do(SBGestures.Act.Jump); hopCool = 1.2f; }
            // 榜首：只要你的快拳 / 腿还在半路上、他腾得出手，就一定防——
            //   自己正在预备（还要 0.08 秒以上才打得出去）：把这一招收住先防，不跟你对拼；
            //   自己那一拳刚打完正在收手：收手的同时把防守举起来（你想抓他收招的空当，多半打在拳套上）。
            if (champ && fa != SBGestures.Act.None && !SBGestures.IsHeavy(fa) && inDanger && now > guardLock && self.GuardHP > 20f && tauntT <= 0)
            {
                bool pull = self.WindingUp && self.WindLeft > 0.08f && !(self.Kit.armor && SBGestures.IsHeavy(self.ActiveAttack));
                if (pull) { self.CancelWind(); plan.Clear(); planOn = false; feintAt = -1; cool = Mathf.Min(cool, 0.12f); }
                if (pull || self.Recovering || !self.Busy)
                {
                    guardT = Mathf.Max(guardT, 0.5f);
                    if (now - lastBlockSay > 2f) { lastBlockSay = now; Say("你出手，防住"); }
                }
            }
            self.guard = guardT > 0 && tauntT <= 0;
            self.taunt = tauntT > 0;

            // ───── 攻 ─────
            if (self.Stunned) return;
            // 假动作：起手闪红，到一半收住，换一招
            if (feintAt > 0 && now >= feintAt) { feintAt = -1; self.CancelWind(); plan.Clear(); plan.Enqueue(feintNext); planOn = true; planWind = (P.windup * 0.92f) * 0.5f; Say("假动作"); }
            // 连招里的下一下
            if (riposteAt > 0 && now >= riposteAt)
            {
                riposteAt = -1f;
                if (idle && !self.Stunned && canHit)
                {
                    guardT = 0; self.guard = false;
                    plan.Clear(); if (P.tier >= 4) plan.Enqueue(Random.value < 0.5f ? SBGestures.Act.JabL : SBGestures.Act.Kick);   // 高手还手是两下
                    if (champ) plan.Enqueue(SBGestures.Act.JabR);                                                                     // 榜首还手是三下，而且出得快：抢在你下一拳之前
                    planWind = champ ? 0.08f : 0.16f; self.Do(Random.value < 0.5f ? SBGestures.Act.JabR : SBGestures.Act.JabL, champ ? 0.07f : 0.15f, 0f); planOn = true;
                    cool = (Random.Range(P.gapMin, P.gapMax) * K) * 0.7f; Say("格挡反击"); return;
                }
            }
            if (plan.Count > 0 && feintAt < 0 && !self.Busy)
            {
                if (dist < reach + self.H * 0.6f) { var a = plan.Dequeue(); bool hv = SBGestures.IsHeavy(a); self.Do(a, hv ? planWind * 1.4f : planWind, hv ? Random.Range(0.2f, 0.7f) : 0f); }
                else plan.Clear();
            }
            if (planOn && plan.Count == 0 && !self.Busy && feintAt < 0) { planOn = false; backT = (kid == "kite" ? 1.3f : 0.9f * (1f - aggr * 0.45f)) * (Pilot.runaway ? 1.9f : 1f); }
            if (plan.Count > 0 || self.Busy || feintAt > 0) return;

            // ───── 读招：你做了什么，我就做克它的那一手（会不会、多快，看这位操偶师的水平）─────
            {
                bool g = foe.GuardUp, cr = foe.Crouching, air = foe.Lift > 0.012f, bk = foe.GuardBroken, ct = foe.CounterReady, ul = foe.Ulting, ds = foe.Dashing, ch = foe.charge > 0.2f;
                float rt = P.react * K * 0.6f;
                void See(Read what, float delay) { if (Random.value < P.read) { intent = what; intentAt = now + delay; } }
                if (ch && !lCharge && dist < reach + self.H * 0.9f) See(Read.Rush, rt);           // 你在蓄力 → 冲上去一拳打断
                else if (g && !lGuard && canHit) See(Read.Break, rt + 0.25f);                     // 你举起防守 → 用重招磨穿它
                else if (cr && !lCrouch && canHit) See(Read.Kick, rt * 0.6f);                     // 你蹲下 → 出腿（蹲躲不开腿）
                else if (air && !lAir && canHit) See(Read.AntiAir, 0.3f);                         // 你跳起来 → 等你落地出拳
                else if (bk && !lBroken) See(Read.AllIn, rt * 0.5f);                              // 你被破防了 → 全力追打
                else if (ct && !lCounter) See(Read.Retreat, rt * 0.5f);                           // 你拳套发金光（要反击）→ 退开，不给你打
                else if ((ul && !lUlt) || (ds && !lDash)) See(Read.Brace, rt * 0.4f);             // 你放必杀 / 弹网冲过来 → 举防守
                // 你出手（快拳 / 腿）→ 我举防守：这是最基本的一条。会打的人几乎每一下都防得住；防守有耐久，防多了要松手后撤，或者被你的重招打穿
                bool atk = foe.ActiveAttack != SBGestures.Act.None && !SBGestures.IsHeavy(foe.ActiveAttack);
                if (atk && !lAtk && inDanger && !self.Busy && now > guardLock && self.GuardHP > 20f && Random.value < P.read)
                {
                    guardT = Mathf.Max(guardT, 0.5f);
                    if (now - lastBlockSay > 2f) { lastBlockSay = now; Say("你出手，防住"); }
                }
                lAtk = atk;
                lGuard = g; lCrouch = cr; lAir = air; lBroken = bk; lCounter = ct; lUlt = ul; lDash = ds; lCharge = ch;
                if (intent != Read.None && now >= intentAt)
                {
                    var it = intent; intent = Read.None; bool free = !self.Busy && plan.Count == 0 && feintAt < 0;
                    switch (it)
                    {
                        case Read.Rush: if (free && foe.charge > 0f && dist < reach + self.H * 0.9f) { guardT = 0; self.guard = false; Go(Random.value < 0.5f ? SBGestures.Act.JabR : SBGestures.Act.JabL, 0.1f, 0f); Say("你蓄力，冲上去打断"); return; } break;
                        case Read.Break: if (free && foe.GuardUp && canHit) { guardT = 0; self.guard = false; Go(Random.value < 0.5f ? SBGestures.Act.Heavy : SBGestures.Act.HeavyL, 0.34f, 0.8f); Say("你防守，重招破防"); return; } break;
                        case Read.Kick: if (free && foe.Crouching && canHit) { guardT = 0; self.guard = false; Go(SBGestures.Act.Kick, 0.14f, 0f); Say("你蹲下，出腿"); return; } break;
                        case Read.AntiAir: if (free && canHit) { guardT = 0; self.guard = false; Go(SBGestures.Act.JabR, 0.12f, 0f); Say("你落地，出拳"); return; } break;
                        case Read.AllIn: if (free && canHit) { guardT = 0; self.guard = false; plan.Clear(); plan.Enqueue(SBGestures.Act.JabL); plan.Enqueue(SBGestures.Act.Kick); planWind = 0.14f; self.Do(SBGestures.Act.Heavy, 0.2f, 1f); planOn = true; Say("你被破防，全力追打"); return; } break;
                        case Read.Retreat: backT = 1.35f; guardT = 0; Say("你要反击，退开"); break;
                        case Read.Brace: guardT = Mathf.Max(guardT, 1.2f); Say("你放大招，防住"); break;
                    }
                }
            }
            cool -= dt;
            // 躲开了你的招：会打的人马上反击（挡不住）
            if (self.CounterReady && canHit && P.punish > 0.25f) { Go(P.tier >= 3 ? SBGestures.Act.Heavy : SBGestures.Act.JabR, 0.22f, 0.3f); cool = (Random.Range(P.gapMin, P.gapMax) * K); Say("反击"); return; }
            // 抓你收招的空当
            if (now < punishUntil && !punishRolled && canHit)
            {
                punishRolled = true;
                if (Random.value < P.punish) { guardT = 0; self.guard = false; StartPlan(aggr, dist, reach, (P.windup * 0.92f) * 0.6f, false); cool = (Random.Range(P.gapMin, P.gapMax) * K) * Mathf.Lerp(1.15f, 0.7f, aggr); return; }
            }
            // 占了上风：爱演的人会招手挑衅（这时候全是空当）
            if (P.showy > 0 && cocky > 0.45f && tauntT <= 0 && dist > reach * 1.1f && Random.value < P.showy * dt * 0.9f) { tauntT = 1.4f; mood -= 0.35f; guardT = 0; Say("挑衅"); return; }
            if (cool <= 0 && canHit && tauntT <= 0)
            {
                // 你正在出手：会打的人不跟你对拼（自己预备到一半会被你打断），先把这一下防住，等你收招的空当再打
                if (foe.ActiveAttack != SBGestures.Act.None && !SBGestures.IsHeavy(foe.ActiveAttack) && inDanger && self.GuardHP > 25f && now > guardLock && Random.value < P.read) { guardT = Mathf.Max(guardT, 0.4f); cool = 0.1f; return; }
                // 榜首：你一直举着防守，就不拿快拳去敲，直接蓄一记重招砸穿（你看见他闪红时蹲下躲开，就能反击——这是打他的办法）
                if (champ && foe.GuardUp) { guardT = 0; self.guard = false; Go(Random.value < 0.5f ? SBGestures.Act.Heavy : SBGestures.Act.HeavyL, 0.3f, 0.9f); cool = Random.Range(P.gapMin, P.gapMax) * K; Say("你防守，重招破防"); return; }
                guardT = 0; self.guard = false;   // 想出手了：放下防守
                // 观望的人多半不先动手，等你露空当；偶尔才试探一下
                if (aggr < 0.35f && Random.value > 0.3f + aggr) { cool = 0.35f; return; }
                StartPlan(aggr, dist, reach, (P.windup * 0.92f), Random.value < P.feint);
                cool = (Random.Range(P.gapMin, P.gapMax) * K) * Mathf.Lerp(1.15f, 0.7f, aggr);
            }
        }

        void Go(SBGestures.Act a, float wind, float charge) { plan.Clear(); self.Do(a, wind, charge); planOn = true; }

        SBGestures.Act Single(float dist, float reach)
        {
            float r = Random.value, r2 = Random.value, hv = Pilot.heavy + (kid == "weight" ? 0.25f : 0f), kk = Pilot.kick + (kid == "bamboo" && dist < reach * 0.7f ? 0.4f : 0f);
            return r < hv ? (r2 < 0.4f ? SBGestures.Act.Heavy : r2 < 0.75f ? SBGestures.Act.HeavyL : SBGestures.Act.HeavyKick)
                 : r < hv + kk ? SBGestures.Act.Kick : (r2 < 0.5f ? SBGestures.Act.JabL : SBGestures.Act.JabR);
        }

        void StartPlan(float aggr, float dist, float reach, float wind, bool feint)
        {
            plan.Clear(); var P = Pilot; planWind = wind * 0.55f;   // 连招后面几下出得快
            SBGestures.Act first;
            if (P.combos.Length > 0 && Random.value < P.combo) { var c = P.combos[Random.Range(0, P.combos.Length)]; first = c[0]; for (int i = 1; i < c.Length; i++) plan.Enqueue(c[i]); }
            else first = Single(dist, reach);
            bool hv = SBGestures.IsHeavy(first);
            self.Do(first, hv ? wind * 1.5f : wind, hv ? Random.Range(0.2f, 0.7f) : 0f); planOn = true;
            if (feint && self.WindingUp)
            {
                feintAt = Time.time + (hv ? wind * 1.5f : wind) * 0.55f;
                feintNext = SBGestures.IsKick(first) ? SBGestures.Act.JabR : SBGestures.Act.Kick;   // 换成另一类招：你要是按第一下去躲，正好躲错
            }
        }
    }
}
