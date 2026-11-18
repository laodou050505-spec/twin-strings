using UnityEditor;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>菜单“双偶/模型展台”：自动进入播放、在八角笼旁边的空地摆出所有模型，Scene 窗口对准展台（可以随便转着看）。</summary>
    [InitializeOnLoad]
    public static class SBShowcaseMenu
    {
        const string Key = "sb_showcase_pending";
        static SBShowcaseMenu() { EditorApplication.playModeStateChanged += OnPlay; }

        [MenuItem("双偶/模型展台")]
        static void Open()
        {
            if (EditorApplication.isPlaying) { BuildNow(); return; }
            EditorPrefs.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        static void OnPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode || !EditorPrefs.GetBool(Key, false)) return;
            EditorPrefs.SetBool(Key, false);
            EditorApplication.delayCall += BuildNow;
        }

        static void BuildNow()
        {
            var lit = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Brawl/Materials/Lit.mat");
            SBShowcase.Build(lit);
            var sv = EditorWindow.GetWindow<SceneView>(); sv.Show(); sv.Focus();
            sv.pivot = SBShowcase.Origin + new Vector3(0.85f, 0.05f, 0.42f); sv.rotation = Quaternion.Euler(38, 0, 0); sv.size = 1.15f; sv.Repaint();
        }
    }
}
