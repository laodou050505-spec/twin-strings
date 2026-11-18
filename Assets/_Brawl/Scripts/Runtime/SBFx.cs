using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>打击感：顿帧（时间几乎停一下）、木屑 / 稻草飞溅、程序合成的声音（不用素材文件，没有版权问题）。
    /// 声音全部现场合成：打中 = 低频“咚” + 一小段噪声；重击更低更长；挡住 = 清脆的木头“哒”；挥空 = 一阵风声；
    /// 掉下台 = 往下滑的哨音；回合钟 = 两个音叠在一起的“叮”。</summary>
    public class SBFx : MonoBehaviour
    {
        static SBFx I;
        public Material litTemplate;

        AudioClip hit, heavy, block, whoosh, fall, bell, crowd;
        readonly List<Bit> bits = new();
        readonly Stack<Transform> pool = new();
        Material chipMat;
        float stopT;

        class Bit { public Transform t; public Vector3 v, w; public float life;  public float g = 3f; }

        void Awake()
        {
            I = this;
            hit = Make(0.16f, (t, n) => Mathf.Sin(t * 2 * Mathf.PI * (140f - 260f * t)) * Mathf.Exp(-t * 28f) * 0.9f + n * Mathf.Exp(-t * 60f) * 0.6f);
            heavy = Make(0.32f, (t, n) => Mathf.Sin(t * 2 * Mathf.PI * (90f - 120f * t)) * Mathf.Exp(-t * 12f) + n * Mathf.Exp(-t * 25f) * 0.7f);
            block = Make(0.12f, (t, n) => (Mathf.Sin(t * 2 * Mathf.PI * 1250f) * 0.6f + Mathf.Sin(t * 2 * Mathf.PI * 1870f) * 0.3f) * Mathf.Exp(-t * 45f) + n * Mathf.Exp(-t * 120f) * 0.3f);
            float lp = 0;
            whoosh = Make(0.2f, (t, n) => { lp = Mathf.Lerp(lp, n, 0.08f + t * 0.6f); return lp * Mathf.Sin(t / 0.2f * Mathf.PI) * 2.2f; });
            fall = Make(0.9f, (t, n) => Mathf.Sin(2 * Mathf.PI * (900f * t - 350f * t * t)) * 0.35f * (1f - t / 0.9f));
            bell = Make(1.4f, (t, n) => (Mathf.Sin(t * 2 * Mathf.PI * 880f) + 0.6f * Mathf.Sin(t * 2 * Mathf.PI * 1320f) + 0.3f * Mathf.Sin(t * 2 * Mathf.PI * 2210f)) * Mathf.Exp(-t * 3f) * 0.45f);
            float c1 = 0, c2 = 0;
            crowd = Make(1.2f, (t, n) => { c1 = Mathf.Lerp(c1, n, 0.05f); c2 = Mathf.Lerp(c2, c1, 0.05f); return (c1 - c2) * 6f * Mathf.Sin(t / 1.2f * Mathf.PI); });
            chipMat = new Material(litTemplate) { color = new Color(.85f, .72f, .45f) };
        }

        static AudioClip Make(float len, System.Func<float, float, float> f)
        {
            int sr = 44100, n = Mathf.CeilToInt(len * sr);
            var d = new float[n];
            var rnd = new System.Random(n);
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)sr, (float)(rnd.NextDouble() * 2 - 1)), -1f, 1f);
            var c = AudioClip.Create("fx", n, 1, sr, false); c.SetData(d, 0); return c;
        }

        static void Play(AudioClip c, Vector3 p, float vol, float pitch = 1f)
        {
            if (!I || c == null) return;
            var go = new GameObject("声");
            go.transform.position = p;
            var a = go.AddComponent<AudioSource>();
            a.clip = c; a.volume = vol; a.pitch = pitch * Random.Range(0.93f, 1.07f);
            a.spatialBlend = 1f; a.minDistance = 0.3f; a.maxDistance = 5f; a.dopplerLevel = 0;
            a.Play(); Destroy(go, c.length / Mathf.Max(0.1f, a.pitch) + 0.1f);
        }

        public static void Hit(Vector3 p, Vector3 dir, float power, bool blocked)
        {
            if (!I) return;
            if (blocked) { Play(I.block, p, 0.8f); I.Burst(p, dir, 4, 0.5f, new Color(.6f, .75f, 1f)); I.Stop(0.04f); return; }
            Play(power > 2f ? I.heavy : I.hit, p, Mathf.Clamp01(0.6f + power * 0.2f), 1.1f - power * 0.08f);
            I.Burst(p, dir, Mathf.RoundToInt(6 + power * 6), 0.6f + power * 0.25f, new Color(.88f, .75f, .45f));
            I.Stop(0.05f + power * 0.03f);
        }
        public static void Whoosh(Vector3 p, float strength) { if (I) Play(I.whoosh, p, 0.25f * strength, 0.8f + strength * 0.4f); }
        public static void Fall(Vector3 p) { if (I) { Play(I.fall, p, 0.7f); Play(I.crowd, p, 0.5f); } }
        /// <summary>木偶说话：从木偶的位置发声。同一只木偶同时只有一句（新的一句开口，上一句就停）。
        /// 音源等“这句话真的播完了”才收掉（不按固定时长掐），优先级最高（场上再吵，台词也不会被别的音效挤掉）。</summary>
        static readonly Dictionary<int, SBVoiceOnce> speaking = new();
        public static void Voice(AudioClip c, Vector3 p, int who = 0)
        {
            if (!I || c == null) return;
            if (speaking.TryGetValue(who, out var old) && old) Destroy(old.gameObject);
            var go = new GameObject("台词音源"); go.transform.position = p;
            var a = go.AddComponent<AudioSource>();
            a.clip = c; a.volume = 1f; a.pitch = 1f; a.priority = 0; a.playOnAwake = false;
            a.spatialBlend = 0.6f; a.minDistance = 0.5f; a.maxDistance = 6f; a.dopplerLevel = 0;
            speaking[who] = go.AddComponent<SBVoiceOnce>();
        }
        public static void Bell(Vector3 p) { if (I) Play(I.bell, p, 0.8f); }
        /// <summary>界面音：手移到木牌上“哒”一声（小），选中“咚”一声（大）。</summary>
        public static void Ui(Vector3 p, bool big) { if (I) { if (big) { Play(I.heavy, p, 0.55f, 1.35f); Play(I.block, p, 0.4f, 0.8f); } else Play(I.block, p, 0.35f, 1.7f); } }

        // —— 必杀技效果 ——
        public Material glowMat;
        public static void Ult(Vector3 p, string kind)
        {
            if (!I) return;
            Play(I.bell, p, 0.5f, 1.6f); Play(I.crowd, p, 0.6f, 1.1f);
            I.Burst(p, Vector3.up, 16, 0.5f, new Color(1f, .85f, .35f));
            Shockwave(p - Vector3.up * 0.08f, 0.06f, new Color(1f, .85f, .35f));
        }

        /// <summary>地面一圈往外扩的光环（千斤坠、开场）。</summary>
        public static void Shockwave(Vector3 p, float radius, Color c)
        {
            if (!I || !I.glowMat) return;
            var g = new GameObject("冲击波"); g.transform.SetParent(I.transform, false); g.transform.position = p + Vector3.up * 0.002f;
            g.AddComponent<MeshFilter>().sharedMesh = Ring(48);
            var m = new Material(I.glowMat); m.SetColor("_Color", c); m.SetFloat("_Fade", 0f);
            g.AddComponent<MeshRenderer>().material = m;
            I.rings.Add((g.transform, m, 0f, radius, c));
            Play(I.heavy, p, 1f, 0.6f);
        }

        /// <summary>残影拖尾（纸鸢、铁甲、不倒翁）：沿路留下一串慢慢消失的小光点。</summary>
        public static void Trail(Vector3 p, Color c)
        {
            if (!I || !I.glowMat || Time.frameCount % 2 != 0) return;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(I.transform, false); q.transform.position = p; q.transform.localScale = Vector3.one * 0.02f;
            var m = new Material(I.glowMat); m.SetColor("_Color", c * 0.6f); m.SetFloat("_Fade", 0f);
            q.GetComponent<Renderer>().material = m;
            I.trails.Add((q.transform, m, 0.35f, c * 0.6f));
        }

        /// <summary>乌鸦群：一只只从 from 飞向对手，碰到就啄一下（回调）。</summary>
        public static void Crows(Vector3 from, SBPuppet target, int n, System.Action<SBPuppet, int> onPeck)
        {
            if (!I) return;
            var black = new Material(I.chipMat) { color = new Color(.06f, .06f, .07f) };
            for (int i = 0; i < n; i++)
            {
                var c = new GameObject("乌鸦").transform; c.SetParent(I.transform, false); c.position = from;
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(body.GetComponent<Collider>()); body.transform.SetParent(c, false); body.transform.localScale = new Vector3(0.012f, 0.01f, 0.02f); body.GetComponent<Renderer>().material = black;
                foreach (int sx in new[] { -1, 1 })
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(w.GetComponent<Collider>()); w.transform.SetParent(c, false);
                    w.transform.localPosition = new Vector3(sx * 0.012f, 0.002f, 0); w.transform.localScale = new Vector3(0.02f, 0.001f, 0.012f); w.GetComponent<Renderer>().material = black;
                }
                I.crows.Add(new Crow { t = c, target = target, delay = i * 0.15f, cb = onPeck, idx = i, ph = Random.value * 6f, off = Random.insideUnitSphere * 0.05f });
            }
            Play(I.whoosh, from, 0.6f, 0.6f);
        }

        class Crow { public Transform t; public SBPuppet target; public float delay, life, ph; public int idx; public bool done; public Vector3 off; public System.Action<SBPuppet, int> cb; }
        readonly List<Crow> crows = new();
        readonly List<(Transform t, Material m, float age, float r, Color c)> rings = new();
        readonly List<(Transform t, Material m, float life, Color c)> trails = new();

        static Mesh Ring(int seg)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.85f); v.Add(d); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); col.Add(Color.white); col.Add(Color.white);
            }
            for (int i = 0; i < seg; i++) { int b = i * 2; t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        void TickUlt(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i]; r.age += dt; rings[i] = r;
                float k = r.age / 0.5f;
                r.t.localScale = Vector3.one * r.r * Mathf.Lerp(0.1f, 1f, Mathf.Sqrt(Mathf.Clamp01(k)));
                r.m.SetColor("_Color", r.c * (1f - Mathf.Clamp01(k)) * 1.5f);
                if (k >= 1f) { Destroy(r.t.gameObject); rings.RemoveAt(i); }
            }
            for (int i = trails.Count - 1; i >= 0; i--)
            {
                var r = trails[i]; r.life -= dt; trails[i] = r;
                if (Camera.main) r.t.rotation = Quaternion.LookRotation(r.t.position - Camera.main.transform.position);
                r.m.SetColor("_Color", r.c * Mathf.Clamp01(r.life / 0.35f));
                if (r.life <= 0) { Destroy(r.t.gameObject); trails.RemoveAt(i); }
            }
            for (int i = crows.Count - 1; i >= 0; i--)
            {
                var c = crows[i];
                if (c.delay > 0) { c.delay -= dt; continue; }
                c.life += dt;
                if (!c.target || !c.target.Root) { Destroy(c.t.gameObject); crows.RemoveAt(i); continue; }
                var tp = c.target.Root.position + Vector3.up * c.target.H * 0.75f + c.off;
                var dir = tp - c.t.position;
                c.t.position += dir.normalized * Mathf.Min(dir.magnitude, dt * 0.55f) + Vector3.up * Mathf.Sin(c.life * 12f + c.ph) * 0.002f;
                if (dir.sqrMagnitude > 1e-6f) c.t.rotation = Quaternion.LookRotation(dir);
                foreach (Transform w in c.t) if (w.localScale.x > 0.015f) w.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(c.life * 40f) * 40f * Mathf.Sign(w.localPosition.x));
                if (!c.done && dir.magnitude < 0.015f) { c.done = true; c.cb?.Invoke(c.target, c.idx); Play(hit, c.t.position, 0.4f, 1.6f); Burst(c.t.position, dir, 4, 0.4f, new Color(.1f, .1f, .1f)); }
                if (c.done || c.life > 2.5f) { Destroy(c.t.gameObject); crows.RemoveAt(i); }
            }
        }
        public static void Cheer(Vector3 p) { if (I) Play(I.crowd, p, 0.8f, 1.2f); }

        // 顿帧：时间流速降到 5%，用不受时间流速影响的计时恢复
        void Stop(float sec) { stopT = Mathf.Max(stopT, sec); Time.timeScale = 0.05f; }

        /// <summary>拳套被打穿：几团棉絮崩出来，轻飘飘地落下去。</summary>
        public static void Stuffing(Vector3 p, Vector3 dir)
        {
            if (!I) return;
            for (int i = 0; i < 7; i++)
            {
                Transform t;
                if (I.pool.Count > 0) t = I.pool.Pop();
                else { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>()); g.GetComponent<Renderer>().material = I.chipMat; t = g.transform; t.SetParent(I.transform, false); }
                t.gameObject.SetActive(true);
                t.position = p + Random.insideUnitSphere * 0.006f; t.localScale = Vector3.one * Random.Range(0.006f, 0.011f); t.rotation = Random.rotation;
                var v = (dir.normalized * 0.5f + Random.insideUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(0.18f, 0.42f);
                I.bits.Add(new Bit { t = t, v = v, w = Random.insideUnitSphere * 260f, life = Random.Range(0.9f, 1.4f), g = 0.9f });
                t.GetComponent<Renderer>().material.color = new Color(.97f, .94f, .86f);
            }
        }

        void Burst(Vector3 p, Vector3 dir, int n, float speed, Color c)
        {
            for (int i = 0; i < n; i++)
            {
                Transform t;
                if (pool.Count > 0) t = pool.Pop();
                else
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                    g.GetComponent<Renderer>().material = chipMat; t = g.transform; t.SetParent(transform, false);
                }
                t.gameObject.SetActive(true);
                t.position = p; t.localScale = new Vector3(0.004f, 0.002f, Random.Range(0.006f, 0.014f));
                t.rotation = Random.rotation;
                var v = (dir.normalized + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f).normalized * speed * Random.Range(0.4f, 1f);
                bits.Add(new Bit { t = t, v = v, w = Random.insideUnitSphere * 900f, life = Random.Range(0.4f, 0.8f), g = 3f });
                t.GetComponent<Renderer>().material.color = c;
            }
        }

        void Update()
        {
            if (stopT > 0) { stopT -= Time.unscaledDeltaTime; if (stopT <= 0) Time.timeScale = 1f; }
            TickUlt(Time.deltaTime);
            float dt = Time.unscaledDeltaTime;
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.life -= dt; b.v += Vector3.down * b.g * dt; if (b.g < 2f) { b.v *= 1f - dt * 1.8f; b.t.localScale *= 1f - dt * 0.5f; }   // 棉絮：轻，飘着落，越飘越小
                b.t.position += b.v * dt; b.t.Rotate(b.w * dt);
                if (b.life <= 0) { b.t.gameObject.SetActive(false); pool.Push(b.t); bits.RemoveAt(i); }
            }
        }

        void OnDestroy() { Time.timeScale = 1f; }
    }

    /// <summary>一句台词的音源：等音频数据就绪再开口，播完了（不是到了某个时间）才销毁自己。用不受游戏慢动作影响的时间计时。</summary>
    public class SBVoiceOnce : MonoBehaviour
    {
        AudioSource a; bool started; float t0;
        void Awake() { a = GetComponent<AudioSource>(); t0 = Time.unscaledTime; }
        void Update()
        {
            if (!a || !a.clip) { Destroy(gameObject); return; }
            if (!started)
            {
                if (a.clip.loadState == AudioDataLoadState.Unloaded) a.clip.LoadAudioData();
                if (a.clip.loadState == AudioDataLoadState.Loaded) { a.Play(); started = true; t0 = Time.unscaledTime; }
                else if (a.clip.loadState == AudioDataLoadState.Failed || Time.unscaledTime - t0 > 3f) Destroy(gameObject);
                return;
            }
            if (!a.isPlaying && Time.unscaledTime - t0 > 0.25f) Destroy(gameObject);
        }
    }
}
