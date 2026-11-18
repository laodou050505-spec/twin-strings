using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>程序建模的拳套（代替生成模型：生成的那只又糊又塌，护腕包不住前臂）。
    /// 坐标：护腕朝上（+y 接前臂），拳头朝下（-y 是击打面），手背朝前（+z），掌心朝后（-z）。
    /// 结构：
    ///   拳头主体 = 两个叠在一起的超椭球（上面是手掌，下面前倾一点的是加厚的指节垫）；
    ///   大拇指 = 一个细长椭球，贴在掌侧、靠身体中线那一边，大半埋进主体；
    ///   护腕 = 车削的筒，口上一圈往里卷的翻边，比前臂粗，前臂插进去；
    ///   细节 = 一圈侧缝滚边、护腕上的奶白色带 + 铜牌、掌侧三组交叉系带。</summary>
    public static class SBGlove
    {
        /// <param name="gs">拳套尺寸单位（整只约 1.45 gs 高）</param>
        /// <param name="armR">前臂半径：护腕口要比它大，才包得住</param>
        /// <param name="sx">+1 右手，-1 左手（大拇指在靠身体中线的那一侧）</param>
        public static Transform Build(Transform parent, float gs, float armR, int sx, Color col, Material lit)
        {
            var root = new GameObject("拳套").transform; root.SetParent(parent, false);
            var leather = new SBGeo(); var trim = new SBGeo(); var band = new SBGeo(); var gold = new SBGeo(); var lace = new SBGeo();

            // —— 拳头主体 ——
            var fc = new Vector3(0, -0.82f, 0.03f) * gs; var fh = new Vector3(0.40f, 0.50f, 0.43f) * gs;
            leather.Blob(fc, fh, Quaternion.identity, 2.5f, 20, 30);   // 一整块，不拼接（两块互相穿插会在交线上出锯齿）

            // 主体表面上的点（超椭球方程反解 z）：缝线、软垫都贴着它放
            Vector3 Surf(float x, float y, float side)
            {
                float u = Mathf.Pow(Mathf.Abs(x / fh.x), 2.5f) + Mathf.Pow(Mathf.Abs((y - fc.y) / fh.y), 2.5f);
                return new Vector3(x, y, fc.z + side * fh.z * Mathf.Pow(Mathf.Max(0f, 1f - u), 1f / 2.5f));
            }
            // —— 掌侧：弯起来的四根手指是一条横着的软垫（指尖藏在里面），上沿一道缝线 ——
            leather.Blob(new Vector3(0, -1.06f, -0.27f) * gs, new Vector3(0.33f, 0.17f, 0.15f) * gs, Quaternion.Euler(-18f, 0, 0), 2.3f, 12, 20);
            var fs = new List<Vector3>();
            for (int k = 0; k <= 12; k++) { float x = Mathf.Lerp(-0.3f, 0.3f, k / 12f) * gs; var q = Surf(x, -0.9f * gs, -1f); q.z -= 0.012f * gs; fs.Add(q); }
            trim.Tube(fs, 0.016f * gs, 5);
            // —— 手背：指节加厚垫的一圈缝线（U 形，开口朝护腕）——
            var ks = new List<Vector3>();
            for (int k = 0; k <= 16; k++)
            {
                float t = Mathf.Lerp(-1f, 1f, k / 16f);
                float x = t * 0.3f * gs, y = (-0.72f - 0.38f * Mathf.Cos(t * Mathf.PI * 0.5f)) * gs;
                ks.Add(Surf(x, y, 1f) + Vector3.forward * 0.004f * gs);
            }
            trim.Tube(ks, 0.016f * gs, 5);

            // —— 大拇指：掌侧、靠中线，尖朝下略往里扣；拇指和拳身之间一条皮桥连着，拇指尖用一根小皮带拴在拳身上（真拳套的“连体拇指”）——
            var tc = new Vector3(-sx * 0.30f, -0.66f, -0.24f) * gs;
            leather.Blob(tc, new Vector3(0.15f, 0.30f, 0.16f) * gs, Quaternion.Euler(-14f, 0, sx * 16f), 2f, 12, 18);
            leather.Blob(new Vector3(-sx * 0.2f, -0.64f, -0.27f) * gs, new Vector3(0.12f, 0.22f, 0.1f) * gs, Quaternion.Euler(-10f, 0, sx * 10f), 2.2f, 10, 14);
            trim.Tube(new List<Vector3> { new Vector3(-sx * 0.36f, -0.9f, -0.3f) * gs, new Vector3(-sx * 0.3f, -0.98f, -0.36f) * gs, new Vector3(-sx * 0.18f, -1.0f, -0.4f) * gs }, 0.022f * gs, 5);

            // —— 护腕：椭圆筒，口上往里卷一圈翻边（开口，前臂插在里面）——
            float cr = Mathf.Max(0.32f * gs, armR * 1.15f);
            var prof = new[] {
                new Vector2(cr * 0.96f, -0.46f * gs), new Vector2(cr, -0.32f * gs), new Vector2(cr * 1.03f, -0.02f * gs), new Vector2(cr * 1.07f, 0.10f * gs),
                new Vector2(cr * 1.11f, 0.155f * gs), new Vector2(cr * 1.05f, 0.195f * gs), new Vector2(cr * 0.92f, 0.19f * gs), new Vector2(cr * 0.87f, 0.12f * gs), new Vector2(cr * 0.86f, -0.05f * gs) };
            leather.M = Matrix4x4.Scale(new Vector3(1f, 1f, 0.92f));
            leather.Lathe(Vector3.zero, prof, 26, 0f, false, false);
            leather.M = Matrix4x4.identity;
            // 色带 + 手背一侧的铜牌
            band.M = Matrix4x4.Scale(new Vector3(1f, 1f, 0.92f));
            band.Lathe(Vector3.zero, new[] { new Vector2(cr * 1.035f, -0.25f * gs), new Vector2(cr * 1.06f, -0.235f * gs), new Vector2(cr * 1.065f, -0.10f * gs), new Vector2(cr * 1.045f, -0.085f * gs) }, 26, 0f, false, false);
            band.M = Matrix4x4.identity;
            gold.Blob(new Vector3(0, -0.168f * gs, cr * 1.065f * 0.92f), new Vector3(0.13f, 0.075f, 0.025f) * gs, Quaternion.identity, 3f, 8, 14);
            // 魔术贴绑带：斜着缠护腕一圈（深色），手背一侧压一块贴片
            var ring = new List<Vector3>(); var rot = Quaternion.Euler(sx * 10f, 0, 12f * sx);
            for (int k = 0; k < 24; k++) { float a2 = k / 24f * Mathf.PI * 2f; ring.Add(new Vector3(0, -0.36f * gs, 0) + rot * new Vector3(Mathf.Sin(a2) * cr * 1.025f, 0, Mathf.Cos(a2) * cr * 1.025f * 0.92f)); }
            trim.Tube(ring, 0.04f * gs, 5, true);
            // 掌侧交叉系带（贴着护腕的弧面）
            float Zs(float x, float rr) => -Mathf.Sqrt(Mathf.Max(0f, rr * rr - x * x)) * 0.92f;
            for (int c = 0; c < 2; c++)
            {
                float y0 = (-0.075f - c * 0.085f) * gs, y1 = y0 - 0.07f * gs, xw = 0.075f * gs, rr = cr * 1.07f;
                foreach (var sgn in new[] { -1f, 1f })
                {
                    var pts = new List<Vector3>();
                    for (int k = 0; k <= 4; k++) { float t = k / 4f, x = Mathf.Lerp(-xw, xw, t) * sgn; pts.Add(new Vector3(x, Mathf.Lerp(y0, y1, t), Zs(x, rr) - 0.012f * gs)); }
                    lace.Tube(pts, 0.016f * gs, 5);
                }
            }

            // —— 侧缝滚边：沿主体侧面轮廓（z ≈ 0 的那一圈），从护腕一侧绕过拳头底下到另一侧 ——
            var seam = new List<Vector3>(); float e = 2f / 2.5f;
            for (int k = 0; k <= 30; k++)
            {
                float t = Mathf.Lerp(Mathf.PI / 2f + 0.55f, Mathf.PI * 2.5f - 0.55f, k / 30f);
                float c0 = Mathf.Cos(t), s0 = Mathf.Sin(t);
                var p = new Vector3(Mathf.Sign(c0) * Mathf.Pow(Mathf.Abs(c0), e) * fh.x, Mathf.Sign(s0) * Mathf.Pow(Mathf.Abs(s0), e) * fh.y, 0) * 1.004f;
                seam.Add(fc + p);
            }
            trim.Tube(seam, 0.02f * gs, 6);

            var ml = new Material(lit) { color = col }; ml.SetFloat("_Smoothness", 0.52f); ml.SetFloat("_Metallic", 0f);
            var mt = new Material(lit) { color = col * 0.55f }; mt.SetFloat("_Smoothness", 0.35f);
            var mb = new Material(lit) { color = new Color(.95f, .9f, .78f) }; mb.SetFloat("_Smoothness", 0.35f);
            var mg = new Material(lit) { color = new Color(.85f, .65f, .3f) }; mg.SetFloat("_Metallic", 0.7f); mg.SetFloat("_Smoothness", 0.55f);
            foreach (var (g, m, n) in new[] { (leather, ml, "皮"), (trim, mt, "滚边"), (band, mb, "色带"), (gold, mg, "铜牌"), (lace, mb, "系带") })
            {
                var o = g.Make(root, n, m);
                o.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }
    }
}
