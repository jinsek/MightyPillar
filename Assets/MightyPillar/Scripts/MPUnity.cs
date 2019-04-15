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
    public static class MPDataAccessor
    {
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public static void TransformPos2UV(PillarData data, Vector3 pos, ref int u, ref int v)
        {
            int detail = 1 << data.setting.subdivision;
            float sizex = data.setting.maxX * data.setting.sliceSize[0];
            float sizez = data.setting.maxZ * data.setting.sliceSize[1];
            float fx = pos.x - (data.setting.center[0] - 0.5f * sizex);
            float fz = pos.z - (data.setting.center[2] - 0.5f * sizez);
            fx = Mathf.Clamp(fx, 0, sizex);
            fz = Mathf.Clamp(fz, 0, sizez);
            u = Mathf.FloorToInt(fx / (data.setting.sliceSize[0] / detail));
            v = Mathf.FloorToInt(fz / (data.setting.sliceSize[1] / detail));
        }
        public static Vector3 TransformUV2Pos(PillarData data, ushort h, int u, int v)
        {
            int detail = 1 << data.setting.subdivision;
            float sizex = data.setting.maxX * data.setting.sliceSize[0];
            float sizez = data.setting.maxZ * data.setting.sliceSize[1];
            return new Vector3(data.setting.center[0] - 0.5f * sizex + u * data.setting.sliceSize[0] / detail,
                h * data.setting.hegithPerGrade + data.setting.heightValRange[0],
                data.setting.center[2] - 0.5f * sizez + v * data.setting.sliceSize[1] / detail);
        }
        public static void FindPath(PillarData data, Vector3 start, Vector3 dest, Stack<MPPathResult> result,
            Queue<MPDebugPlane> qDebug = null)
        {
            int srcx = 0;
            int srcz = 0;
            ushort srcH = (ushort)(Mathf.Clamp(start.y - data.setting.heightValRange[0], 0, data.setting.heightValRange[1]) / 
                data.setting.hegithPerGrade);
            TransformPos2UV(data, start, ref srcx, ref srcz);
            ushort fixH = srcH;
            MPPathNode srcNode = data.GetStandablePathNode(srcx, srcz, srcH);
            if (srcNode == null)
                return;
            int destx = 0;
            int destz = 0;
            ushort destH = (ushort)(Mathf.Clamp(dest.y - data.setting.heightValRange[0], 0, data.setting.heightValRange[1]) /
                data.setting.hegithPerGrade);
            TransformPos2UV(data, dest, ref destx, ref destz);
            MPPathNode destNode = data.GetStandablePathNode(destx, destz, destH);
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
            Vector3 volumnMin = new Vector3(data.setting.center[0] - 0.5f * data.setting.maxX * data.setting.sliceSize[0],
                data.setting.heightValRange[0],
                data.setting.center[2] - 0.5f * data.setting.maxZ * data.setting.sliceSize[1]);
            Vector2 minCellSize = new Vector2(data.setting.sliceSize[0], data.setting.sliceSize[1]);
            minCellSize *= 1f / (1 << data.setting.subdivision);
            if (qDebug != null)
                MPAStarPath.isDebug = true;
            if (MPAStarPath.FindPath(data, srcNode, destNode))
            {
                MPPathNode prevNode = destNode;
                while (MPAStarPath.pathResult.Count > 0)
                {
                    MPPathNode node = MPAStarPath.pathResult.Dequeue();
                    //use request node as final node
                    if (node.UID == destNode.UID)
                        continue;
                    if (node.UID == srcNode.UID)
                    {
                        result.Push(MPPathResultPool.Pop(start, node.HeightGrade, node.Flag));
                        continue;
                    }
                    if (MPAStarPath.pathResult.Count > 0 && node.Subdivision > prevNode.Subdivision && 
                        node.HeightGrade == prevNode.HeightGrade &&
                        node.Subdivision > MPAStarPath.pathResult.Peek().Subdivision)
                    {
                        prevNode = node;
                        continue;
                    }
                    prevNode = node;
                   //subdivision -- bigger subdivision -- subdivision
                   //the pass by bigger subdivision can be remove
                   Vector3 nodeCenter = volumnMin + node.X * Vector3.right + node.Z * Vector3.forward +
                        node.HeightGrade * data.setting.hegithPerGrade * Vector3.up;
                    result.Push(MPPathResultPool.Pop(nodeCenter, node.HeightGrade, node.Flag));
                }
            }
            else
            {
                MPLog.Log("no path result.");
                result.Pop();
            }
            //debug
            while (MPAStarPath.isDebug && MPAStarPath.debugnodes.Count > 0)
            {
                MPPathNode node = MPAStarPath.debugnodes.Dequeue();
                MPDebugPlane plane = new MPDebugPlane();
                plane.center = volumnMin + node.X * Vector3.right + node.Z * Vector3.forward +
                    (node.HeightGrade + 2) * data.setting.hegithPerGrade * Vector3.up;
                plane.size = (1 << node.Subdivision) * minCellSize;
                qDebug.Enqueue(plane);
            }
            MPPathNodePool.Push(srcNode);
            MPPathNodePool.Push(destNode);
            MPAStarPath.EndFindPath();
        }
        //dynamic obstacles
        public static void DynamicAddPillar(PillarData data, Vector3 pos, Bounds bnd)
        {
            Vector3 min = pos - bnd.extents;
            Vector3 max = pos + bnd.extents;
            int startU = 0, startV = 0, endU = data.setting.maxX, endV = data.setting.maxZ;
            TransformPos2UV(data, min, ref startU, ref startV);
            TransformPos2UV(data, max, ref endU, ref endV);
            Vector3 volumnMin = new Vector3(data.setting.center[0] - 0.5f * data.setting.maxX * data.setting.sliceSize[0],
                data.setting.heightValRange[0],
                data.setting.center[2] - 0.5f * data.setting.maxZ * data.setting.sliceSize[1]);
            Vector3 checkHalfExtent = new Vector3(data.setting.sliceSize[0] / 2, data.setting.hegithPerGrade,
                data.setting.sliceSize[1] / 2);
            RaycastHit[] hitResultBuff = new RaycastHit[128];
            HashSet<uint> dirtyNodes = new HashSet<uint>();
            float rayLen = 1.1f * (data.setting.heightValRange[1] - data.setting.heightValRange[0]);
            int detailedSize = 1 << data.setting.subdivision;
            for (int u = startU; u <= endU; ++u)
            {
                for (int v = startV; v <= endV; ++v)
                {
                    int curXIdx = u >> data.setting.subdivision;
                    int curZIdx = v >> data.setting.subdivision;
                    QuadTreeBase subtree = data.tree[curXIdx * data.setting.maxZ + curZIdx];
                    QuadTreeNode node = null;
                    if (subtree is QuadTreeLeaf)
                    {
                        QuadTreeLeaf leaf = (QuadTreeLeaf)subtree;
                        node = new QuadTreeNode(0);
                        //subdivide this into leafs
                        for (int i = 0; i < 4; ++i)
                        {
                            QuadTreeLeaf childLeaf = (QuadTreeLeaf)node.Children[i];
                            childLeaf.Reset(leaf.Slices.Length, leaf.HashVal);
                            Array.Copy(leaf.Slices, childLeaf.Slices, leaf.Slices.Length);
                        }
                        HeightSlicePool.Push(leaf.Header, leaf.Slices);
                        data.tree[curXIdx * data.setting.maxZ + curZIdx] = node;
                    }
                    else
                    {
                        node = (QuadTreeNode)subtree;
                    }
                    uint dirtyId = (uint)curXIdx;
                    dirtyId = (dirtyId << 16) | (uint)curZIdx;
                    if (!dirtyNodes.Contains(dirtyId))
                        dirtyNodes.Add(dirtyId);
                    OrderedSlices slices = new OrderedSlices();
                    float fx = (float)(u + 0.5f) / detailedSize * data.setting.sliceSize[0];
                    float fz = (float)(v + 0.5f) / detailedSize * data.setting.sliceSize[1];
                    Vector3 top = volumnMin + fx * Vector3.right + 
                        (data.setting.heightValRange[1] + 10f * data.setting.hegithPerGrade) * Vector3.up +
                        fz * Vector3.forward;
                    int len = Physics.BoxCastNonAlloc(top, checkHalfExtent, Vector3.down, hitResultBuff, Quaternion.identity, 1.1f * rayLen);
                    //floor
                    for (int h = 0; h < len && h < SliceAccessor.MaxHeightSliceCount / 2; ++h)
                    {
                        RaycastHit hit = hitResultBuff[h];
                        if (Vector3.Dot(hit.normal, Vector3.up) < 0)
                            continue;
                        RawSlice rs = new RawSlice();
                        rs.height = hit.point.y;
                        rs.flag = 0;
                        rs.heightGrade = ushort.MaxValue;
                        slices.Add(rs);
                        //Debug.Log(string.Format("u : {0}, v : {1}, height : {2}, flag : {3}", curXIdx, curZIdx, rs.height, rs.flag));
                    }
                    slices.SortSlices();
                    if (slices.Count == 0)
                    {
                        slices.Add(new RawSlice());
                    }
                    //ceiling
                    Vector3 down = top;
                    down.y = slices[0].height + 2f * data.setting.hegithPerGrade;
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
                    slices.Unify(data.setting.heightValRange[0], data.setting.hegithPerGrade);
                    node.AddPillar(data.setting.subdivision, u, v, slices);
                }
            }
            //merge
            foreach(var dirtyId in dirtyNodes)
            {
                uint x = dirtyId >> 16;
                uint z = dirtyId & 0x0000ffff;
                int idx = (int)(x * data.setting.maxZ + z);
                QuadTreeNode node = (QuadTreeNode)data.tree[idx];
                node.CombineTree();
                if (node.IsCombinableLeaf)
                {
                    data.tree[idx] = (QuadTreeLeaf)node.Children[0];
                    for (int cid = 1; cid < 4; ++cid)
                    {
                        QuadTreeLeaf leaf = (QuadTreeLeaf)node.Children[cid];
                        HeightSlicePool.Push(leaf.Header, leaf.Slices);
                    }
                }
            }
        }
    }
}
