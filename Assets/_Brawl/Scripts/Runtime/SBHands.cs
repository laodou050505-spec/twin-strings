using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace StringBrawl
{
    /// <summary>读 PICO 手部追踪 26 个关节，算出每根手指的弯曲程度（0 = 伸直，1 = 弯到底）、手掌位置和朝向。
    /// 弯曲 = 指根、中节、末节三个关节的弯折角之和 ÷ 该手指弯到底时的角度；
    /// 每个人手不一样，所以再按这只手出现过的最直 / 最弯自动校准（最直和最弯会慢慢回缩，换人玩也能适应）。
    /// 编辑器里没有真手，用键盘模拟（见 SBFingerTest）。</summary>
    [DefaultExecutionOrder(-50)]
    public class SBHands : MonoBehaviour
    {
        public Transform trackingOrigin;

        public class Hand
        {
            public bool tracked;
            public Vector3 palm;                    // 手掌中心（世界坐标）
            public Quaternion palmRot = Quaternion.identity;
            public readonly float[] raw = new float[5];   // 原始弯折角（度）：拇指、食指、中指、无名指、小指
            public readonly float[] curl = new float[5];  // 校准后的弯曲 0..1
            public readonly Vector3[] tips = new Vector3[5];
            public readonly float[] lo = { 999, 999, 999, 999, 999 }, hi = { -999, -999, -999, -999, -999 };
            public float trackedTime, lostTime;     // 统计：追踪到 / 丢失的累计秒数
            public int lostEvents;
        }

        public Hand Left { get; } = new();
        public Hand Right { get; } = new();
        public Hand Get(bool right) => right ? Right : Left;

        /// <summary>编辑器 / 测试用：非空时代替真手的弯曲值（0..1）。</summary>
        [System.NonSerialized] public float[] simLeft, simRight;
        [System.NonSerialized] public Vector3 simPalmLeft = new(-0.12f, 1.0f, 0.35f), simPalmRight = new(0.12f, 1.0f, 0.35f);

        // 每根手指弯到底的大概角度（度），用来把原始角度换成 0..1 的初值；之后按个人范围再校准
        static readonly float[] FullCurl = { 110f, 230f, 240f, 240f, 230f };
        const float MinSpan = 35f;      // 校准范围至少这么宽，避免刚开始一点点抖动就满格

#if ENABLE_PICO_XR_SDK
        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
#endif

        void Update()
        {
            ReadHand(false, Left, simLeft, simPalmLeft);
            ReadHand(true, Right, simRight, simPalmRight);
        }

        void ReadHand(bool right, Hand h, float[] sim, Vector3 simPalm)
        {
            bool was = h.tracked;
            if (sim != null)
            {
                h.tracked = true; h.palm = simPalm; h.palmRot = Quaternion.identity;
                // 假手：拇指离食指远一点，不会被当成捏合
                for (int i = 0; i < 5; i++) { h.curl[i] = Mathf.Clamp01(sim[i]); h.raw[i] = sim[i] * FullCurl[i]; h.tips[i] = simPalm + new Vector3((i - 2) * 0.03f + (i == 0 ? -0.03f : 0f), -0.01f, 0.08f); }
            }
            else h.tracked = ReadPico(right, h);
            if (h.tracked) h.trackedTime += Time.unscaledDeltaTime; else h.lostTime += Time.unscaledDeltaTime;
            if (was && !h.tracked) h.lostEvents++;
        }

        bool ReadPico(bool right, Hand h)
        {
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            if (PXR_HandTracking.GetActiveInputDevice() != ActiveInputDevice.HandTrackingActive) return false;
            if (!PXR_HandTracking.GetJointLocations(right ? HandType.HandRight : HandType.HandLeft, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return false;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            bool ok = true;
            Vector3 P(HandJoint jn)
            {
                var l = locs.jointLocations[(int)jn];
                if (((ulong)l.locationStatus & need) == 0) ok = false;
                var p = l.pose.Position.ToVector3();
                return trackingOrigin ? trackingOrigin.TransformPoint(p) : p;
            }
            var palmLoc = locs.jointLocations[(int)HandJoint.JointPalm];
            h.palm = P(HandJoint.JointPalm);
            var q = palmLoc.pose.Orientation.ToQuat();
            h.palmRot = trackingOrigin ? trackingOrigin.rotation * q : q;
            // 拇指：掌骨 → 近节 → 远节 → 指尖，两个关节
            h.raw[0] = Bend(P(HandJoint.JointThumbMetacarpal), P(HandJoint.JointThumbProximal), P(HandJoint.JointThumbDistal))
                     + Bend(P(HandJoint.JointThumbProximal), P(HandJoint.JointThumbDistal), P(HandJoint.JointThumbTip));
            h.tips[0] = P(HandJoint.JointThumbTip);
            // 其余四指：掌骨 → 近节 → 中节 → 远节 → 指尖，三个关节
            int[] baseJ = { 0, (int)HandJoint.JointIndexMetacarpal, (int)HandJoint.JointMiddleMetacarpal, (int)HandJoint.JointRingMetacarpal, (int)HandJoint.JointLittleMetacarpal };
            for (int f = 1; f < 5; f++)
            {
                var m = P((HandJoint)baseJ[f]); var p = P((HandJoint)(baseJ[f] + 1)); var i = P((HandJoint)(baseJ[f] + 2));
                var d = P((HandJoint)(baseJ[f] + 3)); var t = P((HandJoint)(baseJ[f] + 4));
                h.raw[f] = Bend(m, p, i) + Bend(p, i, d) + Bend(i, d, t);
                h.tips[f] = t;
            }
            if (!ok) return false;
            Calibrate(h);
            return true;
#else
            return false;
#endif
        }

        /// <summary>b 处的弯折角（度）：a→b 和 b→c 的夹角，伸直 = 0。</summary>
        static float Bend(Vector3 a, Vector3 b, Vector3 c) => Vector3.Angle(b - a, c - b);

        static void Calibrate(Hand h)
        {
            for (int f = 0; f < 5; f++)
            {
                float r = h.raw[f];
                // 记下出现过的最直 / 最弯，并以很慢的速度往中间回缩
                if (r < h.lo[f]) h.lo[f] = r; else h.lo[f] += Time.deltaTime * 2f;
                if (r > h.hi[f]) h.hi[f] = r; else h.hi[f] -= Time.deltaTime * 2f;
                float lo = Mathf.Min(h.lo[f], 25f), hi = Mathf.Max(h.hi[f], lo + MinSpan, FullCurl[f] * 0.45f);
                float c = Mathf.InverseLerp(lo + 6f, hi - 6f, r);
                // 平滑，压掉追踪抖动
                h.curl[f] = Mathf.Lerp(h.curl[f], c, 1f - Mathf.Exp(-Time.deltaTime * 25f));
            }
        }
    }
}
