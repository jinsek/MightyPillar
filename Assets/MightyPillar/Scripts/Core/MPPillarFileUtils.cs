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
        public ulong HashValue = 0;
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
                slice.heightGrade = (ushort)Math.Ceiling((slice.height - startHeight)/ heightPerGrade);
                HashValue += SliceAccessor.packVal(slice.heightGrade, 0, 0, slice.flag);
            }
        }
    }
    public class QuadTreeNodeSerializable : QuadTreeNode
    {
        public QuadTreeNodeSerializable(FileStream stream)
        {
            //tree mask
            byte[] bBuff = new byte[1];
            stream.Read(bBuff, 0, 1);
            Deserialize(bBuff[0], stream);
        }
        public QuadTreeNodeSerializable(int len) : base(len)
        {}
        public QuadTreeNodeSerializable(byte mask, FileStream stream)
        {
            Deserialize(mask, stream);
        }
        protected override QuadTreeNode CreateSubTree(int sub) { return new QuadTreeNodeSerializable(sub); }
        protected override QuadTreeLeaf CreateLeaf() { return new QuadTreeLeafSerializable(); }
        private void Deserialize(byte mask, FileStream stream)
        {

            for (int i = 0; i < 4; ++i)
            {
                byte childMask = (byte)(1 << i);
                if ((mask & childMask) > 0)
                    Children[i] = new QuadTreeNodeSerializable(stream);
                else
                    Children[i] = new QuadTreeLeafSerializable(stream);
            }
        }
        public void Serialize(FileStream stream)
        {
            byte[] bBuff = new byte[1] { GetChildMask() };
            stream.Write(bBuff, 0, 1);
            foreach(var child in Children)
            {
                if (child is QuadTreeNodeSerializable)
                    ((QuadTreeNodeSerializable)child).Serialize(stream);
                else if (child is QuadTreeLeafSerializable)
                    ((QuadTreeLeafSerializable)child).Serialize(stream);
            }
        }
    }
    public class QuadTreeLeafSerializable : QuadTreeLeaf
    {
        public QuadTreeLeafSerializable(FileStream stream) : base()
        {
            //slice count
            byte[] bBuff = new byte[1];
            byte[] uBuff = new byte[sizeof(ulong)];
            stream.Read(bBuff, 0, 1);
            Reset(bBuff[0], 0);
            for(int i=0; i< Slices.Length; ++i)
            {
                stream.Read(uBuff, 0, sizeof(ulong));
                Slices[i] = BitConverter.ToUInt64(uBuff, 0);
                HashVal += Slices[i];
            }
        }
        public QuadTreeLeafSerializable() : base()
        {}
        public void Serialize(FileStream stream)
        {
            byte[] bBuff = new byte[1] { 0 };
            if (Slices != null)
                bBuff[0] = (byte)Slices.Length;
            stream.Write(bBuff, 0, 1);
            if (Slices == null)
                return;
            foreach (var slice in Slices)
            {
                byte[] uBuff = BitConverter.GetBytes(slice);
                stream.Write(uBuff, 0, uBuff.Length);
            }
        }
    }
    public static class MPFileUtil
    {
        public static bool SaveData(string path, PillarSetting setting, QuadTreeBase[] trees)
        {
            if (File.Exists(path))
                File.Delete(path);
            FileStream stream = File.Open(path, FileMode.Create);
            byte[] stbuff = setting.ToArray();
            stream.Write(stbuff, 0, stbuff.Length);
            //trees
            byte[] bRootLeafBuff = new byte[1] { 1 << 4 };
            for (int x = 0; x < setting.maxX; ++x)
            {
                for (int z = 0; z < setting.maxZ; ++z)
                {
                    QuadTreeBase subTree = trees[x * setting.maxZ + z];
                    if (subTree is QuadTreeLeafSerializable)
                    {
                        stream.Write(bRootLeafBuff, 0, 1);
                        QuadTreeLeafSerializable leaf = (QuadTreeLeafSerializable)subTree;
                        leaf.Serialize(stream);
                    }
                    else
                    {
                        QuadTreeNodeSerializable node = (QuadTreeNodeSerializable)subTree;
                        node.Serialize(stream);
                    }
                }
            }
            stream.Close();
            MPLog.Log("create data successed!");
            return true;
        }
        public static PillarData LoadData(string path, string dataName)
        {
            FileStream stream = File.Open(path, FileMode.Open);
            PillarData data = new PillarData();
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
            data.tree = new QuadTreeBase[headerSize];
            byte[] bBuff = new byte[1] { 0 };
            byte bRootLeafBuff = 1 << 4;
            for (int i=0; i<headerSize; ++i)
            {
                //root mask
                stream.Read(bBuff, 0, 1);
                //
                if ((bBuff[0] & bRootLeafBuff) > 0)
                {
                    data.tree[i] = new QuadTreeLeafSerializable(stream);
                }
                else
                {
                    data.tree[i] = new QuadTreeNodeSerializable(bBuff[0], stream);
                }
            }
            MPLog.Log("load successed !");
            stream.Close();
            return data;
        }
    }
}
