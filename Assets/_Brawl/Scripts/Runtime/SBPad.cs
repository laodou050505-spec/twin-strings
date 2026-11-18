using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>八角笼的软包：黑色人造革包泡棉。
    /// 护栏 / 底边：每两根立柱之间一段，截面是圆角矩形（超椭圆），每段分成两块枕头一样鼓起来的软包、接缝处收紧；
    /// 上下两条压线包边；每块软包两道绑带（带铜扣）把软垫绑在下面的钢管上；外侧印标志。
    /// 立柱：圆柱软包（中间微鼓），三道绑带。全部程序建模、平滑法线。</summary>
    public static class SBPad
    {
        // 圆角矩形截面上的一点：ang 绕一圈，半宽 hw、半高 hh，n 越大越方
        static Vector2 Prof(float ang, float hw, float hh, float n = 3.2f)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            return new Vector2(hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n), hh * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / n));
        }

        /// <summary>两块软包的鼓起程度：t = 0..1 沿这一段；两头和中间接缝处收紧，每块中间鼓。</summary>
        static float Pillow(float t) { float f = (t * 2f) % 1f; return 0.86f + 0.14f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(f * Mathf.PI)), 0.55f); }

        public class Out { public Mesh pad, trim, strap; public List<(Vector3 pos, Vector3 outN)> logos = new(); }

        /// <summary>沿八角形每条边（两根立柱之间）做一段软包。R = 立柱所在外接圆半径；cy = 截面中心高度；hw/hh = 截面半宽 / 半高；gap = 两头留给立柱的空。</summary>
        public static Out Rail(float R, float cy, float hw, float hh, float gap, bool piping, int strapsPerPad, bool logos)
        {
            var o = new Out();
            var pv = new List<Vector3>(); var pt = new List<int>(); var puv = new List<Vector2>();
            var trim = new SBGeo(); var strap = new SBGeo();
            const int Ring = 24, Len = 28;
            for (int e = 0; e < 8; e++)
            {
                float a0 = (e + 0.5f) * Mathf.PI / 4f, a1 = (e + 1.5f) * Mathf.PI / 4f;
                var p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * R; var p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * R;
                var d = (p1 - p0).normalized; p0 += d * gap; p1 -= d * gap;
                var nm = Vector3.Cross(d, Vector3.up).normalized; if (Vector3.Dot(nm, p0 + p1) < 0) nm = -nm;   // 朝外
                int b = pv.Count;
                bool outwardOk = Vector3.Dot(Vector3.Cross(Vector3.up, d), nm) > 0;
                for (int i = 0; i <= Len; i++)
                {
                    float t = i / (float)Len, s = Pillow(t);
                    var c = Vector3.Lerp(p0, p1, t) + Vector3.up * cy;
                    for (int k = 0; k < Ring; k++)
                    {
                        var q = Prof(k * Mathf.PI * 2f / Ring, hw * s, hh * (0.94f + 0.06f * s));
                        pv.Add(c + nm * q.x + Vector3.up * q.y); puv.Add(new Vector2(t * 4f, k / (float)Ring));
                    }
                }
                for (int i = 0; i < Len; i++)
                    for (int k = 0; k < Ring; k++)
                    {
                        int a = b + i * Ring + k, a2 = b + i * Ring + (k + 1) % Ring, c0 = a + Ring, c2 = a2 + Ring;
                        // 正面必须朝外：截面在外侧的切线是“上”，沿边方向是 d，up × d 要和外法线 nm 同向；反了就换绕序
                        if (outwardOk) { pt.Add(a); pt.Add(a2); pt.Add(c0); pt.Add(a2); pt.Add(c2); pt.Add(c0); }
                        else { pt.Add(a); pt.Add(c0); pt.Add(a2); pt.Add(a2); pt.Add(c0); pt.Add(c2); }
                    }
                // 两头封口（藏在立柱里，但从缝里看得到时不是空的）
                foreach (int end in new[] { 0, Len })
                {
                    int center = pv.Count; pv.Add(Vector3.Lerp(p0, p1, end / (float)Len) + Vector3.up * cy); puv.Add(Vector2.zero);
                    for (int k = 0; k < Ring; k++) { int a = b + end * Ring + k, a2 = b + end * Ring + (k + 1) % Ring; pt.Add(center); pt.Add(a); pt.Add(a2); pt.Add(center); pt.Add(a2); pt.Add(a); }   // 封口两面都画
                }
                // 压线包边：截面左上、右上两条细管
                if (piping)
                    foreach (float ang in new[] { Mathf.PI * 0.27f, Mathf.PI * 0.73f })
                        for (int i = 0; i < Len; i++)
                        {
                            Vector3 P(int ii) { float t = ii / (float)Len, s = Pillow(t); var q = Prof(ang, hw * s, hh * (0.94f + 0.06f * s)); return Vector3.Lerp(p0, p1, t) + Vector3.up * cy + nm * q.x + Vector3.up * q.y; }
                            trim.Cyl(P(i), P(i + 1), 0.00055f, 6, false);
                        }
                // 绑带：每块软包 strapsPerPad 道，比软包略大一圈的扁环 + 外侧一个铜扣
                for (int pad = 0; pad < 2; pad++)
                    for (int sIdx = 0; sIdx < strapsPerPad; sIdx++)
                    {
                        float t = (pad + (sIdx + 1f) / (strapsPerPad + 1f)) / 2f, s = Pillow(t) * 1.03f;
                        var c = Vector3.Lerp(p0, p1, t) + Vector3.up * cy;
                        var pts = new List<Vector3>();
                        for (int k = 0; k <= Ring; k++) { var q = Prof(k * Mathf.PI * 2f / Ring, hw * s, hh * s); pts.Add(c + nm * q.x + Vector3.up * q.y); }
                        for (int k = 0; k < Ring; k++) strap.Box((pts[k] + pts[k + 1]) / 2f, new Vector3(0.0035f, 0.0006f, (pts[k + 1] - pts[k]).magnitude + 0.0002f), Quaternion.LookRotation(pts[k + 1] - pts[k], (pts[k] + pts[k + 1]) / 2f - c));
                        trim.Box(c + nm * (hw * s + 0.0004f) + Vector3.down * hh * 0.25f, new Vector3(0.0045f, 0.0035f, 0.0009f), Quaternion.LookRotation(nm));
                    }
                if (logos) for (int pad = 0; pad < 2; pad++) { float t = (pad + 0.5f) / 2f; o.logos.Add((Vector3.Lerp(p0, p1, t) + Vector3.up * cy + nm * (hw * Pillow(t) + 0.0004f), nm)); }
            }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(pv); m.SetUVs(0, puv); m.SetTriangles(pt, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            o.pad = m; o.trim = trim.Empty ? null : trim.Mesh(); o.strap = strap.Mesh();
            return o;
        }

        /// <summary>立柱软包：中间微鼓的圆柱（20 段），从 y0 到 y1；三道绑带。</summary>
        public static void Post(SBGeo pad, SBGeo strap, Vector3 c, float r, float y0, float y1)
        {
            var prof = new List<Vector2>();
            for (int i = 0; i <= 12; i++) { float t = i / 12f; prof.Add(new Vector2(r * (0.93f + 0.07f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.5f)), Mathf.Lerp(y0, y1, t))); }
            pad.Lathe(c, prof, 20, 0, true, false, 3f);
            foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
            {
                float y = Mathf.Lerp(y0, y1, t), rr = r * (0.93f + 0.07f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.5f)) + 0.0006f;
                strap.Lathe(c, new[] { new Vector2(rr, y - 0.0018f), new Vector2(rr, y + 0.0018f) }, 20, 0, false, false);
            }
        }
    }
}
