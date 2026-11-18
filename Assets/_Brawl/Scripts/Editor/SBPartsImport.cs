using UnityEditor;

namespace StringBrawl.EditorTools
{
    /// <summary>生成部件（Resources/Parts）的导入规则：网格可读写（运行时要摆正、居中）、不导入 FBX 自带材质、不要动画；贴图开 mipmap、各向异性过滤。</summary>
    public class SBPartsImport : AssetPostprocessor
    {
        // 配音：必须“预加载音频数据”。不预加载的话，头显上第一次播某一句要现解码，声音晚出来甚至出不来（“有时不说话、只说一半”就是这个）
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Voice/")) return;
            var imp = (AudioImporter)assetImporter; imp.loadInBackground = false; imp.forceToMono = true;
            var ss = imp.defaultSampleSettings; ss.preloadAudioData = true; ss.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad; ss.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis; ss.quality = 0.8f;
            imp.defaultSampleSettings = ss;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Resources/Parts/")) return;
            var m = (ModelImporter)assetImporter;
            m.isReadable = true;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importAnimation = false; m.animationType = ModelImporterAnimationType.None;
            m.importCameras = false; m.importLights = false;
        }

        void OnPreprocessTexture()
        {
            if (assetPath.Contains("/Resources/Belt/"))
            {   // 金腰带的贴图（素材库里的雕花金材质）：normal.png 本身就是法线贴图，直接按法线导入
                var bt = (TextureImporter)assetImporter; bt.mipmapEnabled = true; bt.anisoLevel = 4; bt.wrapMode = UnityEngine.TextureWrapMode.Repeat;
                if (assetPath.EndsWith("/normal.png")) { bt.textureType = TextureImporterType.NormalMap; bt.convertToNormalmap = false; }
                else { bt.textureType = TextureImporterType.Default; bt.sRGBTexture = true; }
                return;
            }
            if (assetPath.EndsWith("/Resources/UI/tripo_logo.png"))
            {   // 公司标志（标题页的出品签）：不压缩（压缩会让标志的边发糊、颜色偏），带多级缩小图 + 各向异性，斜着看、离得远也清楚
                var lt = (TextureImporter)assetImporter; lt.textureType = TextureImporterType.Default; lt.sRGBTexture = true; lt.alphaSource = TextureImporterAlphaSource.None;
                lt.mipmapEnabled = true; lt.anisoLevel = 8; lt.wrapMode = UnityEngine.TextureWrapMode.Clamp; lt.npotScale = TextureImporterNPOTScale.None;
                lt.textureCompression = TextureImporterCompression.Uncompressed; lt.filterMode = UnityEngine.FilterMode.Trilinear;
                return;
            }
            if (!assetPath.Contains("/Resources/Parts/")) return;
            var t = (TextureImporter)assetImporter;
            t.mipmapEnabled = true; t.anisoLevel = 4;
            if (assetPath.EndsWith("/normal.png"))
            {
                // 亮度图 → 法线贴图：木纹、针脚、铆钉、描金会有一点真实的凹凸受光
                t.textureType = TextureImporterType.NormalMap;
                t.convertToNormalmap = true; t.heightmapScale = 0.06f; t.normalmapFilter = TextureImporterNormalFilter.Sobel;
            }
            else { t.textureType = TextureImporterType.Default; t.sRGBTexture = true; }
        }
    }
}
