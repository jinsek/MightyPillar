using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using MightyPillar;

internal class MyDisplaySlice
{
    public Vector3[] verts;
}

internal class MyDisplayCube
{
    public Vector3 center;
    public Vector3 size;
}


public enum MPDataDrawMode
{
    None,
    All,
    Floor,
    Blocks,
}

[RequireComponent(typeof(MPDataCreator))]
public class MPDataDisplayer : MonoBehaviour
{
    public MPDataDrawMode Draw = MPDataDrawMode.None;
    private List<MyDisplaySlice> mSlices = new List<MyDisplaySlice>();
    private List<MyDisplayCube> mCubes = new List<MyDisplayCube>();
    public void OnCreatorRegenData()
    {
        mSlices.Clear();
        mCubes.Clear();
    }
    public void EditorRefreshData()
    {
        MPDataCreator creator = gameObject.GetComponent<MPDataCreator>();
        if (creator == null)
            return;
        string path = string.Format("{0}/MightyPillar/Resources/{1}.bytes", Application.dataPath, creator.DataName);
        PillarData data = MPFileUtil.LoadData(path, creator.DataName);
        //create display data
        mSlices.Clear();
        mCubes.Clear();
        Handles.color = Color.green;
        List<DisplaySlice> lSlices = new List<DisplaySlice>();
        Vector3 startPos = new Vector3(data.setting.center[0], data.setting.center[1], data.setting.center[2]);
        startPos.x -= data.setting.maxX * data.setting.sliceSize[0] * 0.5f;
        startPos.y -= (data.setting.heightValRange[1] - data.setting.heightValRange[0]) * 0.5f;
        startPos.z -= data.setting.maxZ * data.setting.sliceSize[1] * 0.5f;
        for (int x = 0; x < data.setting.maxX; ++x)
        {
            for (int z = 0; z < data.setting.maxZ; ++z)
            {
                data.GetDisplaySlice(startPos.x, startPos.z, x, z, lSlices);
            }
        }
        for (int s = 0; s < lSlices.Count; ++s)
        {
            DisplaySlice slice = lSlices[s];
            if (slice.flag > 0 && s + 1 < lSlices.Count)
            {
                MyDisplayCube cube = new MyDisplayCube();
                Vector3 c0 = slice.min[0] * Vector3.right + slice.min[1] * Vector3.forward + slice.height * Vector3.up;
                DisplaySlice next = lSlices[s + 1];
                Vector3 c1 = next.max[0] * Vector3.right + next.max[1] * Vector3.forward + next.height * Vector3.up;
                cube.center = 0.5f * (c0 + c1);
                cube.size = c1 - c0;
                mCubes.Add(cube);
            }
            else
            {
                MyDisplaySlice myslice = new MyDisplaySlice();
                myslice.verts = new Vector3[]
                {
                    slice.min[0] * Vector3.right + slice.min[1] * Vector3.forward + slice.height * Vector3.up,
                    slice.min[0] * Vector3.right + slice.max[1] * Vector3.forward + slice.height * Vector3.up,
                    slice.max[0] * Vector3.right + slice.max[1] * Vector3.forward + slice.height * Vector3.up,
                    slice.max[0] * Vector3.right + slice.min[1] * Vector3.forward + slice.height * Vector3.up,
                };
                mSlices.Add(myslice);
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (mSlices.Count == 0 || Draw == MPDataDrawMode.None)
            return;
        Color redRect = new Color(1, 0, 0, 0.3f);
        Color greenRect = new Color(0, 1, 0, 0.3f);
        if (Draw == MPDataDrawMode.All || Draw == MPDataDrawMode.Floor)
        {
            foreach(var slice in mSlices)
                Handles.DrawSolidRectangleWithOutline(slice.verts, greenRect, Color.gray);
        }
        Handles.color = redRect;
        if (Draw == MPDataDrawMode.All || Draw == MPDataDrawMode.Blocks)
        {
            foreach (var cube in mCubes)
                Handles.DrawWireCube(cube.center, cube.size);
        }
    }
#endif
}
