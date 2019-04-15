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
        //volumn center
        public float[] center;
        //total height grades
        public float hegithPerGrade;
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
            return sizeof(int) * 3 + sizeof(float) * 8;
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
            AssginBytes(BitConverter.GetBytes(center[0]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(center[1]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(center[2]), arr, ref offset);
            AssginBytes(BitConverter.GetBytes(hegithPerGrade), arr, ref offset);
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
            center = new float[3];
            center[0] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            center[1] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            center[2] = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
            hegithPerGrade = BitConverter.ToSingle(data, offset);
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
        public const ushort SliceCeiling = 1;
        public static ushort heightGrade(uint val)
        {
            return (ushort)(val >> 16);
        }
        public static ushort flag(uint val)
        {
            return (ushort)(val & 0x0000ffff);
        }
        public static float heightVal(float minHeight, float hegithPerGrade, uint val)
        {
            ushort grade = (ushort)(val >> 8);
            return minHeight + grade * hegithPerGrade;
        }
        public static float heightVal(PillarSetting setting, uint val)
        {
            ushort grade = (ushort)(val >> 8);
            return setting.heightValRange[0] + grade * setting.hegithPerGrade;
        }
    }
    //data structures for data creating
    public class RawSlice
    {
        public float height = 0;
        public ushort flag = 0;
        public ushort heightGrade = 0;
    }
    //data structure for data display
    public class DisplaySlice
    {
        public float height;
        public float[] min;
        public float[] max;
        public ushort flag;
    }
    //data container
    internal class HeightSlicePool
    {
        private static Dictionary<int, Queue<uint[]>> mPools = new Dictionary<int, Queue<uint[]>>();
        private static Dictionary<uint,uint[]> mSlices = new Dictionary<uint, uint[]>();
        public static uint[] GetSlices(uint header)
        {
            if (mSlices.ContainsKey(header))
                return mSlices[header];
            return null;
        }
        public static uint[] Pop(uint header, int len)
        {
            uint[] item = null;
            if (mPools.ContainsKey(len) && mPools[len].Count > 0)
                item = mPools[len].Dequeue();
            if (item == null)
                item = new uint[len];
            mSlices.Add(header, item);
            return item;
        }
        public static void Push(uint header, uint[] slices)
        {
            if (slices.Length == 0)
                return;
            if (!mPools.ContainsKey(slices.Length))
                mPools.Add(slices.Length, new Queue<uint[]>());
            mPools[slices.Length].Enqueue(slices);
            if (mSlices.ContainsKey(header))
                mSlices.Remove(header);
        }
    }
    //internal class QuadTreeNodePool : MPDataPool<QuadTreeNode> { }
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
        public uint[] Slices { get; private set; }
        public uint HashVal { get; protected set; }
        public uint Header { get; private set; }
        public QuadTreeLeaf()
        {
            Header = uint.MaxValue;
            Slices = null;
            HashVal = 0;
        }
        public void Reset(int len, uint hash)
        {
            if (_uid_seed + len >= uint.MaxValue)
                _uid_seed = 0;
            _uid_seed += (uint)len;
            Header = _uid_seed;
            Slices = HeightSlicePool.Pop(Header, len);
            HashVal = hash;
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
    }
    public class QuadTreeNode : QuadTreeBase
    {
        public override string DebugOutput
        {
            get
            {
                string s = "";
                foreach (var child in Children)
                    s += child.DebugOutput;
                return s;
            }
        }
        public bool IsCombinableLeaf
        {
            get
            {
                for (int i = 0; i < Children.Length - 1; ++i)
                {
                    if (!Children[i].IsEqual(Children[i + 1]))
                        return false;
                }
                return true;
            }
        }
        public QuadTreeBase[] Children = new QuadTreeBase[4];
        public override bool IsEqual(QuadTreeBase other)
        {
            return false;
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
                    uint rawSlice = rawSlices[i].heightGrade;
                    rawSlice <<= 16;
                    rawSlice = rawSlice | (uint)(rawSlices[i].flag & 0x0000ffff);
                    leaf.Slices[i] = rawSlice;
                }
            }
        }
        private void SubdividLeaf(int idx)
        {
            QuadTreeLeaf leaf = (QuadTreeLeaf)Children[idx];
            QuadTreeNode node = CreateSubTree(0);
            for (int i = 0; i < 4; ++i)
            {
                QuadTreeLeaf childLeaf = (QuadTreeLeaf)node.Children[i];
                childLeaf.Reset(leaf.Slices.Length, leaf.HashVal);
                Array.Copy(leaf.Slices, childLeaf.Slices, leaf.Slices.Length);
            }
            HeightSlicePool.Push(leaf.Header, leaf.Slices);
            Children[idx] = node;
        }
        public void CombineTree()
        {
            for (int i=0; i<4; ++i)
            {
                if (Children[i] is QuadTreeNode)
                {
                    QuadTreeNode node = (QuadTreeNode)Children[i];
                    node.CombineTree();
                    if (node.IsCombinableLeaf)
                    {
                        Children[i] = (QuadTreeLeaf)node.Children[0];
                        for (int cid = 1; cid < 4; ++cid)
                        {
                            QuadTreeLeaf leaf = (QuadTreeLeaf)node.Children[cid];
                            HeightSlicePool.Push(leaf.Header, leaf.Slices);
                        }
                    }
                }
            }
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
        public QuadTreeLeaf GetLeaf(int x, int z, float sizex, float sizez,  float[] center, ref int subdivision)
        {
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            //center pos
            center[0] += sizex * u;
            center[1] += sizez * v;
            //
            int idx = u * 2 + v;
            QuadTreeBase subtree = Children[idx];
            if (subtree is QuadTreeLeaf)
            {
                center[0] += sizex * 0.5f;
                center[1] += sizex * 0.5f;
                return (QuadTreeLeaf)subtree;
            }
            else
            {//sub tree node
                int detail = 1 << subdivision;
                int subx = x - u * detail;
                int subz = z - v * detail;
                subdivision -= 1;
                QuadTreeNode node = (QuadTreeNode)subtree;
                return node.GetLeaf(subx, subz, sizex * 0.5f, sizez * 0.5f, center, ref subdivision);
            }
        }
    }
    public class PillarData
    {
        public string DataName = "";
        public PillarSetting setting = new PillarSetting();
        public QuadTreeBase[] tree;
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public QuadTreeLeaf GetLeaf(int x, int z, float[] center, ref int subdivision)
        {
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            int idx = u * setting.maxZ + v;
            if (idx > tree.Length)
                return null;
            QuadTreeBase subtree = tree[idx];
            //center pos
            center[0] = setting.sliceSize[0] * u;
            center[1] = setting.sliceSize[1] * v;
            //
            if (subtree is QuadTreeLeaf)
            {
                center[0] += setting.sliceSize[0] * 0.5f;
                center[1] += setting.sliceSize[1] * 0.5f;
                return (QuadTreeLeaf)subtree;
            }
            else
            {//sub tree node
                int detail = 1 << subdivision;
                int subx = x - u * detail;
                int subz = z - v * detail;
                subdivision -= 1;
                QuadTreeNode node = (QuadTreeNode)subtree;
                return node.GetLeaf(subx, subz, setting.sliceSize[0] * 0.5f, setting.sliceSize[1] * 0.5f, center,
                    ref subdivision);
            }
        }
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public MPPathNode GetStandablePathNode(int u, int v, ushort refH)
        {
            float[] center = new float[2] { 0, 0 };
            int subdivision = setting.subdivision;
            QuadTreeLeaf leaf = GetLeaf(u, v, center, ref subdivision);
            if (leaf == null || leaf.Slices == null)
                return null;
            for (uint i = (uint)leaf.Slices.Length - 1; i > 0; --i)
            {
                uint currentSlice = leaf.Slices[i];
                ushort currentf = SliceAccessor.flag(currentSlice);
                ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                uint lowerSlice = leaf.Slices[i - 1];
                ushort lowerf = SliceAccessor.flag(lowerSlice);
                ushort lowerheight = SliceAccessor.heightGrade(lowerSlice);
                if ((currentf & SliceAccessor.SliceCeiling) > 0)
                    continue;
                if (refH >= currentheight ||
                    ((lowerf & SliceAccessor.SliceCeiling) > 0 && refH >= lowerheight))
                {//pillar roof
                    return MPPathNodePool.Pop(leaf.Header, u, v, i, subdivision, setting.subdivision, center, 
                        currentheight, currentf);
                }
            }
            uint floorSlice = leaf.Slices[0];
            ushort floorf = SliceAccessor.flag(floorSlice);
            ushort floorheight = SliceAccessor.heightGrade(floorSlice);
            return MPPathNodePool.Pop(leaf.Header, u, v, 0, subdivision, setting.subdivision,
                center, floorheight, floorf);
        }
        protected virtual bool CanMove2Neighbour(PillarSetting setting, ushort curH, ushort curMaxH,
            ushort neighbourH, ushort neighbourMaxH)
        {
            //jumpable
            if (Math.Abs(curH - neighbourH) * setting.hegithPerGrade > setting.jumpableHeight)
                return false;
            //gap like this, cant go
            // _____|
            //      _______
            //      |
            // _____|  
            if (neighbourH > curH)
            {
                float gapHeight = (curMaxH - neighbourH) * setting.hegithPerGrade;
                if (gapHeight >= setting.boundHeight)
                    return true;
            }
            else
            {
                if (neighbourMaxH > curH)
                {
                    float gapHeight = (neighbourMaxH - curH) * setting.hegithPerGrade;
                    if (gapHeight >= setting.boundHeight)
                        return true;
                }
            }
            return false;
        }
        public void GetPathNeighbours(MPPathNode current, int maxX, int maxZ, MPNeigbours neighbours)
        {
            uint[] curSlices = HeightSlicePool.GetSlices(current.SliceHeader);
            if (curSlices == null)
                return;
            //get current ceiling, where I can't go across
            ushort maxH = ushort.MaxValue;
            if (current.HIdx < curSlices.Length - 1)
            {
                uint higherSlice = curSlices[current.HIdx + 1];
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
            float[] center = new float[2] { 0, 0 };
            int subdivision = setting.subdivision;
            QuadTreeLeaf leaf = GetLeaf(u, v, center, ref subdivision);
            if (leaf == null || leaf.Slices == null)
                return;
            //bigger subdivision may has the same slice structure, add only one
            if (neighbours.Contains(leaf.Header))
                return;
            //each height slice could be a neighbour
            if (leaf.Slices.Length == 1)
            {
                uint floorSlice = leaf.Slices[0];
                ushort height = SliceAccessor.heightGrade(floorSlice);
                ushort flag = SliceAccessor.flag(floorSlice);
                if (CanMove2Neighbour(setting, curH, maxH, height, ushort.MaxValue))
                {
                    MPPathNode floor = MPPathNodePool.Pop(leaf.Header, u, v, 0, subdivision,
                        setting.subdivision, center, height, flag);
                    neighbours.Add(floor);
                }
            }
            else
            {
                for (uint i = 0; i < leaf.Slices.Length - 1; ++i)
                {
                    uint currentSlice = leaf.Slices[i];
                    ushort currentf = SliceAccessor.flag(currentSlice);
                    ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                    uint higherSlice = leaf.Slices[i + 1];
                    ushort higherf = SliceAccessor.flag(higherSlice);
                    ushort higherheight = SliceAccessor.heightGrade(higherSlice);
                    if (i == leaf.Slices.Length - 2 && (higherf & SliceAccessor.SliceCeiling) == 0)
                    {//pillar roof
                        if (CanMove2Neighbour(setting, curH, maxH, higherheight, ushort.MaxValue))
                        {
                            MPPathNode roof = MPPathNodePool.Pop(leaf.Header, u, v, i + 1,
                                subdivision, setting.subdivision, center, higherheight, higherf);
                            neighbours.Add(roof);
                        }
                    }
                    if ((currentf & SliceAccessor.SliceCeiling) > 0)
                        continue;
                    ushort currentMaxH = ushort.MaxValue;
                    if ((higherf & SliceAccessor.SliceCeiling) > 0)
                    {//check standable
                        float holeHeight = (higherheight - currentheight) * setting.hegithPerGrade;
                        if (holeHeight < setting.boundHeight)
                            continue;
                        currentMaxH = higherheight;
                    }
                    if (CanMove2Neighbour(setting, curH, maxH, currentheight, currentMaxH))
                    {
                        MPPathNode step = MPPathNodePool.Pop(leaf.Header, u, v, i,
                            subdivision, setting.subdivision, center, currentheight, currentf);
                        neighbours.Add(step);
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
        private void GetLeafDisplaySlice(uint[] slices, float startx, float startz, float sizex, float sizez, int x, int z,
            List<DisplaySlice> lSlices)
        {
            float minx = startx + sizex * x;
            float minz = startz + sizez * z;
            float maxx = startx + sizex * (x + 1);
            float maxz = startz + sizez * (z + 1);
            for (int i = 0; i < slices.Length; ++i)
            {
                uint rawSlice = slices[i];
                DisplaySlice slice = new DisplaySlice();
                slice.height = setting.heightValRange[0] +
                    SliceAccessor.heightGrade(rawSlice) * setting.hegithPerGrade;
                slice.flag = SliceAccessor.flag(rawSlice);
                slice.min = new float[2] { minx, minz };
                slice.max = new float[2] { maxx, maxz };
                lSlices.Add(slice);
            }
        }
    }
}
