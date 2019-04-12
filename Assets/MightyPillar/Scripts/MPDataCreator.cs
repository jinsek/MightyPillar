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
    public PillarQuadTree[] QuadTrees { get; private set; }
    private Vector3 checkHalfExtent = Vector3.one;
    private RaycastHit[] hitResultBuff = new RaycastHit[128];
    private Vector3 vCheckTop = Vector3.one;
    private Vector3 vCheckDown = Vector3.zero;
    private float mSliceThickness = SliceAccessor.minSliceThickness;
    private int curXIdx = 0;
    private int curZIdx = 0;
    public float[] heightValRange = new float[] { float.MaxValue, float.MinValue};
    public float[] center = new float[] { 0, 0, 0 };
    private RawSlice infinitRoof = new RawSlice();
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
    private void UpdateHeightRange(float height)
    {
        if (height > heightValRange[1])
            heightValRange[1] = height;
        if (height < heightValRange[0])
            heightValRange[0] = height;
    }
    public CreateDataJob(Bounds VolumnBound, int sub, Vector2 PillarSize, float thickness)
    {
        maxX = Mathf.CeilToInt(VolumnBound.size.x / PillarSize.x);
        maxZ = Mathf.CeilToInt(VolumnBound.size.z / PillarSize.y);
        subdivision = sub;
        center = new float[] { VolumnBound.center.x, VolumnBound.center.y, VolumnBound.center.z };
        sliceSize = new float[] { PillarSize.x, PillarSize.y };
        vCheckTop = new Vector3(VolumnBound.center.x - VolumnBound.size.x / 2,
             VolumnBound.center.y + VolumnBound.size.y / 2 + 10f * thickness,
            VolumnBound.center.z - VolumnBound.size.z / 2);
        vCheckDown = new Vector3(VolumnBound.center.x - VolumnBound.size.x / 2,
             VolumnBound.center.y - VolumnBound.size.y / 2 - 10f * thickness,
            VolumnBound.center.z - VolumnBound.size.z / 2);
        checkHalfExtent = new Vector3(sliceSize[0] / 2, thickness, sliceSize[1] / 2);
        mSliceThickness = Mathf.Max(SliceAccessor.minSliceThickness, thickness);
        QuadTrees = new PillarQuadTree[maxX * maxZ];
        //
        infinitRoof.flag = 0;
        infinitRoof.height = float.MaxValue;
        detailedSize = 1 << subdivision;
        checkHalfExtent = 1f / detailedSize * checkHalfExtent;
        //
    }
    private void ScanTree(PillarQuadTree tree)
    {
        float rayLen = vCheckTop.y - vCheckDown.y;
        int detailedX = curXIdx * detailedSize;
        int detailedZ = curZIdx * detailedSize;
        
        for (int u=0; u< detailedSize; ++u)
        {
            for (int v = 0; v < detailedSize; ++v)
            {
                OrderedSlices slices = new OrderedSlices();
                float fx = (curXIdx + (float)(u + 0.5f) / detailedSize) * sliceSize[0];
                float fz = (curZIdx + (float)(v + 0.5f) / detailedSize) * sliceSize[1];
                Vector3 top = vCheckTop + fx * Vector3.right + fz * Vector3.forward;
                int len = Physics.BoxCastNonAlloc(top, checkHalfExtent, Vector3.down, hitResultBuff, Quaternion.identity, 1.1f * rayLen);
                //floor
                for (int h = 0; h < len; ++h)
                {
                    RaycastHit hit = hitResultBuff[h];
                    if (Vector3.Dot(hit.normal, Vector3.up) < 0)
                        continue;
                    RawSlice rs = new RawSlice();
                    rs.height = hit.point.y;
                    rs.flag = 0;
                    rs.heightGrade = ushort.MaxValue;
                    slices.Add(rs);
                    UpdateHeightRange(rs.height);
                    //Debug.Log(string.Format("u : {0}, v : {1}, height : {2}, flag : {3}", curXIdx, curZIdx, rs.height, rs.flag));
                }
                slices.SortSlices();
                if (slices.Count == 0)
                {
                    slices.Add(infinitRoof);
                }
                //ceiling
                Vector3 down = vCheckTop;
                down.y = slices[0].height + 10f * mSliceThickness;
                down += fx * Vector3.right + fz * Vector3.forward;
                len = Physics.BoxCastNonAlloc(down, checkHalfExtent, Vector3.up, hitResultBuff, Quaternion.identity, 1.1f * rayLen);
                for (int h = 0; h < len; ++h)
                {
                    RaycastHit hit = hitResultBuff[h];
                    if (Vector3.Dot(hit.normal, Vector3.down) < 0)
                        continue;
                    RawSlice rs = new RawSlice();
                    rs.height = hit.point.y;
                    rs.flag = 1;
                    slices.Add(rs);
                    UpdateHeightRange(rs.height);
                    //Debug.Log(string.Format("u : {0}, v : {1}, height : {2}, flag : {3}", curXIdx, curZIdx, rs.height, rs.flag));
                }
                tree.AddPillar(subdivision, detailedX + u, detailedZ + v, slices);
            }
        }
    }
    public void Update()
    {
        if (IsDone)
            return;
        if (QuadTrees[curXIdx * maxZ + curZIdx] == null)
            QuadTrees[curXIdx * maxZ + curZIdx] = new PillarQuadTree(subdivision);
        ScanTree(QuadTrees[curXIdx * maxZ + curZIdx]);
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
        int sliceCount = Mathf.CeilToInt((heightValRange[1] - heightValRange[0]) / mSliceThickness);
        sliceCount = Mathf.Min(sliceCount, ushort.MaxValue);
        setting.hegithPerGrade = (heightValRange[1] - heightValRange[0])/ sliceCount;
        setting.heightValRange = heightValRange;
        setting.maxX = maxX;
        setting.maxZ = maxZ;
        setting.subdivision = subdivision;
        setting.center = center;
        setting.sliceSize = sliceSize;
        return setting;
    }
}

public class MPDataCreator : MonoBehaviour
{
    public Bounds VolumnBound;
    public Vector2 PillarSize;
    public int Subdivision { get; private set; } = 3;
    public float SliceThickness { get; private set; } = 0.1f;
    public bool DrawGizmo = true;
    public string DataName = "";
    public void SetSliceThickness(float t)
    {
        SliceThickness = t;
    }
    public void SetSubdivision(int t)
    {
        Subdivision = Mathf.Min(6, t);
    }
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
        mCreateDataJob = new CreateDataJob(VolumnBound, Subdivision, PillarSize , SliceThickness);
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
            mCreateDataJob.QuadTrees[i].UnifySlice(setting.heightValRange[0], setting.hegithPerGrade);
        }
        for (int i = 0; i < mCreateDataJob.QuadTrees.Length; ++i)
        {
            mCreateDataJob.QuadTrees[i].CombineTree();
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
