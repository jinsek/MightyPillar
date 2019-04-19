namespace MightyPillar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    public static class MPLog
    {
        public static void Log(object message)
        {
            Debug.Log(message);
        }
        public static void LogError(object message)
        {
            Debug.LogError(message);
        }
    }
    public class MPPathResult : IMPPoolItem
    {
        public Vector3 Pos = Vector3.zero;
        public ushort heightGrade = 0;
        public ushort flag = 0;
        void IMPPoolItem.Reset()
        {
            Pos = Vector3.zero;
            heightGrade = 0;
            flag = 0;
        }
        public void Reset(Vector3 pos, ushort h, ushort f)
        {
            Pos = pos;
            heightGrade = h;
            flag = f;
        }
    }
    internal class MPPathResultPool : MPDataPool<MPPathResult>
    {
        public static MPPathResult Pop(Vector3 pos, ushort h, ushort f)
        {
            MPPathResult ret = null;
            if (mqPool.Count > 0)
                ret = mqPool.Dequeue();
            if (ret == null)
                ret = new MPPathResult();
            ret.Reset(pos, h, f);
            return ret;
        }
    }
    public struct MPDebugPlane
    {
        public Vector3 center;
        public Vector2 size;
    }
    public class MPUnityHeightScanner
    {
        private Vector3 checkHalfExtent = Vector3.one;
        private RaycastHit[] hitResultBuff = new RaycastHit[128];
        private RawSlice infinitRoof = new RawSlice();
        public MPUnityHeightScanner(Vector3 halfExtent)
        {
            checkHalfExtent = halfExtent;
            infinitRoof.flag = 0;
            infinitRoof.height = float.MaxValue;
        }
        public void Reset(Vector3 halfExtent)
        {
            checkHalfExtent = halfExtent;
        }
        public void RunScan(Vector3 topOrigin, float heightPerGrade, float[] hRange, OrderedSlices slices)
        {
            float rayLen = hRange[1] - hRange[0];
            int len = Physics.BoxCastNonAlloc(topOrigin, checkHalfExtent, Vector3.down, hitResultBuff,
                Quaternion.identity, 1.1f * rayLen);
            //floor
            for (int h = 0; h < len && h < SliceAccessor.MaxHeightSliceCount / 2; ++h)
            {
                RaycastHit hit = hitResultBuff[h];
                if (Vector3.Dot(hit.normal, Vector3.up) < 0)
                    continue;
                RawSlice rs = new RawSlice();
                rs.height = hit.point.y;
                rs.flag = 0;
                slices.Add(rs);
                //Debug.Log(string.Format("u : {0}, v : {1}, height : {2}, flag : {3}", curXIdx, curZIdx, rs.height, rs.flag));
            }
            slices.SortSlices();
            if (slices.Count == 0)
            {
                slices.Add(infinitRoof);
            }
            //ceiling
            Vector3 down = topOrigin;
            down.y = slices[0].height + 2f * heightPerGrade;
            len = Physics.BoxCastNonAlloc(down, checkHalfExtent, Vector3.up, hitResultBuff, Quaternion.identity, 1.1f * rayLen);
            for (int h = 0; h < len && h < SliceAccessor.MaxHeightSliceCount / 2; ++h)
            {
                RaycastHit hit = hitResultBuff[h];
                if (Vector3.Dot(hit.normal, Vector3.down) < 0)
                    continue;
                RawSlice rs = new RawSlice();
                rs.height = hit.point.y;
                rs.flag = 1;
                slices.Add(rs);
                //Debug.Log(string.Format("u : {0}, v : {1}, height : {2}, flag : {3}", curXIdx, curZIdx, rs.height, rs.flag));
            }
            slices.Unify(hRange[0], heightPerGrade);
        }
    }
    public class MPUnityAStar : MPAStarPath
    {
        //dynamic add obstacle
        private MPUnityHeightScanner mHeightScanner = new MPUnityHeightScanner(Vector3.one);
        //
        public MPUnityAStar(PillarData data) : base(data)
        {}
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        private void TransformPos2UV(Vector3 pos, ref int u, ref int v)
        {
            float fx = pos.x - (VolumeCenterX - 0.5f * mVolumeSizeX);
            float fz = pos.z - (VolumeCenterZ - 0.5f * mVolumeSizeZ);
            fx = Mathf.Clamp(fx, 0, mVolumeSizeX);
            fz = Mathf.Clamp(fz, 0, mVolumeSizeZ);
            u = Mathf.FloorToInt(fx / mDetailGridX);
            v = Mathf.FloorToInt(fz / mDetailGridZ);
        }
        private Vector3 TransformUV2Pos(ushort h, int u, int v)
        {
            return new Vector3(VolumeCenterX - 0.5f * mVolumeSizeX + u * mDetailGridX, h * VolumeHSliceT + VolumeFloor,
                VolumeCenterZ - 0.5f * mVolumeSizeZ + v * mDetailGridZ);
        }
        public void FindPath(Vector3 start, Vector3 dest, Stack<MPPathResult> result, Queue<MPDebugPlane> qDebug = null)
        {
            int srcx = 0;
            int srcz = 0;
            ushort srcH = (ushort)(Mathf.Clamp(start.y - VolumeFloor, 0, VolumeCeiling) / VolumeHSliceT);
            TransformPos2UV(start, ref srcx, ref srcz);
            ushort fixH = srcH;
            MPPathNode srcNode = mData.GetStandablePathNode(srcx, srcz, srcH);
            if (srcNode == null)
                return;
            int destx = 0;
            int destz = 0;
            ushort destH = (ushort)(Mathf.Clamp(dest.y - VolumeFloor, 0, VolumeCeiling) / VolumeHSliceT);
            TransformPos2UV(dest, ref destx, ref destz);
            MPPathNode destNode = mData.GetStandablePathNode(destx, destz, destH);
            if (destNode == null)
                return;
            //
            result.Push(MPPathResultPool.Pop(dest, fixH, 0));
            if (srcNode.UID == destNode.UID)
            {
                MPPathNodePool.Push(srcNode);
                MPPathNodePool.Push(destNode);
                return;
            }
            //
            Vector3 volumnMin = new Vector3(VolumeCenterX - 0.5f * mVolumeSizeX, VolumeFloor, 
                VolumeCenterZ - 0.5f * mVolumeSizeZ);
            Vector2 minCellSize = new Vector2(mDetailGridX, mDetailGridZ);
            if (qDebug != null)
                isDebug = true;
            if (FindPath(srcNode, destNode))
            {
                while (pathResult.Count > 0)
                {
                    MPPathNode node = pathResult.Dequeue();
                    //use request node as final node
                    if (node.UID == destNode.UID)
                        continue;
                    if (node.UID == srcNode.UID)
                    {
                        result.Push(MPPathResultPool.Pop(start, node.HeightGrade, node.Flag));
                        continue;
                    }
                   Vector3 nodeCenter = volumnMin + node.X * Vector3.right + node.Z * Vector3.forward +
                        node.HeightGrade * VolumeHSliceT * Vector3.up;
                    result.Push(MPPathResultPool.Pop(nodeCenter, node.HeightGrade, node.Flag));
                }
            }
            else
            {
                MPLog.Log("no path result.");
                result.Pop();
            }
            //debug
            while (isDebug && debugnodes.Count > 0)
            {
                MPPathNode node = debugnodes.Dequeue();
                MPDebugPlane plane = new MPDebugPlane();
                plane.center = volumnMin + node.X * Vector3.right + node.Z * Vector3.forward +
                    (node.HeightGrade + 2) * VolumeHSliceT * Vector3.up;
                plane.size = (1 << node.Subdivision) * minCellSize;
                qDebug.Enqueue(plane);
            }
            EndFindPath();
        }
        //dynamic obstacles
        public void DynamicAddPillar(Vector3 pos, Bounds bnd)
        {
            Vector3 min = pos - bnd.extents;
            Vector3 max = pos + bnd.extents;
            int startU = 0, startV = 0, endU = mData.setting.maxX, endV = mData.setting.maxZ;
            TransformPos2UV(min, ref startU, ref startV);
            TransformPos2UV(max, ref endU, ref endV);
            Vector3 volumnMin = new Vector3(VolumeCenterX - 0.5f * mVolumeSizeX, VolumeFloor,
                VolumeCenterZ - 0.5f * mVolumeSizeZ);
            Vector3 checkHalfExtent = new Vector3(GridX / 2, VolumeHSliceT, GridZ / 2);
            mHeightScanner.Reset(checkHalfExtent);
            HashSet<uint> dirtyNodes = new HashSet<uint>();
            OrderedSlices slices = new OrderedSlices();
            int detailedSize = 1 << mData.setting.subdivision;
            for (int u = startU; u <= endU; ++u)
            {
                for (int v = startV; v <= endV; ++v)
                {
                    int curXIdx = u >> mData.setting.subdivision;
                    int curZIdx = v >> mData.setting.subdivision;
                    QuadTreeBase subtree = mData.tree[curXIdx * mData.setting.maxZ + curZIdx];
                    QuadTreeNode node = null;
                    if (subtree is QuadTreeLeaf)
                    {
                        QuadTreeLeaf leaf = (QuadTreeLeaf)subtree;
                        node = QuadTreeNode.SubdivideLeaf(leaf);
                        mData.tree[curXIdx * mData.setting.maxZ + curZIdx] = node;
                    }
                    else
                    {
                        node = (QuadTreeNode)subtree;
                    }
                    uint dirtyId = (uint)curXIdx;
                    dirtyId = (dirtyId << 16) | (uint)curZIdx;
                    if (!dirtyNodes.Contains(dirtyId))
                        dirtyNodes.Add(dirtyId);
                    float fx = (float)(u + 0.5f) * mDetailGridX;
                    float fz = (float)(v + 0.5f) * mDetailGridZ;
                    Vector3 top = volumnMin + fx * Vector3.right + 
                        (VolumeCeiling + 10f * VolumeHSliceT) * Vector3.up +
                        fz * Vector3.forward;
                    slices.Clear();
                    mHeightScanner.RunScan(top, VolumeHSliceT, mData.setting.heightValRange, slices);
                    node.AddPillar(mData.setting.subdivision, u, v, slices);
                }
            }
            //merge
            foreach(var dirtyId in dirtyNodes)
            {
                uint x = dirtyId >> 16;
                uint z = dirtyId & 0x0000ffff;
                int idx = (int)(x * mData.setting.maxZ + z);
                QuadTreeNode node = (QuadTreeNode)mData.tree[idx];
                QuadTreeBase replaceLeaf = QuadTreeNode.CombineTree(node, 0.5f * GridX, 0.5f * GridZ, VolumeHSliceT, 
                    mData.setting.slopeErr);
                if (replaceLeaf != null)
                {
                    mData.tree[idx] = replaceLeaf;
                }
            }
        }
    }
}
