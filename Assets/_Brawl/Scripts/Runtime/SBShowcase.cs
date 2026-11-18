using UnityEngine;

namespace StringBrawl
{
    /// <summary>模型展台（只在编辑器里用）：第一排 6 个角色 + 木人桩，按游戏里的真实尺寸站着做待机动作；
    /// 后面几排是 Resources/Parts 里所有生成部件，各自摆正、挂名字牌。放在离八角笼几米外的空地上，不影响游戏。</summary>
    public static class SBShowcase
    {
        public static readonly Vector3 Origin = new(3f, 0f, 3f);

        public static GameObject Build(Material lit)
        {
            var old = GameObject.Find("模型展台"); if (old) Object.Destroy(old);
            var root = new GameObject("模型展台"); root.transform.position = Origin;
            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(plinth.GetComponent<Collider>());
            plinth.transform.SetParent(root.transform, false); plinth.transform.localPosition = new Vector3(0.85f, -0.01f, 0.45f); plinth.transform.localScale = new Vector3(2.0f, 0.02f, 1.2f);
            plinth.GetComponent<Renderer>().material = new Material(lit) { color = new Color(.16f, .15f, .15f) };

            // 已经被替换、游戏里不再用的旧件（程序建模或别的部件代替了）
            var unused = new System.Collections.Generic.HashSet<string> { "glove", "kite_wings", "weight_torso", "weight_head", "tumbler_torso", "tumbler_head", "lantern", "spotlight", "cage_post", "corner_stool", "bucket", "ring_steps" };
            void Label(string s, Vector3 p, bool dim = false)
            {
                var g = new GameObject("名牌_" + s); g.transform.SetParent(root.transform, false); g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(50, 0, 0);
                var tm = g.AddComponent<TextMesh>(); tm.text = s; tm.characterSize = 0.0028f; tm.fontSize = 60; tm.anchor = TextAnchor.MiddleCenter; tm.color = dim ? new Color(.55f, .55f, .55f) : new Color(1f, .85f, .5f);
            }

            var kits = new System.Collections.Generic.List<SBKit>(SBKit.All) { SBKit.Dummy };
            for (int k = 0; k < kits.Count; k++)
            {
                var kit = kits[k];
                var g = new GameObject("展_" + kit.id); g.transform.SetParent(root.transform, false);
                var pos = Origin + new Vector3(0.12f + k * 0.26f, 0, 0);
                var q = g.AddComponent<SBPuppet>(); q.Init(kit, lit, 0.16f); q.Place(pos, 0f); q.floorY = Origin.y; q.target = pos;
                Label(kit.zh + "\n" + kit.en, new Vector3(0.12f + k * 0.26f, 0.006f, -0.08f));
            }
#if UNITY_EDITOR
            var dirs = System.IO.Directory.GetDirectories("Assets/_Brawl/Resources/Parts");
            System.Array.Sort(dirs); int n = 0;
            foreach (var d in dirs)
            {
                var name = System.IO.Path.GetFileName(d);
                int col = n % 11, row = n / 11;
                var lp = new Vector3(col * 0.17f, 0.06f, 0.3f + row * 0.24f);
                var t = SBParts.Make(name, root.transform, lp, Vector3.one * 0.1f, lit, Color.white, true);
                if (t) { var b = t.GetComponentInChildren<Renderer>().bounds; t.position += Vector3.up * (Origin.y - b.min.y); }
                bool old2 = unused.Contains(name); Label(old2 ? name + "\n（旧件·未用）" : name, new Vector3(lp.x, 0.004f, lp.z - 0.075f), old2);
                n++;
            }
#endif
            return root;
        }
    }
}
