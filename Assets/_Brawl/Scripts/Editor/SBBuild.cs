using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StringBrawl.EditorTools
{
    /// <summary>搭场景 + 打包。经验照搬琴村（TKBuild）：
    /// - PXR_ProjectSetting 要存成资产并打开手部 / 透视，否则真机读不到手、清单没有权限；
    /// - 切平台和打包不能在同一次调用里做（会漏掉 PICO 清单注入，头显里变 2D 窗口）——批处理启动时就用 -buildTarget Android；
    /// - 打包前清掉 Gradle 旧资源目录；IL2CPP + ARM64、minSdk 29、Activity 入口。
    /// 命令行：Unity -batchmode -buildTarget Android -projectPath … -executeMethod StringBrawl.EditorTools.SBBuild.Batch</summary>
    public static class SBBuild
    {
        public const string Package = "com.toybox.stringbrawl";
        public const string ScenePath = "Assets/_Brawl/Scenes/Brawl.unity";

        [MenuItem("StringBrawl/0 PICO 工程设置")]
        public static string EnsurePico()
        {
            const string path = "Assets/Resources/PXR_ProjectSetting.asset";
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(path);
            if (!c)
            {
                c = ScriptableObject.CreateInstance<PXR_ProjectSetting>();
                AssetDatabase.CreateAsset(c, path);
            }
            c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            c.portalInited = true;
            c.handTracking = true;
            c.eyeTracking = true;        // 只给注视点渲染省性能用，玩法不用眼动
            c.videoSeeThrough = true;
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return $"hand={c.handTracking} vst={c.videoSeeThrough}";
        }

        [MenuItem("StringBrawl/1 搭擂台手感测试场景")]
        public static string BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("灯").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.color = new Color(1f, .95f, .88f);
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            // 环境光：上暖、中性、下冷的三色渐变（比单色平光更有体积感）
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.5f, .45f, .4f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .3f, .3f);
            RenderSettings.ambientGroundColor = new Color(.20f, .21f, .26f);
            RenderSettings.sun = sun;

            var origin = new GameObject("XR Origin").transform;
            var camGO = new GameObject("XR Camera");
            camGO.transform.SetParent(origin, false);
            camGO.transform.localPosition = new Vector3(0, 1.4f, 0);
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.22f, 0f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 100f; cam.fieldOfView = 90f;
            var cd = camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false;
            camGO.AddComponent<AudioListener>();
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadPos", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadRot", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            origin.gameObject.AddComponent<PXR_Manager>();
            origin.gameObject.AddComponent<SBMR>().cam = cam;
            var hands = origin.gameObject.AddComponent<SBHands>();
            hands.trackingOrigin = origin;

            var match = new GameObject("擂台").AddComponent<SBMatch>();
            match.hands = hands; match.head = camGO.transform;
            match.litTemplate = MatAsset("Lit", "Universal Render Pipeline/Lit");
            match.unlitTemplate = MatAsset("Unlit", "Universal Render Pipeline/Unlit");
            match.glowMat = MatAsset("Glow", "StringBrawl/Glow");
            match.fenceMat = MatAsset("Fence", "StringBrawl/Fence");
            match.crowdMat = MatAsset("CrowdCut", "StringBrawl/CrowdCut");
            match.textMat = MatAsset("Text", "StringBrawl/Text");
            match.shadowMat = MatAsset("Shadow", "StringBrawl/Shadow");
            match.paperMat = MatAsset("Paper", "StringBrawl/Paper");
            match.outlineMat = MatAsset("Outline", "StringBrawl/Outline");   // 选人描边
            match.ledMat = MatAsset("Led", "StringBrawl/Led");   // 场景引用它，打包才会带上滚动屏的着色器

            Directory.CreateDirectory("Assets/_Brawl/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return "场景已保存 " + ScenePath;
        }

        /// <summary>材质资产（场景引用它，打包才会带上对应的着色器）。</summary>
        static Material MatAsset(string name, string shader)
        {
            Directory.CreateDirectory("Assets/_Brawl/Materials");
            string path = $"Assets/_Brawl/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!sh) throw new System.Exception("找不到着色器 " + shader);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) { m.shader = sh; EditorUtility.SetDirty(m); }
            return m;
        }

        /// <summary>启动画面（11-17 公司署名）：去掉 Made with Unity，换成排版一样的 MADE WITH + TRIPO 标志（左边两行小号大写字 MADE / WITH，右边官方标志，横排）。
        /// 仍然用 Unity 自带的启动画面（位置、淡入淡出、深色背景都是同一套），只是关掉 Unity 标志、换成我们这一张（Brand/made_with_tripo.png，透明底，和《霓虹巨兽》用的是同一张图）。
        /// 头显里 Unity 不画这个“标志列表”，只认 Virtual Reality Splash Image 这一张贴图 → 再把头显专用的那张（不透明深色底、内容居中）填进去。
        /// 每次打包都会调一遍；图不在的时候什么都不改。</summary>
        public static string ApplySplash()
        {
            const string path = "Assets/_Brawl/Brand/made_with_tripo.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[SB] 没找到启动画面图 " + path + "，启动画面保持原样"); return "缺 " + path; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sp) { Debug.LogWarning("[SB] 启动画面图导入失败 " + path); return "导入失败 " + path; }
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            const string vrPath = "Assets/_Brawl/Brand/made_with_tripo_vr.png";
            var vi = AssetImporter.GetAtPath(vrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[SB] 没找到头显启动图 " + vrPath); return "缺 " + vrPath; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(vrPath);
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (!vt || all == null || all.Length == 0) { Debug.LogWarning("[SB] 头显启动图没设上"); return "头显启动图没设上"; }
            var so = new SerializedObject(all[0]); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop == null) { Debug.LogWarning("[SB] 这个版本的 Unity 没有 m_VirtualRealitySplashScreen"); return "没有 m_VirtualRealitySplashScreen"; }
            if (prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
            return "ok";
        }

        [MenuItem("StringBrawl/打包 APK（PICO）")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return "当前平台不是 Android：请用 -buildTarget Android 启动";
            EnsurePico();
            ApplySplash();   // 启动画面：MADE WITH TRIPO（普通启动画面 + 头显专用图两处都填）
            // 批处理里 XR 加载器有时没登记上，PICO 就不往清单里写 VR 标记（装上去是 2D 窗口）：这里显式确认一遍
            var gs = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (gs == null || gs.AssignedSettings == null) return "找不到 Android 的 XR 设置";
            if (!gs.AssignedSettings.activeLoaders.Any(l => l is PXR_Loader))
                UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(gs.AssignedSettings, typeof(PXR_Loader).FullName, BuildTargetGroup.Android);
            Debug.Log("[SB] Android XR 加载器：" + string.Join(",", gs.AssignedSettings.activeLoaders.Select(l => l.GetType().Name)) + "  initOnStart=" + gs.InitManagerOnStart);
            BuildScene();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Package);
            PlayerSettings.productName = "双偶";
            // 应用图标：BB2（木偶师的手提着木偶拳手，聚光灯 + 八角台）
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Brawl/Icon/app_icon.png");
            if (icon)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round })
                {
                    var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                    foreach (var pi in icons) pi.SetTexture(icon);
                    PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
                }
                Debug.Log("[SB] 图标已设置");
            }
            PlayerSettings.companyName = "toybox";
            PlayerSettings.bundleVersion = "0.0." + DateTime.Now.ToString("MMdd");
            PlayerSettings.Android.bundleVersionCode = int.Parse(DateTime.Now.ToString("MMddHH"));
            var gradleAssets = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets"));
            if (Directory.Exists(gradleAssets)) Directory.Delete(gradleAssets, true);
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
            var apk = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/StringBrawl_{DateTime.Now:MMdd_HHmm}.apk"));
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = apk,
                target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None,
            });
            var s = rep.summary;
            string msg = $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 大小 {s.totalSize / 1048576f:0.0} MB 错误 {s.totalErrors} → {apk}";
            File.AppendAllText("/tmp/sb_build.txt", DateTime.Now.ToString("HH:mm:ss ") + msg + "\n");
            Debug.Log("[SB] 打包：" + msg);
            return msg;
        }

        /// <summary>批处理入口：设置 → 搭场景 → 打包 → 退出（失败返回 1）。</summary>
        public static void Batch()
        {
            string msg;
            try { msg = BuildApk(); }
            catch (Exception e) { msg = "异常 " + e; }
            File.AppendAllText("/tmp/sb_build.txt", DateTime.Now.ToString("HH:mm:ss ") + "Batch: " + msg + "\n");
            EditorApplication.Exit(msg.StartsWith("Succeeded") ? 0 : 1);
        }
    }

#if UNITY_ANDROID
    /// <summary>清单守卫：PICO 清单注入没生效（缺 VR 标记 / 手部权限）就让打包失败，否则头显里是 2D 窗口。</summary>
    internal class SBManifestGuard : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var m = File.ReadAllText(Path.Combine(path, "src/main/AndroidManifest.xml"));
            foreach (var need in new[] { "pvr.app.type", "com.picovr.permission.HAND_TRACKING" })
                if (!m.Contains(need))
                    throw new BuildFailedException($"[SB] AndroidManifest 缺少 {need}：PICO 清单注入没生效。");
        }
    }
#endif
}
