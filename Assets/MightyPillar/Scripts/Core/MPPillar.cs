//MIGHTY PILLAR created by chenshi, jinsekcs@163.com
//Pillar is the base element of the mighty-pillar tool-set
//1. One pillar is a set of space slices following Y axis which represent the height information of ground/obstacles.
//2. The first grade horizontal cells are actually root nodes of a quad tree, which subdivided the cells to get more 
//accurate pillars.The data header is an x * z int array, each int value defined as:
//  |-- 27 bit : pointer to quadtree
//  |-- 1 bit : tree node a single leaf node, 0, single leaf, 1, tree node
//  |-- 4 bit : tree mask, 0 means leaf children, 1 means sub node. 0000 means this is a leaf node 
//3. The quad tree data defined as 3 types:
// if tree mask is single leaf node, first grade leaf:
//  |-- 6 bit : count of slices
//  |-- 26 bit : slices offset (max to 8192 * 8192)
// else then, there should be 4 continue in to define the children
//   if tree mask is 0, this is a leaf:
//     |-- 6 bit : count of slices
//     |-- 26 bit : slices offset (max to 8192 * 8192)
//   else if tree mask is 1, this is a subnode:
//     |-- 28 bit : pointer to subtree
//     |-- 4 bit : tree mask, 0 means leaf children, 1 means sub node.
//4. data structure for slices:
//     |-- 2 BYTE : height value
//     |-- 2 BYTE : ground logic bit flag, 0 -- ground, 0x0001 -- ceiling

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
    public class PillarRawData
    {
        public string DataName = "";
        public PillarSetting setting = new PillarSetting();
        public uint[] header;
        public uint[] quadtree;
        public uint[] slices;
    }
    public static class PillarContainer
    {
        private static Dictionary<string, PillarRawData> mDatas = new Dictionary<string, PillarRawData>();
        public static bool Contains(string dataName)
        {
            return mDatas.ContainsKey(dataName);
        }
        public static PillarRawData GetData(string dataName)
        {
            if (mDatas.ContainsKey(dataName))
                return mDatas[dataName];
            return null;
        }
        public static bool AddData(PillarRawData data)
        {
            if (!mDatas.ContainsKey(data.DataName))
            {
                mDatas.Add(data.DataName, data);
                return true;
            }
            return false;
        }
    }
    //data acccessor
    public class QuadTreeAccessor
    {
        private static bool parseHeader(PillarRawData data, int u, int v,
            ref uint headerVal, ref int treeAddr, ref bool singleLeaf, ref byte mask)
        {
            int idx = u * data.setting.maxZ + v;
            if (idx >= data.header.Length)
            {
                MPLog.LogError(string.Format("parseHeader header idx overflow u : {0} v : {1}", u, v));
                return false;
            }
            headerVal = data.header[idx];
            treeAddr = (int)(headerVal >> 5);
            singleLeaf = (headerVal & 0x00000010) == 0;
            mask = (byte)(headerVal & 0x000000f);
            return true;
        }
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public static uint getSliceHeader(PillarRawData data, int x, int z, float[] center, ref int subdivision)
        {
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            uint headerVal = 0;
            int treeAddr = 0;
            bool singleLeaf = false;
            byte mask = 0;
            if (!parseHeader(data, u, v, ref headerVal, ref treeAddr, ref singleLeaf, ref mask))
            {
                MPLog.LogError("getSliceHeader quadtree idx overflow");
                return uint.MaxValue;
            }
            uint treeVal = data.quadtree[treeAddr];
            //center pos
            center[0] = data.setting.sliceSize[0] * u;
            center[1] = data.setting.sliceSize[1] * v;
            //
            if (singleLeaf)
            {
                center[0] += data.setting.sliceSize[0] * 0.5f;
                center[1] += data.setting.sliceSize[1] * 0.5f;
                return treeVal;
            }
            else
            {//sub tree node
                int childrenAddr = (int)(treeVal >> 4);
                byte submask = (byte)(treeVal & 0x000000f);
                int detail = 1 << subdivision;
                int subx = x - u * detail;
                int subz = z - v * detail;
                subdivision -= 1;
                return getSliceTreeNode(data, childrenAddr, submask, subx, subz, ref subdivision, center);
            }
        }
        private static uint getSliceTreeNode(PillarRawData data, int childrenAddr, byte mask, int x, int z, 
            ref int subdivision, float[] center)
        {
            if (subdivision < 0)
            {
                MPLog.LogError("getSliceTreeNode subdivision out of range");
                return uint.MaxValue;
            }
            int u = x >> subdivision; // 0 or 1
            int v = z >> subdivision; // 0 or 1
            if (u > 1 || v > 1)
            {
                MPLog.LogError("getSliceTreeNode u,v out of range");
            }
            int childOffset = u * 2 + v;
            int childmask = 1 << childOffset;
            if (childrenAddr + childOffset >= data.quadtree.Length)
            {
                MPLog.LogError("getSliceTreeNode quadtree idx overflow");
                return uint.MaxValue;
            }
            uint treeVal = data.quadtree[childrenAddr + childOffset];
            //center pos
            float subsize = data.setting.sliceSize[0] / (1 << (data.setting.subdivision - subdivision));
            center[0] += subsize * u;
            center[1] += subsize * v;
            //
            if ((mask & childmask) == 0)
            {
                center[0] += subsize * 0.5f;
                center[1] += subsize * 0.5f;
                return treeVal;
            }
            //this is sub tree
            int subAddr = (int)(treeVal >> 4);
            byte submask = (byte)(treeVal & 0x000000f);
            int detail = 1 << subdivision;
            int subx = x - u * detail;
            int subz = z - v * detail;
            subdivision -= 1;
            return getSliceTreeNode(data, subAddr, submask, subx, subz, ref subdivision, center);
        }
        //display data
        //x ~ (0, setting.maxX), x ~ (0, setting.maxZ)
        public static void getDisplaySlice(PillarRawData data, float startx, float startz, int x, int z, List<DisplaySlice> lSlices)
        {
            //first grade
            uint headerVal = data.header[x * data.setting.maxZ + z];
            int treeAddr = (int)(headerVal >> 5);
            bool singleLeaf = (headerVal & 0x00000010) == 0;
            byte mask = (byte)(headerVal & 0x000000f);
            uint treeVal = data.quadtree[treeAddr];
            if (singleLeaf)
            {
                GetDisplaySlice(data.setting, data.slices, treeVal, startx, startz,
                    data.setting.sliceSize[0], data.setting.sliceSize[1], 0, 0, lSlices);
            }
            else
            {//sub tree node
                int childrenAddr = (int)(treeVal >> 4);
                byte submask = (byte)(treeVal & 0x000000f);
                getSubDisplaySlice(data, childrenAddr, submask, startx, startz,
                    data.setting.sliceSize[0] * 0.5f, data.setting.sliceSize[1] * 0.5f, lSlices);
            }
        }
        private static void getSubDisplaySlice(PillarRawData data, int childrenAddr, byte mask,
            float startx, float startz, float sizex, float sizez, List<DisplaySlice> lSlices)
        {
            for (int i=0; i<4; ++i)
            {
                uint treeVal = data.quadtree[childrenAddr + i];
                int subx = i >> 1;
                int subz = i & 0x00000001;
                if (mask == 0 || (mask & (1 << i)) == 0)
                {
                    GetDisplaySlice(data.setting, data.slices, treeVal, startx, startz, sizex, sizez, subx, subz, lSlices);
                }
                else
                {
                    int subAddr = (int)(treeVal >> 4);
                    byte submask = (byte)(treeVal & 0x000000f);
                    getSubDisplaySlice(data, subAddr, submask, startx + subx * sizex, startz + subz * sizez,
                        sizex * 0.5f, sizez * 0.5f, lSlices);
                }
            }
        }
        private static void GetDisplaySlice(PillarSetting setting, uint[] slices, uint sliceHeader, 
            float startx, float startz, float sizex, float sizez, int x, int z, 
            List<DisplaySlice> lSlices)
        {
            int sliceCount = (int)(sliceHeader >> 26);
            int sliceOffset = (int)(sliceHeader & 0x03ffffff);
            float minx = startx + sizex * x;
            float minz = startz + sizez * z;
            float maxx = startx + sizex * (x + 1);
            float maxz = startz + sizez * (z + 1);
            for (int i = 0; i < sliceCount; ++i)
            {
                if (sliceOffset + i >= slices.Length)
                {
                    break;
                }
                uint rawSlice = slices[sliceOffset + i];
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
    public static class SliceAccessor
    {
        public const float minSliceThickness = 0.001f;
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

    public static class PillarAccessor
    {
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        private static void getSliceHeader(PillarRawData data, int x, int z, float[] center, ref int subdivision,
            out uint sliceHeader, out int sliceCount, out int sliceOffset)
        {
            sliceHeader = QuadTreeAccessor.getSliceHeader(data, x, z, center, ref subdivision);
            sliceCount = (int)(sliceHeader >> 26);
            sliceOffset = (int)(sliceHeader & 0x03ffffff);
        }
        private static void getSliceHeader(uint sliceHeader, out int sliceCount, out int sliceOffset)
        {
            sliceCount = (int)(sliceHeader >> 26);
            sliceOffset = (int)(sliceHeader & 0x03ffffff);
        }
        //slice data accssors
        //retrieve neighbour for path-finding
        public static void getPathNeighbours(PillarRawData data, MPPathNode current, int maxX, int maxZ, MPNeigbours neighbours)
        {
            uint sliceHeader = current.SliceHeader;
            int sliceCount = 0;
            int offset = 0;
            getSliceHeader(sliceHeader, out sliceCount, out offset);
            if (sliceCount == 0)
                return;
            //get current ceiling, where I can't go across
            ushort maxH = ushort.MaxValue;
            if (current.HIdx < sliceCount - 1)
            {
                uint higherSlice = data.slices[offset + current.HIdx + 1];
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
                    getPathNeighbour(data, current.HeightGrade, maxH, u, current.BoundaryZMin, neighbours);
                if (current.BoundaryZMax >= 0 && current.BoundaryZMax < maxZ)
                    getPathNeighbour(data, current.HeightGrade, maxH, u, current.BoundaryZMax, neighbours);
            }
            for (int v = current.BoundaryZMin; v <= current.BoundaryZMax; ++v)
            {
                if (v < 0 || v >= maxZ)
                    continue;
                if (current.BoundaryXMin >= 0 && current.BoundaryXMin < maxX)
                    getPathNeighbour(data, current.HeightGrade, maxH, current.BoundaryXMin, v, neighbours);
                if (current.BoundaryXMax >= 0 && current.BoundaryXMax < maxX)
                    getPathNeighbour(data, current.HeightGrade, maxH, current.BoundaryXMax, v, neighbours);
            }
        }
        private static void getPathNeighbour(PillarRawData data, ushort curH, ushort maxH, int u, int v, 
            MPNeigbours neighbours)
        {
            float[] center = new float[2] { 0, 0 };
            int subdivision = data.setting.subdivision;
            uint neighbourSliceHeader = QuadTreeAccessor.getSliceHeader(data, u, v, center, ref subdivision);
            if (neighbourSliceHeader == uint.MaxValue)
                return;
            //bigger subdivision may has the same slice structure, add only one
            if (neighbours.Contains(neighbourSliceHeader))
                return;
            //y direction get node
            int sliceCount = 0;
            int offset = 0;
            getSliceHeader(neighbourSliceHeader, out sliceCount, out offset);
            if (sliceCount == 0)
                return;
            //each height slice could be a neighbour
            if (sliceCount == 1)
            {
                uint floorSlice = data.slices[offset];
                ushort height = SliceAccessor.heightGrade(floorSlice);
                ushort flag = SliceAccessor.flag(floorSlice);
                if (canMove2Neighbour(data.setting, curH, maxH, height, ushort.MaxValue))
                {
                    MPPathNode floor = MPPathNodePool.Pop(neighbourSliceHeader, u, v, 0, subdivision, 
                        data.setting.subdivision, center, height, flag);
                    neighbours.Add(floor);
                }
            }
            else
            {
                for (uint i = 0; i < sliceCount - 1; ++i)
                {
                    uint currentSlice = data.slices[offset + i];
                    ushort currentf = SliceAccessor.flag(currentSlice);
                    ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                    uint higherSlice = data.slices[offset + i + 1];
                    ushort higherf = SliceAccessor.flag(higherSlice);
                    ushort higherheight = SliceAccessor.heightGrade(higherSlice);
                    if (i == sliceCount - 2 && (higherf & SliceAccessor.SliceCeiling) == 0)
                    {//pillar roof
                        if (canMove2Neighbour(data.setting, curH, maxH, higherheight, ushort.MaxValue))
                        {
                            MPPathNode roof = MPPathNodePool.Pop(neighbourSliceHeader, u, v, i + 1, 
                                subdivision, data.setting.subdivision, center, higherheight, higherf);
                            neighbours.Add(roof);
                        }
                    }
                    if ((currentf & SliceAccessor.SliceCeiling) > 0)
                        continue;
                    ushort currentMaxH = ushort.MaxValue;
                    if ((higherf & SliceAccessor.SliceCeiling) > 0)
                    {//check standable
                        float holeHeight = (higherheight - currentheight) * data.setting.hegithPerGrade;
                        if (holeHeight < data.setting.boundHeight)
                            continue;
                        currentMaxH = higherheight;
                    }
                    if (canMove2Neighbour(data.setting, curH, maxH, currentheight, currentMaxH))
                    {
                        MPPathNode step = MPPathNodePool.Pop(neighbourSliceHeader, u, v, i, 
                            subdivision, data.setting.subdivision, center, currentheight, currentf);
                        neighbours.Add(step);
                    }
                }
            }
        }
        //x ~ (0, setting.maxX * power(2, subdivision)), z ~ (0, setting.maxZ * power(2, subdivision))
        public static MPPathNode getStandablePathNode(PillarRawData data, int u, int v, ushort refH)
        {
            float[] center = new float[2] { 0, 0 };
            int subdivision = data.setting.subdivision;
            uint sliceHeader = QuadTreeAccessor.getSliceHeader(data, u, v, center, ref subdivision);
            int sliceCount = 0;
            int offset = 0;
            getSliceHeader(sliceHeader, out sliceCount, out offset);
            if (sliceCount == 0)
                return null;
            if (sliceCount > 1)
            {
                for (uint i = (uint)sliceCount - 1; i > 0; --i)
                {
                    uint currentSlice = data.slices[offset + i];
                    ushort currentf = SliceAccessor.flag(currentSlice);
                    ushort currentheight = SliceAccessor.heightGrade(currentSlice);
                    uint lowerSlice = data.slices[offset + i - 1];
                    ushort lowerf = SliceAccessor.flag(lowerSlice);
                    ushort lowerheight = SliceAccessor.heightGrade(lowerSlice);
                    if ((currentf & SliceAccessor.SliceCeiling) > 0)
                        continue;
                    if (refH >= currentheight ||
                        ((lowerf & SliceAccessor.SliceCeiling) > 0 && refH >= lowerheight))
                    {//pillar roof
                        return MPPathNodePool.Pop(sliceHeader, u, v, i, subdivision,
                            data.setting.subdivision, center, currentheight, currentf);
                    }
                }
            }
            uint floorSlice = data.slices[offset];
            ushort floorf = SliceAccessor.flag(floorSlice);
            ushort floorheight = SliceAccessor.heightGrade(floorSlice);
            return MPPathNodePool.Pop(sliceHeader, u, v, 0, subdivision, data.setting.subdivision, 
                center, floorheight, floorf);
        }
        
        private static bool canMove2Neighbour(PillarSetting setting, ushort curH, ushort curMaxH, 
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
    }
}
