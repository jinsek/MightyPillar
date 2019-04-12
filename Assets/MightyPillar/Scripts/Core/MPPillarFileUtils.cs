namespace MightyPillar
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class OrderedSlices : List<RawSlice>
    {
        static int CompareSlice(RawSlice a, RawSlice b)
        {
            if (a.height < b.height ||
                (a.height == b.height && a.flag < b.flag))
                return -1;
            return 1;
        }
        public uint HashValue = 0;
        public void SortSlices()
        {
            Sort(CompareSlice);
        }
        public bool IsEqual(OrderedSlices other)
        {
            if (this.Count != other.Count || HashValue != other.HashValue)
                return false;
            return true;
        }
        public void Unify(float startHeight, float heightPerGrade)
        {
            SortSlices();
            if (Count == 0)
                MPLog.LogError("pillar is empty.");
            //merge the slices, slices should be floor|ceiling|floor|ceiling....|floor
            bool bNeedMerge = true;
            while (bNeedMerge && Count > 0)
            {
                bNeedMerge = false;
                for (int i = 0; i < Count - 1; ++i)
                {
                    if (this[i].flag == this[i+1].flag)
                    {
                        if ((this[i].flag & SliceAccessor.SliceCeiling) > 0)
                        {//ceiling use lower one
                            RemoveAt(i + 1);
                        }
                        else
                        {//floor use higher one
                            RemoveAt(i);
                        }
                        bNeedMerge = true;
                        break;
                    }
                }
            }
            HashValue = 0;
            for (int i=0; i<Count; ++i)
            {
                RawSlice slice = this[i];
                slice.heightGrade = (ushort)Math.Floor((slice.height - startHeight)/ heightPerGrade);
                uint hash = slice.heightGrade;
                HashValue += (hash << 16) | slice.flag;
            }
        }
    }
    public class PillarQuadTree
    {
        public PillarQuadTree[] Children { get; private set; }
        public OrderedSlices Slices { get; private set; }
        public bool IsLeaf
        {
            get
            {
                return Children == null;
            }
        }
        public PillarQuadTree(int depth)
        {
            if (depth == 0)
                return;
            Children = new PillarQuadTree[4];
            BuildTree(depth);
        }
        private void BuildTree(int depth)
        {
            for (int i = 0; i < 4; ++i)
            {
                Children[i] = new PillarQuadTree(depth - 1);
            }
        }
        private bool IsEqual(PillarQuadTree other)
        {
            if (IsLeaf && other.IsLeaf)
            {
                if (Slices.IsEqual(other.Slices))
                    return true;
            }
            return false;
        }
        //x ~ (0, setting.maxX * power(2, subdivision)), x ~ (0, setting.maxZ * power(2, subdivision))
        public void AddPillar(int subdivision, int x, int z, OrderedSlices slices)
        {
            //first grade
            int u = x >> subdivision; // x / power(2, subdivision);
            int v = z >> subdivision;
            if (subdivision == 0)
            {
                Slices = slices;
            }
            else
            {
                int subx = x - u * (1 << subdivision);
                int subz = z - v * (1 << subdivision);
                --subdivision;
                Children[(subx >> subdivision) * 2 + (subz >> subdivision)].AddPillar(subdivision, subx, subz, slices);
            }
        }
        public void UnifySlice(float startHeight, float heightPerGrade)
        {
            if (Slices != null)
            {
                Slices.Unify(startHeight, heightPerGrade);
            }
            if (Children != null)
            {
                foreach (var child in Children)
                    child.UnifySlice(startHeight, heightPerGrade);
            }
        }
        public string GetDebugOutput()
        {
            if (IsLeaf)
                return Slices.Count.ToString();
            else
            {
                string s = "";
                foreach (var child in Children)
                    s += child.GetDebugOutput();
                return s;
            }
        }
        public void CombineTree()
        {
            if (IsLeaf)
                return;
            bool isChildrenAllLeaf = true;
            foreach (var child in Children)
            {
                if (!child.IsLeaf)
                    child.CombineTree();
                if (!child.IsLeaf)
                    isChildrenAllLeaf = false;
            }
            if (!isChildrenAllLeaf)
                return;
            for (int i = 0; i < Children.Length - 1; ++i)
            {
                if (!Children[i].IsEqual(Children[i + 1]))
                    return;
            }
            Slices = Children[0].Slices;
            Children = null;
        }
        public uint GetChildMask()
        {
            uint mask = 0;
            if (Children == null)
                return mask;
            if (!Children[0].IsLeaf) mask |= 1;
            if (!Children[1].IsLeaf) mask |= 2;
            if (!Children[2].IsLeaf) mask |= 4;
            if (!Children[3].IsLeaf) mask |= 8;
            return mask;
        }
        private void FillLeaf(List<uint> lTrees, List<uint> lSlices)
        {
            uint count = (uint)Math.Min(byte.MaxValue, Slices.Count);
            if (((lSlices.Count + count) & 0xff000000) > 0)
            {
                MPLog.LogError("slice data overflow, buff len : " + lSlices.Count + ", current slice : " + count);
                return;
            }
            uint val = count << 26 | ((uint)lSlices.Count & 0x03ffffff);
            lTrees.Add(val);
            for (int i = 0; i < Slices.Count; ++i)
            {
                uint rawSlice = Slices[i].heightGrade;
                rawSlice <<= 16;
                rawSlice = rawSlice | (uint)(Slices[i].flag & 0x0000ffff);
                lSlices.Add(rawSlice);
            }
        }
        public void GetData(List<uint> lTrees, List<uint> lSlices)
        {
            if (IsLeaf)
            {
                FillLeaf(lTrees, lSlices);
            }
            else
            {
                Queue<PillarQuadTree> qSubs = new Queue<PillarQuadTree>();
                uint subOffset = (uint)(lTrees.Count + 1);
                uint mask = GetChildMask();
                uint val = subOffset << 4 | (mask & 0x0000000f);
                lTrees.Add(val);
                qSubs.Enqueue(this);
                while(qSubs.Count > 0)
                {
                    PillarQuadTree sub = qSubs.Dequeue();
                    foreach (var child in sub.Children)
                    {
                        if (child.IsLeaf)
                            child.FillLeaf(lTrees, lSlices);
                        else
                        {
                            qSubs.Enqueue(child);
                            subOffset += 4;
                            mask = child.GetChildMask();
                            val = subOffset << 4 | (mask & 0x0000000f);
                            lTrees.Add(val);
                        }
                    }
                }
            }
        }
    }
    public static class MPFileUtil
    {
        public static bool SaveData(string path, PillarSetting setting,  PillarQuadTree[] trees)
        {
            if (File.Exists(path))
                File.Delete(path);
            FileStream stream = File.Open(path, FileMode.Create);
            byte[] stbuff = setting.ToArray();
            stream.Write(stbuff, 0, stbuff.Length);
            List<uint> lTrees = new List<uint>();
            List<uint> lSlices = new List<uint>();
            //header
            for (int x = 0; x < setting.maxX; ++x)
            {
                for (int z = 0; z < setting.maxZ; ++z)
                {
                    PillarQuadTree tree = trees[x * setting.maxZ + z];
                    uint uheader = (uint)(lTrees.Count) << 5;
                    if (!tree.IsLeaf)
                    {
                        uint mask = tree.GetChildMask();
                        mask |= 0x00000010;
                        uheader |= mask & 0x0000001f;
                    }
                    tree.GetData(lTrees, lSlices);
                    byte[] buff = BitConverter.GetBytes(uheader);
                    stream.Write(buff, 0, buff.Length);
                }
            }
            //quadtree
            byte[] sizeBuff = BitConverter.GetBytes(lTrees.Count);
            stream.Write(sizeBuff, 0, sizeBuff.Length);
            foreach (var val in lTrees)
            {
                byte[] buff = BitConverter.GetBytes(val);
                stream.Write(buff, 0, buff.Length);
            }
            //slices
            sizeBuff = BitConverter.GetBytes(lSlices.Count);
            stream.Write(sizeBuff, 0, sizeBuff.Length);
            foreach (var val in lSlices)
            {
                byte[] buff = BitConverter.GetBytes(val);
                stream.Write(buff, 0, buff.Length);
            }
            stream.Close();
            MPLog.Log("create data successed!");
            return true;
        }
        public static PillarRawData LoadData(string path, string dataName)
        {
            FileStream stream = File.Open(path, FileMode.Open);
            PillarRawData data = new PillarRawData();
            data.DataName = dataName;
            int readOffset = 0;
            //read setting
            int settingSize = data.setting.byteSize();
            if (stream.Length - readOffset >= settingSize)
            {
                byte[] buff = new byte[settingSize];
                int len = stream.Read(buff, readOffset, settingSize);
                data.setting.Reset(buff);
                readOffset += len;
            }
            else
            {
                MPLog.LogError("load setting failed");
                return null;
            }
            //read header
            int headerSize = data.setting.maxX * data.setting.maxZ;
            if (stream.Length - readOffset < headerSize * sizeof(uint))
            {
                MPLog.LogError("load header failed");
                return null;
            }
            data.header = new uint[headerSize];
            byte[] uBuff = new byte[sizeof(uint)];
            int readLen = 0;
            while (readLen < headerSize)
            {
                int len = stream.Read(uBuff, 0, sizeof(uint));
                uint val = BitConverter.ToUInt32(uBuff, 0);
                data.header[readLen] = val;
                ++readLen;
            }
            //read quad tree
            stream.Read(uBuff, 0, sizeof(uint));
            uint quadtreeSize = BitConverter.ToUInt32(uBuff, 0);
            data.quadtree = new uint[quadtreeSize];
            readLen = 0;
            while (readLen < quadtreeSize)
            {
                int len = stream.Read(uBuff, 0, sizeof(uint));
                uint val = BitConverter.ToUInt32(uBuff, 0);
                data.quadtree[readLen] = val;
                ++readLen;
            }
            //read slices
            stream.Read(uBuff, 0, sizeof(uint));
            uint heightSliceSize = BitConverter.ToUInt32(uBuff, 0);
            data.slices = new uint[heightSliceSize];
            readLen = 0;
            while (readLen < heightSliceSize)
            {
                int len = stream.Read(uBuff, 0, sizeof(uint));
                uint val = BitConverter.ToUInt32(uBuff, 0);
                data.slices[readLen] = val;
                ++readLen;
            }
            MPLog.Log("load successed !");
            stream.Close();
            return data;
        }
    }
}
