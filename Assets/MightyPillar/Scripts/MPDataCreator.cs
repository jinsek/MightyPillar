using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using MightyPillar;
using System.IO;


internal class CreateDataJob
{
    public int maxX { get; private set; }
    public int maxZ { get; private set; }
    public int subdivision { get; private set; }
    public float[] sliceSize { get; private set; }
    public QuadTreeBase[] QuadTrees { get; private set; }
    public float slopeErr { get; private set; }
    public float heightPerGrade { get; private set; }
    private MPUnityHeightScanner mScanner;
    private Vector3 vCheckTop = Vector3.one;
    private int curXIdx = 0;
    private int curZIdx = 0;
    public float[] heightValRange = new float[] { float.MaxValue, float.MinValue};
    public float[] center = new float[] { 0, 0, 0 };
    private int detailedSize = 1;
    public bool IsDone
    {
        get
        {
            return curXIdx >= maxX && curZIdx >= maxZ;
        }
    }
    public float progress
    {
        get
        {
            return (float)(curXIdx + curZIdx * maxX) / (float)(maxX * maxZ);
        }
    }
    public CreateDataJob(Bounds VolumnBound, int sub, Vector2 PillarSize, float thickness, float serr)
    {
        maxX = Mathf.CeilToInt(VolumnBound.size.x / PillarSize.x);
        maxZ = Mathf.CeilToInt(VolumnBound.size.z / PillarSize.y);
        subdivision = sub;
        center = new float[] { VolumnBound.center.x, VolumnBound.center.y, VolumnBound.center.z };
        heightValRange = new float[] { VolumnBound.center.y - 0.5f * VolumnBound.size.y,
            VolumnBound.center.y + 0.5f * VolumnBound.size.y};
        sliceSize = new float[] { PillarSize.x, PillarSize.y };
        vCheckTop = new Vector3(VolumnBound.center.x - VolumnBound.size.x / 2,
             VolumnBound.center.y + VolumnBound.size.y / 2 + 10f * thickness,
            VolumnBound.center.z - VolumnBound.size.z / 2);
        int sliceCount = Mathf.CeilToInt((heightValRange[1] - heightValRange[0]) / thickness);
        sliceCount = Mathf.Min(sliceCount, ushort.MaxValue);
        heightPerGrade = (heightValRange[1] - heightValRange[0]) / sliceCount; 
        QuadTrees = new QuadTreeBase[maxX * maxZ];
        slopeErr = serr;
        //
        detailedSize = 1 << subdivision;
        Vector3 checkHalfExtent = new Vector3(sliceSize[0] / 2, thickness, sliceSize[1] / 2);
        checkHalfExtent = 1f / detailedSize * checkHalfExtent;
        mScanner = new MPUnityHeightScanner(checkHalfExtent);
        //
    }
    private void ScanTree(QuadTreeNodeSerializable tree)
    {
        int detailedX = curXIdx * detailedSize;
        int detailedZ = curZIdx * detailedSize;

        OrderedSlices slices = new OrderedSlices();
        for (int u=0; u< detailedSize; ++u)
        {
            for (int v = 0; v < detailedSize; ++v)
            {
                slices.Clear();
                float fx = (curXIdx + (float)(u + 0.5f) / detailedSize) * sliceSize[0];
                float fz = (curZIdx + (float)(v + 0.5f) / detailedSize) * sliceSize[1];
                Vector3 top = vCheckTop + fx * Vector3.right + fz * Vector3.forward;
                mScanner.RunScan(top, heightPerGrade, heightValRange, slices);
                tree.AddPillar(subdivision, detailedX + u, detailedZ + v, slices);
            }
        }
    }
    public void Update()
    {
        if (IsDone)
            return;
        if (QuadTrees[curXIdx * maxZ + curZIdx] == null)
            QuadTrees[curXIdx * maxZ + curZIdx] = new QuadTreeNodeSerializable(subdivision);
        ScanTree((QuadTreeNodeSerializable)QuadTrees[curXIdx * maxZ + curZIdx]);
        //update idx
        ++curXIdx;
        if (curXIdx >= maxX)
        {
            if (curZIdx < maxZ - 1)
                curXIdx = 0;
            ++curZIdx;
        }
    }
    public PillarSetting CreateSetting()
    {
        PillarSetting setting = new PillarSetting();
        setting.heightPerGrade = heightPerGrade;
        setting.heightValRange = heightValRange;
        setting.maxX = maxX;
        setting.maxZ = maxZ;
        setting.subdivision = subdivision;
        setting.center = center;
        setting.sliceSize = sliceSize;
        setting.slopeErr = slopeErr;
        return setting;
    }
}

public class MPDataCreator : MonoBehaviour
{
    public Bounds VolumnBound;
    public Vector2 PillarSize;
    [HideInInspector]
    public int Subdivision = 3;
    [HideInInspector]
    public float SampleThickness = 0.1f;
    [HideInInspector]
    public float SlopeError = 0.1f;
    public bool DrawGizmo = true;
    public string DataName = "";
    //intermediate data
    private CreateDataJob mCreateDataJob;
    public float EditorCreateDataProgress
    {
        get
        {
            if (mCreateDataJob != null)
            {
                return mCreateDataJob.progress;
            }
            return 0;
        }
    }
    public bool IsEditorCreateDataDone
    {
        get
        {
            if (mCreateDataJob != null)
            {
                return mCreateDataJob.IsDone;
            }
            return true;
        }
    }
    public void EditorCreateDataBegin()
    {
        Subdivision = Mathf.Min(6, Subdivision);
        mCreateDataJob = new CreateDataJob(VolumnBound, Subdivision, PillarSize, SampleThickness, SlopeError);
    }
    public bool EditorCreateDataUpdate()
    {
        if (mCreateDataJob == null)
            return true;
        mCreateDataJob.Update();
        return mCreateDataJob.IsDone;
    }
    public void EditorCreateDataEnd()
    {
        if (mCreateDataJob == null || mCreateDataJob.QuadTrees == null)
            return;
        PillarSetting setting = mCreateDataJob.CreateSetting();
        //finaliz the tree data
        for (int i = 0; i < mCreateDataJob.QuadTrees.Length; ++i)
        {
            QuadTreeNodeSerializable node = (QuadTreeNodeSerializable)mCreateDataJob.QuadTrees[i];
            QuadTreeBase replaceLeaf = QuadTreeNode.CombineTree(node, 0.5f * mCreateDataJob.sliceSize[0],
                 0.5f * mCreateDataJob.sliceSize[1], mCreateDataJob.heightPerGrade, mCreateDataJob.slopeErr);
            if (replaceLeaf != null)
            {
                mCreateDataJob.QuadTrees[i] = replaceLeaf;
            }
        }
        //
        string path = string.Format("{0}/MightyPillar/Resources/{1}.bytes", Application.dataPath, DataName);
        MPFileUtil.SaveData(path, setting, mCreateDataJob.QuadTrees);
        MPDataDisplayer displayer = gameObject.GetComponent<MPDataDisplayer>();
        if (displayer != null)
            displayer.OnCreatorRegenData();
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!DrawGizmo)
            return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(VolumnBound.center, VolumnBound.size);
        if (PillarSize.magnitude > 0)
        {
            int uCount = Mathf.CeilToInt(VolumnBound.size.x / PillarSize.x);
            int vCount = Mathf.CeilToInt(VolumnBound.size.z / PillarSize.y);
            Vector3 vStart = new Vector3(VolumnBound.center.x - VolumnBound.size.x / 2,
                 VolumnBound.center.y - VolumnBound.size.y / 2,
                VolumnBound.center.z - VolumnBound.size.z / 2);
            for (int u=1; u<uCount; ++u)
            {
                for (int v = 1; v < vCount; ++v)
                {
                    Gizmos.DrawLine(vStart + v * PillarSize.y * Vector3.forward,
                        vStart + v * PillarSize.y * Vector3.forward + VolumnBound.size.x * Vector3.right);
                    Gizmos.DrawLine(vStart + u * PillarSize.x * Vector3.right,
                        vStart + u * PillarSize.x * Vector3.right + VolumnBound.size.x * Vector3.forward);
                }
            }
        }
    }
#endif
}
