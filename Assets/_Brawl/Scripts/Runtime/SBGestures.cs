using UnityEngine;

namespace StringBrawl
{
    /// <summary>把一只手的手指 / 手掌数据翻译成木偶动作。按真机测试结论只用三路手指：拇指、食指、后三指（中指无名指小指一起）。
    /// 手势和动作对得上（拉线的动作就是出招的动作）：
    ///   一条规则管三根手指：按一下 = 快招，按住不放 = 蓄力，松开 = 重招。
    ///   拇指 = 左拳 / 左重拳（勾拳）；食指 = 右拳 / 右重拳（直拳）；后三指 = 踢 / 回旋踢；
    ///   五指握拳不放 = 抱头防守；怒气满时拇指食指捏住、往下一拽 = 必杀（同一只手）；
    ///   手往下一沉 = 蹲；手往上一提 = 跳；手掌平移 = 走（按相对位移放大，手放哪都行）。
    /// “按一下”在松开时才算（0.35 秒内弯下又伸开），这样握拳、捏合时顺带弯了的手指不会误出拳。</summary>
    public class SBGestures
    {
        public enum Act { None, JabL, JabR, Heavy, Kick, Jump, Duck, HeavyL, HeavyKick, Ult }
        public static bool IsKick(Act a) => a == Act.Kick || a == Act.HeavyKick;
        public static bool IsHeavy(Act a) => a == Act.Heavy || a == Act.HeavyL || a == Act.HeavyKick;
        public static int Arm(Act a) => a == Act.JabL || a == Act.HeavyL ? 0 : 1;

        // 弯曲阈值（带滞后，压掉追踪抖动）
        const float Down = 0.55f, Up = 0.35f, TapMax = 0.38f, ChargeAfter = 0.38f;

        class Ch { public bool down; public float t0; public bool spoiled; }
        readonly Ch thumb = new(), index = new(), rear = new();

        public bool Guard { get; private set; }       // 五指握拳
        public bool Pinch { get; private set; }       // 拇指食指捏住
        public float Charge { get; private set; }     // 蓄力 0..1（正在按住的那根手指）
        public int ChargeSide { get; private set; }   // 0 = 左拳（拇指）1 = 右拳（食指）2 = 腿（后三指）
        public float Released { get; private set; }   // 刚松开的那一下蓄了多少（0..1），重招的力度按它加
        static bool Holding(Ch c, float now) => c.down && !c.spoiled && now - c.t0 > ChargeAfter;
        public bool Charging => Holding(thumb, Time.unscaledTime) || Holding(index, Time.unscaledTime) || Holding(rear, Time.unscaledTime);
        bool pinchWas; float pinchY0, pinchT0, guardSince = -9f; bool yanked;
        public string Last { get; private set; } = "";
        float lastY, vy; bool hasY; float vertCool;

        public Act Update(SBHands.Hand h)
        {
            if (h == null || !h.tracked) { Reset(); return Act.None; }
            float now = Time.unscaledTime;
            float t = h.curl[0], ix = h.curl[1], r = (h.curl[2] + h.curl[3] + h.curl[4]) / 3f;
            float pinchD = Vector3.Distance(h.tips[0], h.tips[1]);
            Pinch = Pinch ? pinchD < 0.04f : pinchD < 0.022f;
            // 握拳 = 防守。真机上握拳时拇指常常量不出多少弯曲（它是搭在别的手指外面的），所以：
            //   进入：食指和后三指都握紧，拇指只要稍微收一点；或者三路都中等程度握住。
            //   保持：食指和后三指还握着就算（拇指不管），并且至少保持 0.15 秒，不会一抖就掉。
            bool enter = (ix > 0.5f && r > 0.5f && t > 0.18f) || (t > 0.42f && ix > 0.42f && r > 0.42f);
            bool stay = (ix > 0.3f && r > 0.3f) || now - guardSince < 0.15f;
            bool g2 = Guard ? stay : enter;
            if (g2 && !Guard) guardSince = now;
            Guard = g2;
            // 握拳 / 捏合期间按下的手指都作废（松开时不出招）
            if (Guard || Pinch) { thumb.spoiled |= thumb.down; index.spoiled |= index.down; rear.spoiled |= rear.down; }

            var act = Act.None;
            act = Pick(act, Step(thumb, t, now, Act.JabL, Act.HeavyL));
            act = Pick(act, Step(index, ix, now, Act.JabR, Act.Heavy));
            act = Pick(act, Step(rear, r, now, Act.Kick, Act.HeavyKick, needOthersOpen: true, others: Mathf.Max(t, ix)));
            Charge = 0f;
            var chs = new[] { thumb, index, rear };
            for (int i = 0; i < 3; i++)
                if (Holding(chs[i], now)) { float c = Mathf.Clamp01((now - chs[i].t0 - ChargeAfter) / 0.6f); if (c >= Charge) { Charge = c; ChargeSide = i; } }

            // 必杀：同一只手，拇指食指捏住，0.6 秒内往下一拽 5 厘米（像拽提线）
            if (Pinch && !pinchWas) { pinchY0 = h.palm.y; pinchT0 = now; yanked = false; }
            if (Pinch && !yanked && act == Act.None && now - pinchT0 < 0.6f && pinchY0 - h.palm.y > 0.05f) { act = Act.Ult; yanked = true; vertCool = 0.5f; }
            pinchWas = Pinch;

            // 手掌上下：速度够快 = 跳 / 蹲（冷却 0.4 秒，免得一次动作算两次）
            float y = h.palm.y;
            if (hasY) vy = Mathf.Lerp(vy, (y - lastY) / Mathf.Max(Time.unscaledDeltaTime, 1e-3f), 0.5f);
            lastY = y; hasY = true;
            vertCool -= Time.unscaledDeltaTime;
            if (vertCool <= 0 && act == Act.None && !Pinch)
            {
                if (vy > 0.9f) { act = Act.Jump; vertCool = 0.45f; }
                else if (vy < -0.8f) { act = Act.Duck; vertCool = 0.45f; }
            }
            if (act != Act.None) Last = act.ToString();
            return act;
        }

        static Act Pick(Act a, Act b) => a != Act.None ? a : b;

        Act Step(Ch c, float v, float now, Act tap, Act heavy, bool needOthersOpen = false, float others = 0f)
        {
            if (!c.down && v > Down) { c.down = true; c.t0 = now; c.spoiled = Guard || Pinch || (needOthersOpen && others > 0.45f); return Act.None; }
            if (c.down && v < Up)
            {
                c.down = false;
                if (c.spoiled) return Act.None;
                float held = now - c.t0;
                if (held <= TapMax) return tap;
                if (held > ChargeAfter) { Released = Mathf.Clamp01((held - ChargeAfter) / 0.6f); return heavy; }
            }
            return Act.None;
        }

        void Reset()
        {
            thumb.down = index.down = rear.down = false;
            Guard = Pinch = pinchWas = false; Charge = 0; hasY = false; vy = 0;
        }
    }
}
