using UnityEngine;

namespace StringBrawl
{
    /// <summary>编辑器里的假手：没有头显时自动走位、出拳、踢、防守，用来截图验证比赛流程（只在 SBMatch.autoPilot 打开时生效）。</summary>
    public class SBAutoPilot : MonoBehaviour
    {
        public SBHands hands;
        public SBMatch match;
        public bool fingers = true;
        [System.NonSerialized] public bool heavyMode; float holdUntil; int holdFinger;   // 测试：假手隔一会儿蓄一记重招（验证对手会不会躲）
        [System.NonSerialized] public Vector3 forcePalm; [System.NonSerialized] public bool useForce;   // 测试：把假手硬放到某个位置（验证弹网）   // 练习场里由 SBMatch 直接下指令，假手只负责走位
        readonly float[] curl = new float[5];
        float t, next; int step;

        void Update()
        {
            if (!hands || !match || !match.ArenaT) return;
            t += Time.unscaledDeltaTime;
            var ring = match.ArenaT; var rival = match.CpuRoot;
            var aim = rival ? rival.position : ring.position;
            var away = ring.position - aim; away.y = 0; away = away.sqrMagnitude > 1e-4f ? away.normalized : -ring.forward;
            var want = aim + away * 0.08f + Vector3.up * 0.18f + ring.right * Mathf.Sin(t * 0.7f) * 0.03f;
            if (useForce) want = forcePalm;
            hands.simPalmRight = Vector3.Lerp(hands.simPalmRight, want, Time.unscaledDeltaTime * 4f);
            for (int i = 0; i < 5; i++) curl[i] = Mathf.MoveTowards(curl[i], 0f, Time.unscaledDeltaTime * 8f);
            if (heavyMode && fingers)
            {
                if (t < holdUntil) { if (holdFinger == 2) curl[2] = curl[3] = curl[4] = 1f; else curl[holdFinger] = 1f; }
                else if (t > next) { holdFinger = (step++ % 3 == 2) ? 2 : 1; holdUntil = t + 0.95f; next = t + 2.1f; }
            }
            else if (fingers && t > next)
            {
                step++;
                int k = step % 8;
                if (k == 0 || k == 3) curl[0] = 1f;                         // 左拳
                else if (k == 1 || k == 4) curl[1] = 1f;                    // 右拳
                else if (k == 2) { curl[2] = curl[3] = curl[4] = 1f; }      // 踢
                next = t + 0.35f;
                if (k == 5) next = t + 0.8f;
            }
            hands.simRight = curl;
        }
    }
}
