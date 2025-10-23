#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public sealed class SceneSwitcherWindow : EditorWindow
{
    [MenuItem("Tools/Scene Switcher %#s")] // Ctrl/Cmd + Shift + S
    private static void OpenWindow()
    {
        var window = GetWindow<SceneSwitcherWindow>("Scene Switcher");
        window.minSize = new Vector2(280, 200);
        window.Show();
    }

    private Vector2 _scroll;

    private void OnGUI()
    {
        GUILayout.Space(6);
        GUILayout.Label("Scenes in Build Settings", EditorStyles.boldLabel);
        GUILayout.Space(4);

        var scenes = EditorBuildSettings.scenes;
        if (scenes == null || scenes.Length == 0)
        {
            EditorGUILayout.HelpBox("No scenes found in Build Settings!", MessageType.Info);
            return;
        }

        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (var scene in scenes)
        {
            if (!scene.enabled) continue;

            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scene.path);

            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(sceneName, GUILayout.Width(160));

            if (GUILayout.Button("Open", GUILayout.Height(22)))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(scene.path);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }
}
#endif
