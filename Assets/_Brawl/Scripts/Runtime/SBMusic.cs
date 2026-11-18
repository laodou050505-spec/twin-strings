using System.Threading.Tasks;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>背景音乐：全部用代码现场合成（原创，没有任何外部曲目 / 采样，可商用无版权风险）。
    /// - 菜单 / 练习：92 拍每分钟，D 小调五声音阶的拨弦琶音（Karplus-Strong 弹拨弦算法，像古筝 / 提线的“铮”声）+ 木鱼 + 轻太鼓 + 低音持续音；
    /// - 比赛：132 拍每分钟，四拍底鼓 + 太鼓切分 + 二四拍拍手 + 八分镲 + 低音贝斯走线 + 五声音阶拨弦主旋律，开头一声锣；
    /// 两段都是无缝循环，切换时交叉淡入淡出。合成放在后台线程，不卡开场。</summary>
    public class SBMusic : MonoBehaviour
    {
        const int SR = 32000;
        AudioSource menuSrc, fightSrc, crowdSrc;
        Task<float[]> menuJob, fightJob, crowdJob;
        static readonly int ExciteId = Shader.PropertyToID("_SB_Excite");
        bool wantFight;
        public float volume = 0.32f;

        void Start()
        {
            menuSrc = MakeSrc(); fightSrc = MakeSrc();
            menuJob = Task.Run(() => Compose(false));
            fightJob = Task.Run(() => Compose(true));
            crowdSrc = MakeSrc(); crowdJob = Task.Run(Crowd);
        }

        AudioSource MakeSrc()
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.loop = true; a.spatialBlend = 0; a.volume = 0; a.playOnAwake = false; a.priority = 0;
            return a;
        }

        public void Fight(bool on) => wantFight = on;

        void Update()
        {
            Ready(ref menuJob, menuSrc, "menu");
            Ready(ref fightJob, fightSrc, "fight");
            Ready(ref crowdJob, crowdSrc, "crowd");
            // 观众底噪：跟着场上激烈程度（八角笼里打中、KO 时会推高）起伏
            float ex = Shader.GetGlobalFloat(ExciteId);
            crowdSrc.volume = Mathf.MoveTowards(crowdSrc.volume, 0.1f + ex * 0.45f, Time.unscaledDeltaTime * 0.8f);
            float dt = Time.unscaledDeltaTime;
            menuSrc.volume = Mathf.MoveTowards(menuSrc.volume, wantFight ? 0 : volume, dt * 0.5f);
            fightSrc.volume = Mathf.MoveTowards(fightSrc.volume, wantFight ? volume : 0, dt * 0.8f);
            // 比赛音乐每次进场从头（锣声）开始
            if (wantFight && fightSrc.clip && fightSrc.volume < 0.02f && !justStarted) { fightSrc.timeSamples = 0; justStarted = true; }
            if (!wantFight) justStarted = false;
        }
        bool justStarted;

        static void Ready(ref Task<float[]> job, AudioSource src, string name)
        {
            if (job == null || !job.IsCompleted) return;
            var data = job.Result; job = null;
            var c = AudioClip.Create("bgm_" + name, data.Length, 1, SR, false); c.SetData(data, 0);
            src.clip = c; src.Play();
        }

        // 观众人声底噪：很多“人”的嗡嗡声叠在一起——每个人是一段带通噪声、各自慢慢起伏，偶尔一阵起哄
        static float[] Crowd()
        {
            int len = SR * 9; var o = new float[len]; var rnd = new System.Random(5);
            float N() => (float)(rnd.NextDouble() * 2 - 1);
            for (int v = 0; v < 24; v++)
            {
                float lo = 0, hi = 0, a = 0.02f + (float)rnd.NextDouble() * 0.05f, rate = 0.15f + (float)rnd.NextDouble() * 0.6f, ph = (float)rnd.NextDouble() * 6.28f;
                float cut = 0.04f + (float)rnd.NextDouble() * 0.08f;
                for (int i = 0; i < len; i++)
                {
                    float x = N(); lo += (x - lo) * cut; hi += (lo - hi) * 0.02f;
                    float t = i / (float)SR;
                    // 9 秒循环：起伏频率取整数周期，接缝不跳
                    float env = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * Mathf.Round(rate * 9f) / 9f * t + ph);
                    o[i] += (lo - hi) * env * a * 6f;
                }
            }
            float peak = 0; foreach (var x in o) peak = Mathf.Max(peak, Mathf.Abs(x));
            for (int i = 0; i < len; i++) o[i] = o[i] / Mathf.Max(peak, 1e-4f) * 0.7f;
            int f = SR / 10;   // 首尾 0.1 秒交叉淡化，循环不咔哒
            for (int i = 0; i < f; i++) { float k = i / (float)f; o[i] = o[i] * k + o[len - f + i] * (1 - k); }
            System.Array.Resize(ref o, len - f);
            return o;
        }

        // ───────────── 作曲 ─────────────
        static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        static float[] Compose(bool fight)
        {
            float bpm = fight ? 132f : 92f; int bars = fight ? 8 : 4;
            float step = 60f / bpm / 4f;                 // 十六分音符
            int len = Mathf.RoundToInt(bars * 16 * step * SR);
            var o = new float[len];
            var rnd = new System.Random(fight ? 11 : 7);
            float N() => (float)(rnd.NextDouble() * 2 - 1);
            int At(int bar, int s16) => Mathf.RoundToInt((bar * 16 + s16) * step * SR);

            // 低音根音（D 小调）：每小节一个
            int[] roots = fight ? new[] { 38, 38, 34, 36, 38, 38, 41, 33 } : new[] { 38, 34, 36, 33 };
            // 五声音阶主题（十六分位置, MIDI）
            (int s, int n)[] motifA = { (0, 62), (3, 65), (6, 67), (8, 69), (10, 72), (12, 69), (14, 67) };
            (int s, int n)[] motifB = { (0, 74), (2, 72), (4, 69), (6, 67), (8, 65), (10, 67), (12, 62) };

            for (int bar = 0; bar < bars; bar++)
            {
                int root = roots[bar];
                if (fight)
                {
                    for (int b = 0; b < 4; b++) Kick(o, At(bar, b * 4), 0.55f);
                    Taiko(o, At(bar, 0), 0.6f, N); Taiko(o, At(bar, 6), 0.45f, N); if (bar % 2 == 1) Taiko(o, At(bar, 14), 0.4f, N);
                    Clap(o, At(bar, 4), 0.32f, N); Clap(o, At(bar, 12), 0.32f, N);
                    for (int h = 0; h < 8; h++) Hat(o, At(bar, h * 2), h % 2 == 1 ? 0.13f : 0.07f, N);
                    foreach (var s in new[] { 3, 7, 10, 14 }) Block(o, At(bar, s), 0.12f);
                    // 贝斯：八分音符走线（根音 / 八度 / 五度）
                    int[] line = { 0, 0, 12, 0, 7, 0, 10, 12 };
                    for (int e = 0; e < 8; e++) Bass(o, At(bar, e * 2), step * 2 * 0.9f, Hz(root + line[e]), 0.22f);
                    var m = (bar / 2) % 2 == 0 ? motifA : motifB;
                    int up = bar >= 4 && bar < 6 ? 12 : 0;
                    if (bar % 2 == 0 || bar == 7)
                        foreach (var (s, n) in m) Pluck(o, At(bar, s), Hz(n + up), 0.9f, 0.3f, N);
                    else
                        foreach (var (s, n) in m) if (s % 4 == 0) Pluck(o, At(bar, s), Hz(n + up - 12), 0.7f, 0.18f, N);
                }
                else
                {
                    Taiko(o, At(bar, 0), 0.35f, N); if (bar % 2 == 1) Taiko(o, At(bar, 10), 0.25f, N);
                    foreach (var s in new[] { 4, 7, 12, 14 }) Block(o, At(bar, s), 0.09f);
                    Bass(o, At(bar, 0), step * 16 * 0.95f, Hz(root), 0.16f);
                    Bass(o, At(bar, 0), step * 16 * 0.95f, Hz(root + 7), 0.07f);
                    // 琶音：五声音阶上行再下行
                    int[] arp = { 0, 3, 5, 7, 10, 12, 10, 7 };
                    for (int a = 0; a < 8; a++) Pluck(o, At(bar, a * 2), Hz(root + 24 + arp[a]), 1.4f, a == 0 ? 0.28f : 0.18f, N);
                    if (bar == bars - 1) Pluck(o, At(bar, 14), Hz(root + 36), 1.6f, 0.15f, N);
                }
            }
            if (fight) Gong(o, 0, 0.25f);
            // 循环接缝：尾巴叠回开头（拨弦余音不会被截断）
            // 软削波 + 归一
            float peak = 0;
            for (int i = 0; i < len; i++) { o[i] = (float)System.Math.Tanh(o[i] * 1.3f); peak = Mathf.Max(peak, Mathf.Abs(o[i])); }
            float g = peak > 0 ? 0.85f / peak : 1f;
            for (int i = 0; i < len; i++) o[i] *= g;
            // 首尾各 5 毫秒淡入淡出，防止循环点“咔”一声
            int f = SR / 200;
            for (int i = 0; i < f; i++) { float k = i / (float)f; o[i] *= k; o[len - 1 - i] *= k; }
            return o;
        }

        // 写入（越界的部分绕回开头，循环天然无缝）
        static void Add(float[] o, int at, float v) { int i = at % o.Length; if (i < 0) i += o.Length; o[i] += v; }

        static void Kick(float[] o, int at, float vol)
        {
            int n = (int)(0.28f * SR); float ph = 0;
            for (int i = 0; i < n; i++) { float t = i / (float)SR; float f = 45f + 110f * Mathf.Exp(-t * 30f); ph += f / SR; Add(o, at + i, Mathf.Sin(ph * 2 * Mathf.PI) * Mathf.Exp(-t * 9f) * vol); }
        }
        static void Taiko(float[] o, int at, float vol, System.Func<float> N)
        {
            int n = (int)(0.6f * SR); float ph = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR; float f = 62f + 40f * Mathf.Exp(-t * 18f); ph += f / SR; lp = Mathf.Lerp(lp, N(), 0.15f);
                Add(o, at + i, (Mathf.Sin(ph * 2 * Mathf.PI) * Mathf.Exp(-t * 5f) + lp * Mathf.Exp(-t * 40f) * 1.5f) * vol);
            }
        }
        static void Clap(float[] o, int at, float vol, System.Func<float> N)
        {
            int n = (int)(0.16f * SR); float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR; float x = N(); lp = Mathf.Lerp(lp, x, 0.3f);
                float env = Mathf.Exp(-t * 22f) * (t < 0.02f ? (Mathf.Repeat(t, 0.007f) < 0.0035f ? 1f : 0.4f) : 1f);
                Add(o, at + i, (x - lp) * env * vol);
            }
        }
        static void Hat(float[] o, int at, float vol, System.Func<float> N)
        {
            int n = (int)(0.05f * SR); float lp = 0;
            for (int i = 0; i < n; i++) { float t = i / (float)SR; float x = N(); lp = Mathf.Lerp(lp, x, 0.6f); Add(o, at + i, (x - lp) * Mathf.Exp(-t * 70f) * vol); }
        }
        static void Block(float[] o, int at, float vol)
        {
            int n = (int)(0.07f * SR);
            for (int i = 0; i < n; i++) { float t = i / (float)SR; Add(o, at + i, (Mathf.Sin(t * 2 * Mathf.PI * 880f) + 0.5f * Mathf.Sin(t * 2 * Mathf.PI * 1420f)) * Mathf.Exp(-t * 60f) * vol); }
        }
        static void Bass(float[] o, int at, float dur, float f, float vol)
        {
            int n = (int)(dur * SR);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR; float p = t * f * 2 * Mathf.PI;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Min(1f, (dur - t) * 60f) * (0.75f + 0.25f * Mathf.Exp(-t * 6f));
                Add(o, at + i, (Mathf.Sin(p) + 0.35f * Mathf.Sin(p * 2) + 0.15f * Mathf.Sin(p * 3)) * env * vol);
            }
        }
        // Karplus-Strong 拨弦：一小段噪声在延迟线里反复平均衰减，听起来就是“铮”的一声弦
        static void Pluck(float[] o, int at, float f, float dur, float vol, System.Func<float> N)
        {
            int p = Mathf.Max(2, Mathf.RoundToInt(SR / f)); var buf = new float[p];
            for (int i = 0; i < p; i++) buf[i] = N();
            // 拨的位置：去掉一点高频（不刺耳）
            for (int k = 0; k < 2; k++) for (int i = 0; i < p; i++) buf[i] = 0.5f * (buf[i] + buf[(i + 1) % p]);
            int n = (int)(dur * SR), j = 0; float decay = 0.996f;
            for (int i = 0; i < n; i++)
            {
                float v = buf[j]; int nx = (j + 1) % p;
                buf[j] = 0.5f * (buf[j] + buf[nx]) * decay; j = nx;
                float env = Mathf.Min(1f, (n - i) / (SR * 0.05f));
                Add(o, at + i, v * vol * env);
            }
        }
        static void Gong(float[] o, int at, float vol)
        {
            float[] fr = { 98f, 141f, 196.5f, 233f, 311f, 392.4f }; float[] am = { 1f, 0.6f, 0.5f, 0.35f, 0.25f, 0.18f };
            int n = (int)(3.2f * SR);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR, s = 0;
                for (int k = 0; k < fr.Length; k++) s += Mathf.Sin(t * 2 * Mathf.PI * fr[k] * (1f + 0.004f * Mathf.Sin(t * 3f + k))) * am[k] * Mathf.Exp(-t * (0.9f + k * 0.35f));
                Add(o, at + i, s * vol * Mathf.Min(1f, t * 80f));
            }
        }
    }
}
