using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MPDataCreator))]
public class MPDataCreatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MPDataCreator comp = (MPDataCreator)target;
        // Update the serializedProperty - always do this in the beginning of OnInspectorGUI.
        serializedObject.Update();
        base.OnInspectorGUI();
        int subdivision = EditorGUILayout.IntField("Subdivision(Up to 6)", comp.Subdivision);
        if (comp.Subdivision != subdivision)
        {
            comp.Subdivision = Mathf.Min(6, subdivision);
        }
        float thickness = EditorGUILayout.FloatField("Y Slice Thickness", comp.SliceThickness);
        if (comp.SliceThickness != thickness)
        {
            comp.SliceThickness = Mathf.Max(MightyPillar.SliceAccessor.minSliceThickness, thickness);
        }
        MPDataCreator dc = (MPDataCreator)target;
        if (GUILayout.Button("CreateData"))
        {
            if (dc.DataName == "")
            {
                Debug.LogError("data should have a name");
                return;
            }
            dc.EditorCreateDataBegin();
            for(int i= 0; i<int.MaxValue; ++i)
            {
                dc.EditorCreateDataUpdate();
                EditorUtility.DisplayProgressBar("creating data", "scaning volumn", dc.EditorCreateDataProgress);
                if (dc.IsEditorCreateDataDone)
                    break;
            }
            EditorUtility.ClearProgressBar();
            dc.EditorCreateDataEnd();
            AssetDatabase.Refresh();
        }
        // Apply changes to the serializedProperty - always do this in the end of OnInspectorGUI.
        serializedObject.ApplyModifiedProperties();
    }
}
