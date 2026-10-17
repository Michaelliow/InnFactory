using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Inn.EditorTools
{
    /// <summary>生成 Scenes/Inn.unity：只有相机、灯光和 Inn 总控物体。地图和初始建筑在运行时按 GameDatabase.startLayout 生成。</summary>
    public static class InnSceneFactory
    {
        public const string ScenePath = "Assets/Game/Scenes/Inn.unity";

        [MenuItem("InnFactory/Setup/3. Build Scene")]
        public static void Build()
        {
            InnSetup.EnsureFolder("Assets/Game/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 120;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.82f);
            camGo.AddComponent<Inn.IsoCamera>().Snap(new Vector3(16, 0, 5), 6.5f);
            camGo.GetComponent<Inn.IsoCamera>().overrideFocus = null;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.transform.rotation = Quaternion.Euler(55, -30, 0);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.3f, 0.26f);
            RenderSettings.skybox = null;

            var inn = new GameObject("Inn");
            inn.AddComponent<Inn.InnGame>();
            inn.AddComponent<Inn.InnHUD>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[InnScene] saved " + ScenePath);
        }
    }
}
