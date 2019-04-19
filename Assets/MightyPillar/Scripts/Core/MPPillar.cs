//MIGHTY PILLAR created by chenshi, jinsekcs@163.com
//Pillar is the base element of the mighty-pillar tool-set
//1. One pillar is a set of space slices following Y axis which represent the height information of ground/obstacles.
//2. The first grade horizontal cells are actually root nodes of a quad tree, which subdivided the cells to get more 
//accurate pillars.

namespace MightyPillar
{
    using System;
    using System.Collections.Generic;

    public class PillarSetting
    {
        //base data format
        //max(maxX * power(2, subdivision), maxZ * power(2, subdivision)) < 8192
        public int maxX;
        public int maxZ;
        public int subdivision;
        public float slopeErr;
        //volumn center
        public float[] center;
        //total height grades
        public float heightPerGrade;
        //each slice's size, x & z
        public float[] sliceSize;
        //min/max height value
        public float[] heightValRange;
        //-----------------------------
        //path-finding parameter, don't save them
        //the height of character
        public float boundHeight;
        //the height of character can jump
        public float jumpableHeight;
        //-----------------------------
        public int byteSize()
        {
            return sizeof(int) * 3 + sizeof(float) * 9;
        }
        private void AssginBytes(byte[] src, byte[] dst, ref int startIdx)
        {
            for (int i = 0; i < src.Length; ++i)
                dst[startIdx + i] = src[i];
            startIdx += src.Length;
        }
        public byte[] ToArray()
        {
            byte[] arr = new byte[byteSize()];
            int offset = 0;
            AssginBytes(BitConverter.GetBytes(maxX), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(maxZ), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(subdivision), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(slopeErr), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(center[0]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(center[1]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(center[2]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(heightPerGrade), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(sliceSize[0]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(sliceSize[1]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(heightValRange[0]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(heightValRange[1]), arr, ref offset);
            return arr;
        }
        public void Reset(byte[] data)
        {
            if (data.Length < byteSize())
                return;
            int offset = 0;
            maxX = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            maxZ = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            subdivision = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            slopeErr = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            center = new float[3];
            center[0] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            center[1] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            center[2] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            heightPerGrade = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            sliceSize = new float[2];
            sliceSize[0] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            sliceSize[1] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            heightValRange = new float[2];
            heightValRange[0] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            heightValRange[1] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
        }
    }
    public static class SliceAccessor
    {
        public const float minSliceThickness = 0.001f;
        public const int MaxHeightSliceCount = 64;
        public const float maxSlopeError = 10; //around 5.71 degree
        public const float slopeMagnify = 1000f;
        public const ushort SliceCeiling = 1;
        public static ushort heightGrade(ulong val)
        {
            return (ushort)(val >> 48);
        }
        public static short slopeUGrade(ulong val)
        {
            return (short)((val & 0x0000ffff00000000) >> 32);
        }
        public static short slopeVGrade(ulong val)
        {
            return (short)((val & 0x00000000ffff0000) >> 16);
        }
        public static byte flag(ulong val)
        {
            return (byte)(val & 0x00000000000000ff);
        }
        public static ulong packVal(ushort h, short su, short sv, byte f)
        {
            ulong val = h;
            val <<= 48;
            ulong temp = (ulong)(su & 0x000000000000ffff);
            val |= temp << 32;
            temp = (ulong)(sv & 0x000000000000ffff);
            val |= temp << 16;
            val |= f;
            return val;
        }
        public static float heightVal(float minHeight, float heightPerGrade, ulong val)
        {
            ushort grade = (ushort)(val >> 48);
            return minHeight + grade * heightPerGrade;
        }
        public static float heightVal(PillarSetting setting, uint val)
        {
            ushort grade = (ushort)(val >> 48);
            return setting.heightValRange[0] + grade * setting.heightPerGrade;
        }
    }
    //data structures for data creating
    public class RawSlice
    {
        public float height = 0;
        public byte flag = 0;
        public ushort heightGrade = 0;
    }
    //data structure for data display
    public class DisplaySlice
    {
        public float height;
        public float[] min;
        public float[] max;
        public byte flag;
    }
    //data container
    internal class HeightSlicePool
    {
        private static Dictionary<int, Queue<ulong[]>> mPools = new Dictionary<int, Queue<ulong[]>>();
        private static Dictionary<uint, ulong[]> mSlices = new Dictionary<uint, ulong[]>();
        public static ulong[] GetSlices(uint header)
        {
            if (mSlices.ContainsKey(header))
                return mSlices[header];
            return null;
        }
        public static ulong[] Pop(uint header, int len)
        {
            ulong[] item = null;
            if (mPools.ContainsKey(len) && mPools[len].Count > 0)
                item = mPools[len].Dequeue();
            if (item == null)
                item = new ulong[len];
            mSlices.Add(header, item);
            return item;
        }
        public static void Push(uint header, ulong[] slices)
        {
            if (slices.Length == 0)
                return;
            if (!mPools.ContainsKey(slices.Length))
                mPools.Add(slices.Length, new Queue<ulong[]>());
            mPools[slices.Length].Enqueue(slices);
            if (mSlices.ContainsKey(header))
                mSlices.Remove(header);
        }
    }
    public abstract class QuadTreeBase
    {
        public abstract string DebugOutput { get; }
        public abstract bool IsEqual(QuadTreeBase other);
    }
    public class QuadTreeLeaf : QuadTreeBase
    {
        private static uint _uid_seed = 0;
        public override string DebugOutput
        {
            get
            {
                return Slices.Length.ToString();
            }
        }
        public ulong[] Slices { get; private set; }
        public ulong HashVal { get; protected set; }
        public uint Header { get; private set; }
        public QuadTreeLeaf()
        {
            Header = uint.MaxValue;
            Slices = null;
            HashVal = 0;
        }
        public void Reset(int len, ulong hash)
        {
            Header = _uid_seed;
            if (_uid_seed + len >= uint.MaxValue)
                _uid_seed = 0;
            _uid_seed += (uint)len;
            Slices = HeightSlicePool.Pop(Header, len);
            HashVal = hash;
        }
        public void RefreshHash()
        {
            HashVal = 0;
            if (Slices == null)
                return;
            for (int i = 0; i < Slices.Length; ++i)
                HashVal += Slices[i];
        }
        public override bool IsEqual(QuadTreeBase other)
        {
            if (Slices != null && other is QuadTreeLeaf)
            {
                QuadTreeLeaf otherLeaf = (QuadTreeLeaf)other;
                if (otherLeaf.Slices != null && HashVal == otherLeaf.HashVal)
                    return true;
            }
            return false;
        }
        public bool IsCombinable(QuadTreeLeaf other)
        {
            if (other != null && Slices != null && other.Slices != null &&
                Slices.Length == other.Slices.Length)
                return true;
            return false;
        }
    }
    public class QuadTreeNode : QuadTreeBase
    {
        public override string DebugOutput
        {
            get
            {
                string s = "";
                for (int i=0; i<4; ++i)
                    s += Children[i].DebugOutput;
                return s;
            }
        }
        public QuadTreeBase[] Children = new QuadTreeBase[4];
        public override bool IsEqual(QuadTreeBase other)
        {
            return false;
        }
        private bool isFullLeaf
        {
            get
            {
                for (int i = 0; i < Children.Length; ++i)
                {
                    if (Children[i] == null || !(Children[i] is QuadTreeLeaf))
                        return false;
                }
                return true;
            }
        }
        protected QuadTreeNode() { }
        //build a full tree
        public QuadTreeNode(int sub)
        {
            for (int i = 0; i < 4; ++i)
            {
                if(sub > 1)
                    Children[i] = CreateSubTree(sub - 1);
                else
                    Children[i] = CreateLeaf();
            }
        }
        protected virtual QuadTreeNode CreateSubTree(int sub) { return new QuadTreeNode(sub); }
        protected virtual QuadTreeLeaf CreateLeaf() { return new QuadTreeLeaf(); }
        //dynamically add pillars in
        //x ~ (0, setting.maxX * power(2, subdivision)), x ~ (0, setting.maxZ * power(2, subdivision))
        public void AddPillar(int subdivision, int x, int z, OrderedSlices rawSlices)
        {
            //first grade
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            int subx = x - u * (1 << subdivision);
            int subz = z - v * (1 << subdivision);
            --subdivision;
            int idx = (subx >> subdivision) * 2 + (subz >> subdivision);
            if (subdivision > 0)
            {
                if (Children[idx] is QuadTreeLeaf)
                {
                    SubdividLeaf(idx);
                }
                QuadTreeNode node = (QuadTreeNode)Children[idx];
                node.AddPillar(subdivision, subx, subz, rawSlices);
            }
            else
            {
                if (Children[idx] is QuadTreeNode)
                {
                    MPLog.LogError("AddPillar leaf still a tree : " + subdivision);
                    return;
                }
                QuadTreeLeaf leaf = (QuadTreeLeaf)Children[idx];
                if (leaf.Slices != null)
                    HeightSlicePool.Push(leaf.Header, leaf.Slices);
                leaf.Reset(rawSlices.Count, rawSlices.HashValue);
                for (int i = 0; i < rawSlices.Count; ++i)
                {
                    leaf.Slices[i] = SliceAccessor.packVal(rawSlices[i].heightGrade, 0, 0, rawSlices[i].flag);
                }
            }
        }
        private void SubdividLeaf(int idx)
        {
            QuadTreeLeaf leaf = (QuadTreeLeaf)Children[idx];
            Children[idx] = SubdivideLeaf(leaf);
        }
        private short GetSlope(ulong start, ulong end, float delta, float sliceThickness)
        {
            uint startH = SliceAccessor.heightGrade(start);
            uint endH = SliceAccessor.heightGrade(end);
            return (short)(SliceAccessor.slopeMagnify * sliceThickness * (endH - startH) / delta);
        }
        private QuadTreeLeaf Combine(float dx, float dz, float sliceThickness, float slopeErr)
        {
            if (Children[0] == null || !(Children[0] is QuadTreeLeaf))
                return null;
            QuadTreeLeaf leaf = (QuadTreeLeaf)Children[0];
            for (int i = 1; i < Children.Length; ++i)
            {
                if (Children[i] == null || !(Children[i] is QuadTreeLeaf))
                    return null;
                if (!leaf.IsCombinable((QuadTreeLeaf)Children[i]))
                    return null;
            }
            for (int s = 0; s < leaf.Slices.Length; ++s)
            {
                ulong leafS = leaf.Slices[s];
                byte flag = SliceAccessor.flag(leafS);
                //x axis
                short slopeU = GetSlope(leafS, ((QuadTreeLeaf)Children[1]).Slices[s], dx, sliceThickness);
                short slopeU1 = GetSlope(((QuadTreeLeaf)Children[2]).Slices[s], ((QuadTreeLeaf)Children[3]).Slices[s],
                    dx, sliceThickness);
                if ((slopeU != 0 && slopeU1 != 0 && Math.Abs(slopeU1 - slopeU) > slopeErr * SliceAccessor.slopeMagnify))
                    return null;
                slopeU += slopeU1; slopeU /= 2;
                //z axis
                short slopeV = GetSlope(leafS, ((QuadTreeLeaf)Children[2]).Slices[s], dz, sliceThickness);
                short slopeV1 = GetSlope(((QuadTreeLeaf)Children[1]).Slices[s], ((QuadTreeLeaf)Children[3]).Slices[s],
                    dz, sliceThickness);
                if ((slopeV != 0 && slopeV1 != 0 && Math.Abs(slopeV1 - slopeV) > slopeErr * SliceAccessor.slopeMagnify))
                    return null;
                slopeV += slopeV1; slopeV /= 2;
                ushort updateH = 0;
                for (int i = 0; i < Children.Length; ++i)
                {
                    QuadTreeLeaf l = (QuadTreeLeaf)Children[i];
                    ulong lS = l.Slices[s];
                    if (leaf.HashVal != l.HashVal)
                    {
                        byte f = SliceAccessor.flag(lS);
                        short sU = SliceAccessor.slopeUGrade(lS);
                        short sV = SliceAccessor.slopeVGrade(lS);
                        //floor cant match ceiling
                        if (flag != f)
                            return null;
                        //slope error
                        if ((sU != 0 && Math.Abs(sU - slopeU) > slopeErr * SliceAccessor.slopeMagnify) || 
                            (sV != 0 && Math.Abs(sV - slopeV) > slopeErr * SliceAccessor.slopeMagnify))
                            return null;
                    }
                    updateH += SliceAccessor.heightGrade(lS);
                }
                updateH >>= 2;//average height
                leaf.Slices[s] = SliceAccessor.packVal(updateH, slopeU, slopeV, flag);
            }
            leaf.RefreshHash();
            return leaf;
        }
        //note : this function can't be used to subdivide inherented node
        public static QuadTreeNode SubdivideLeaf(QuadTreeLeaf leaf)
        {
            if (leaf == null)
                return null;
            QuadTreeNode node = new QuadTreeNode(0);
            for (int i = 0; i < 4; ++i)
            {
                QuadTreeLeaf childLeaf = (QuadTreeLeaf)node.Children[i];
                childLeaf.Reset(leaf.Slices.Length, leaf.HashVal);
                Array.Copy(leaf.Slices, childLeaf.Slices, leaf.Slices.Length);
            }
            HeightSlicePool.Push(leaf.Header, leaf.Slices);
            return node;
        }
        public static QuadTreeLeaf CombineTree(QuadTreeNode node, float dx, float dz, float sliceThickness, float slopeErr)
        {
            if (node == null)
                return null;
            for (int i = 0; i < 4; ++i)
            {
                if (node.Children[i] is QuadTreeNode)
                {
                    QuadTreeNode subNode = (QuadTreeNode)node.Children[i];
                    QuadTreeLeaf replacedLeaf = CombineTree(subNode, 0.5f * dx, 0.5f * dz, sliceThickness, slopeErr);
                    if (replacedLeaf != null)
                        node.Children[i] = replacedLeaf;
                }
            }
            if (node.isFullLeaf)
            {
                return node.Combine(dx, dz, sliceThickness, slopeErr);
            }
            return null;
        }
        public byte GetChildMask()
        {
            byte mask = 0;
            if (Children == null)
                return mask;
            if (!(Children[0] is QuadTreeLeaf)) mask |= 1;
            if (!(Children[1] is QuadTreeLeaf)) mask |= 2;
            if (!(Children[2] is QuadTreeLeaf)) mask |= 4;
            if (!(Children[3] is QuadTreeLeaf)) mask |= 8;
            return mask;
        }
        public QuadTreeLeaf GetLeaf(int x, int z, float sizex, float sizez, ref float centerx, ref float centerz, 
            ref int subdivision)
        {
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            //center pos
            centerx += sizex * u;
            centerz += sizez * v;
            //
            int idx = u * 2 + v;
            QuadTreeBase subtree = Children[idx];
            if (subtree is QuadTreeLeaf)
            {
                centerx += sizex * 0.5f;
                centerz += sizex * 0.5f;
                return (QuadTreeLeaf)subtree;
            }
            else
            {//sub tree node
                int detail = 1 << subdivision;
                int subx = x - u * detail;
                int subz = z - v * detail;
                subdivision -= 1;
                QuadTreeNode node = (QuadTreeNode)subtree;
                return node.GetLeaf(subx, subz, sizex * 0.5f, sizez * 0.5f, ref centerx, ref centerz, ref subdivision);
            }
        }
    }
    public class PillarData
    {
        public string DataName = "";
        public PillarSetting setting = new PillarSetting();
        public QuadTreeBase[] tree;
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public QuadTreeLeaf GetLeaf(int x, int z, ref float centerx, ref float centerz, ref int subdivision)
        {
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            int idx = u * setting.maxZ + v;
            if (idx > tree.Length)
                return null;
            QuadTreeBase subtree = tree[idx];
            //center pos
            centerx = setting.sliceSize[0] * u;
            centerz = setting.sliceSize[1] * v;
            //
            if (subtree is QuadTreeLeaf)
            {
                centerx += setting.sliceSize[0] * 0.5f;
                centerz += setting.sliceSize[1] * 0.5f;
                return (QuadTreeLeaf)subtree;
            }
            else
            {//sub tree node
                int detail = 1 << subdivision;
                int subx = x - u * detail;
                int subz = z - v * detail;
                subdivision -= 1;
                QuadTreeNode node = (QuadTreeNode)subtree;
                return node.GetLeaf(subx, subz, setting.sliceSize[0] * 0.5f, setting.sliceSize[1] * 0.5f, 
                    ref centerx, ref centerz, ref subdivision);
            }
        }
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public MPPathNode GetStandablePathNode(int u, int v, ushort refH)
        {
            float centerx = 0;
            float centerz = 0;
            int subdivision = setting.subdivision;
            QuadTreeLeaf leaf = GetLeaf(u, v, ref centerx, ref centerz, ref subdivision);
            if (leaf == null || leaf.Slices == null)
                return null;
            for (uint i = (uint)leaf.Slices.Length - 1; i > 0; --i)
            {
                ulong currentSlice = leaf.Slices[i];
                byte currentf = SliceAccessor.flag(currentSlice);
                ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                ulong lowerSlice = leaf.Slices[i - 1];
                ushort lowerf = SliceAccessor.flag(lowerSlice);
                ushort lowerheight = SliceAccessor.heightGrade(lowerSlice);
                if ((currentf & SliceAccessor.SliceCeiling) > 0)
                    continue;
                if (refH >= currentheight ||
                    ((lowerf & SliceAccessor.SliceCeiling) > 0 && refH >= lowerheight))
                {//pillar roof
                    return MPPathNodePool.Pop(leaf.Header, u, v, i, subdivision, setting.subdivision, 
                        centerx, centerz, currentSlice);
                }
            }
            ulong floorSlice = leaf.Slices[0];
            return MPPathNodePool.Pop(leaf.Header, u, v, 0, subdivision, setting.subdivision, 
                centerx, centerz, floorSlice);
        }
        protected virtual bool CanMove2Neighbour(PillarSetting setting, ushort curH, ushort curMaxH,
            ushort neighbourH, ushort neighbourMaxH)
        {
            //jumpable
            if (Math.Abs(curH - neighbourH) * setting.heightPerGrade > setting.jumpableHeight)
                return false;
            //gap like this, cant go
            // _____|
            //      _______
            //      |
            // _____|  
            if (neighbourH > curH)
            {
                float gapHeight = (curMaxH - neighbourH) * setting.heightPerGrade;
                if (gapHeight >= setting.boundHeight)
                    return true;
            }
            else
            {
                if (neighbourMaxH > curH)
                {
                    float gapHeight = (neighbourMaxH - curH) * setting.heightPerGrade;
                    if (gapHeight >= setting.boundHeight)
                        return true;
                }
            }
            return false;
        }
        public void GetPathNeighbours(MPPathNode current, int maxX, int maxZ, MPNeigbours neighbours)
        {
            ulong[] curSlices = HeightSlicePool.GetSlices(current.SliceHeader);
            if (curSlices == null)
                return;
            //get current ceiling, where I can't go across
            ushort maxH = ushort.MaxValue;
            if (current.HIdx < curSlices.Length - 1)
            {
                ulong higherSlice = curSlices[current.HIdx + 1];
                ushort higherf = SliceAccessor.flag(higherSlice);
                ushort higherheight = SliceAccessor.heightGrade(higherSlice);
                if ((higherf & SliceAccessor.SliceCeiling) > 0)
                    maxH = higherheight;
            }
            for (int u = current.BoundaryXMin; u <= current.BoundaryXMax; ++u)
            {
                if (u < 0 || u >= maxX)
                    continue;
                if (current.BoundaryZMin >= 0 && current.BoundaryZMin < maxZ)
                    GetPathNeighbour(current.HeightGrade, maxH, u, current.BoundaryZMin, neighbours);
                if (current.BoundaryZMax >= 0 && current.BoundaryZMax < maxZ)
                    GetPathNeighbour(current.HeightGrade, maxH, u, current.BoundaryZMax, neighbours);
            }
            for (int v = current.BoundaryZMin; v <= current.BoundaryZMax; ++v)
            {
                if (v < 0 || v >= maxZ)
                    continue;
                if (current.BoundaryXMin >= 0 && current.BoundaryXMin < maxX)
                    GetPathNeighbour(current.HeightGrade, maxH, current.BoundaryXMin, v, neighbours);
                if (current.BoundaryXMax >= 0 && current.BoundaryXMax < maxX)
                    GetPathNeighbour(current.HeightGrade, maxH, current.BoundaryXMax, v, neighbours);
            }
        }
        private void GetPathNeighbour(ushort curH, ushort maxH, int u, int v, MPNeigbours neighbours)
        {
            float centerx = 0;
            float centerz = 0;
            int subdivision = setting.subdivision;
            QuadTreeLeaf leaf = GetLeaf(u, v, ref centerx, ref centerz, ref subdivision);
            if (leaf == null || leaf.Slices == null)
                return;
            //bigger subdivision may has the same slice structure, add only one
            if (neighbours.Contains(leaf.Header))
                return;
            //each height slice could be a neighbour
            if (leaf.Slices.Length == 1)
            {
                ulong floorSlice = leaf.Slices[0];
                ushort height = SliceAccessor.heightGrade(floorSlice);
                byte flag = SliceAccessor.flag(floorSlice);
                if (CanMove2Neighbour(setting, curH, maxH, height, ushort.MaxValue))
                {
                    neighbours.Add(leaf.Header, u, v, 0, subdivision, setting.subdivision, centerx, centerz, floorSlice);
                }
            }
            else
            {
                for (uint i = 0; i < leaf.Slices.Length - 1; ++i)
                {
                    ulong currentSlice = leaf.Slices[i];
                    byte currentf = SliceAccessor.flag(currentSlice);
                    ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                    ulong higherSlice = leaf.Slices[i + 1];
                    byte higherf = SliceAccessor.flag(higherSlice);
                    ushort higherheight = SliceAccessor.heightGrade(higherSlice);
                    if (i == leaf.Slices.Length - 2 && (higherf & SliceAccessor.SliceCeiling) == 0)
                    {//pillar roof
                        if (CanMove2Neighbour(setting, curH, maxH, higherheight, ushort.MaxValue))
                        {
                            neighbours.Add(leaf.Header, u, v, i + 1, subdivision, setting.subdivision, 
                                centerx, centerz, higherSlice);
                        }
                        break;
                    }
                    if ((currentf & SliceAccessor.SliceCeiling) > 0)
                        continue;
                    ushort currentMaxH = ushort.MaxValue;
                    if ((higherf & SliceAccessor.SliceCeiling) > 0)
                    {//check standable
                        float holeHeight = (higherheight - currentheight) * setting.heightPerGrade;
                        if (holeHeight < setting.boundHeight)
                            continue;
                        currentMaxH = higherheight;
                    }
                    if (CanMove2Neighbour(setting, curH, maxH, currentheight, currentMaxH))
                    {
                        neighbours.Add(leaf.Header, u, v, i, subdivision, setting.subdivision,
                            centerx, centerz, currentSlice);
                    }
                }
            }
        }
        //display functions
        public void GetDisplaySlice(float startx, float startz, int x, int z, List<DisplaySlice> lSlices)
        {
            //first grade
            QuadTreeBase subtree = tree[x * setting.maxZ + z];
            GetNodeDisplaySlice(subtree, startx, startz, x, z, setting.sliceSize[0], setting.sliceSize[1],
                lSlices);
        }
        private void GetNodeDisplaySlice(QuadTreeBase subtree, float startx, float startz, int x, int z, 
            float sizex, float sizez, List<DisplaySlice> lSlices)
        {
            //first grade
            if (subtree is QuadTreeLeaf)
            {
                QuadTreeLeaf leaf = (QuadTreeLeaf)subtree;
                GetLeafDisplaySlice(leaf.Slices, startx, startz, sizex, sizez, x, z, lSlices);
            }
            else
            {//sub tree node
                QuadTreeNode node = (QuadTreeNode)subtree;
                startx += sizex * x;
                startz += sizez * z;
                for (int i = 0; i < 4; ++i)
                {
                    int subx = i >> 1;
                    int subz = i & 0x00000001;
                    GetNodeDisplaySlice(node.Children[i], startx, startz, subx, subz, 
                        sizex * 0.5f, sizez * 0.5f, lSlices);
                }
            }
        }
        private void GetLeafDisplaySlice(ulong[] slices, float startx, float startz, float sizex, float sizez, int x, int z,
            List<DisplaySlice> lSlices)
        {
            float minx = startx + sizex * x;
            float minz = startz + sizez * z;
            float maxx = startx + sizex * (x + 1);
            float maxz = startz + sizez * (z + 1);
            for (int i = 0; i < slices.Length; ++i)
            {
                ulong rawSlice = slices[i];
                DisplaySlice slice = new DisplaySlice();
                slice.height = setting.heightValRange[0] +
                    SliceAccessor.heightGrade(rawSlice) * setting.heightPerGrade;
                slice.flag = SliceAccessor.flag(rawSlice);
                slice.min = new float[2] { minx, minz };
                slice.max = new float[2] { maxx, maxz };
                lSlices.Add(slice);
            }
        }
    }
}
