#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(WfcGenerator))]
public class WfcGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WfcGenerator gen = (WfcGenerator)target;

        GUILayout.Space(8);
        GUILayout.Label("WFC Controls", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate (Editor Sync)"))
        {
            if (!EditorApplication.isPlaying)
                gen.GenerateSync();

        }

        if (GUILayout.Button("Generate Animated (Editor)"))
        {
            if (!EditorApplication.isPlaying)
                gen.StartEditorAnimatedGeneration();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop Animated"))
        {
            gen.StopEditorAnimatedGeneration();
        }
        if (GUILayout.Button("Clear Area"))
        {
            gen.ClearTiles();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUI.enabled = false;
        EditorGUILayout.Toggle("Is Generating", gen.isGenerating);
        EditorGUILayout.IntField("Current Attempt", gen.currentAttempt);
        GUI.enabled = true;
    }
}
#endif
