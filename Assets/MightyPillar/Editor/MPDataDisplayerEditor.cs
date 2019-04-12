using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MPDataDisplayer))]
public class MPDataDisplayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Update the serializedProperty - always do this in the beginning of OnInspectorGUI.
        serializedObject.Update();
        base.OnInspectorGUI();
        MPDataDisplayer dc = (MPDataDisplayer)target;
        if (GUILayout.Button("Refresh Data"))
        {
            dc.EditorRefreshData();
        }
        // Apply changes to the serializedProperty - always do this in the end of OnInspectorGUI.
        serializedObject.ApplyModifiedProperties();
    }
}
