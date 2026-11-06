using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace OuterRim.Client.Editor
{
    /// <summary>
    /// Builds the prototype's scenes from code, so they can be rebuilt from the terminal
    /// (unity command editor invoke_menu, or script run) and reviewed as a diff.
    /// </summary>
    public static class SceneBuilder
    {
        public const string BootScene = "Assets/Scenes/Boot.unity";
        public const string PanelSettingsPath = "Assets/UI/PanelSettings.asset";
        public const string ThemePath = "Assets/UI/DefaultTheme.tss";

        [MenuItem("Outer Rim/Build Boot Scene")]
        public static void BuildBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var cam = camera.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);

            var boot = new GameObject("Boot", typeof(UIDocument), typeof(BootView));
            boot.GetComponent<UIDocument>().panelSettings = PanelSettings();

            EditorSceneManager.SaveScene(scene, BootScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
            AssetDatabase.SaveAssets();
        }

        /// <summary>One panel for the whole screen UI (UI Toolkit, PLAN T0.4), scaled from 1920×1080 (docs/UI.md).</summary>
        private static PanelSettings PanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }
    }
}
