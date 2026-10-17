using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Inn.EditorTools
{
    /// <summary>URP 管线、DX11、总入口 Run All。可重复执行。</summary>
    public static class InnSetup
    {
        public const string SettingsDir = "Assets/Game/Settings";

        [MenuItem("InnFactory/Setup/Run All")]
        public static void RunAll()
        {
            Pipeline();
            InnDataFactory.Build();
            InnSceneFactory.Build();
            Debug.Log("[InnSetup] Run All 完成");
        }

        [MenuItem("InnFactory/Setup/1. Render Pipeline + DX11")]
        public static void Pipeline()
        {
            EnsureFolder(SettingsDir);
            string assetPath = SettingsDir + "/URP.asset";
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                var create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
                var rendererType = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.RendererType");
                var data = (ScriptableRendererData)create.Invoke(null, new object[] { SettingsDir + "/URP_Renderer.asset", System.Enum.Parse(rendererType, "UniversalRenderer"), false, "Renderer" });
                urp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 60;
            urp.shadowCascadeCount = 1;
            var so = new SerializedObject(urp);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_MainLightShadowmapResolution", 2048);
            SetProp(so, "m_AdditionalLightsRenderingMode", 1);
            SetProp(so, "m_AdditionalLightsPerObjectLimit", 4);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "InnFactory";
            PlayerSettings.companyName = "InnFactory";
            // 窗口模式、可缩放；切到后台继续运行（联机时主机切出去别人不能卡住）
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();
            Debug.Log("[InnSetup] URP + DX11 ready");
        }

        public const string PlayerPath = "Builds/InnFactory/InnFactory.exe";

        /// <summary>打包 Windows 版（联机测试的客户端和给朋友的压缩包都用它）</summary>
        [MenuItem("InnFactory/Build/Windows Player")]
        public static bool BuildPlayer()
        {
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { InnSceneFactory.ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[Build] {report.summary.result} {report.summary.totalSize / 1024 / 1024} MB → {PlayerPath}");
            if (!ok) Debug.LogError("[Build] 打包失败");
            return ok;
        }

        static void SetProp(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[InnSetup] URP property not found: " + name); return; }
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i; break;
            }
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
