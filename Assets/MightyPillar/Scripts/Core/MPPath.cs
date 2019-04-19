namespace MightyPillar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    //path node
    public class MPPathNode : IMPPoolItem
    {
        //id
        public uint UID { get { return SliceHeader + HIdx; } }
        //height slice idx
        public uint HIdx { get; private set; } 
        public int Subdivision { get; private set; }
        public uint SliceHeader { get; private set; }
        public ushort HeightGrade { get; private set; }
        public byte Flag { get; private set; }
        //node center
        public float X { get; private set; }
        public float Z { get; private set; }
        //boundary detail cell range
        public int BoundaryXMin { get; private set; }
        public int BoundaryXMax { get; private set; }
        public int BoundaryZMin { get; private set; }
        public int BoundaryZMax { get; private set; }
        private float mSlopeU = 0;
        private float mSlopeV = 0;
        //detailed x, z
        //dx ~ (0, setting.maxX * power(2, subdivision)), dx ~ (0, setting.maxZ * power(2, subdivision))
        public void Reset(uint header, int dx, int dz, uint ih, int sub, int maxSub, 
            float centerx, float centerz,ulong val)
        {
            SliceHeader = header;
            HIdx = ih;
            Subdivision = sub;
            X = centerx;
            Z = centerz;
            HeightGrade = SliceAccessor.heightGrade(val);
            Flag = SliceAccessor.flag(val);
            mSlopeU = SliceAccessor.slopeUGrade(val) / SliceAccessor.slopeMagnify;
            mSlopeV = SliceAccessor.slopeVGrade(val) / SliceAccessor.slopeMagnify;
            //boundary
            BoundaryXMin = 0;
            BoundaryZMin = 0;
            int detail = 1 << sub;
            if (sub == 0)
            {
                BoundaryXMin = dx;
                BoundaryZMin = dz;
            }
            else
            {
                for (int s = maxSub; s >= sub; --s)
                {
                    detail = 1 << s;
                    int u = dx >> s; // x / power(2, subdivision);
                    int v = dz >> s;
                    BoundaryXMin += u * detail;
                    BoundaryZMin += v * detail;
                    dx -= u * detail;
                    dz -= v * detail;
                }
            }
            BoundaryXMax = BoundaryXMin + detail;
            BoundaryZMax = BoundaryZMin + detail;
            BoundaryXMin -= 1;
            BoundaryZMin -= 1;
        }
        public virtual float GetCost(MPPathNode to)
        { 
            float cost = Math.Abs(HeightGrade - to.HeightGrade);
            cost += Math.Abs(X - to.X) + Math.Abs(Z - to.Z) + Math.Max(0, Subdivision - to.Subdivision);
            return cost;
        }
        void IMPPoolItem.Reset()
        {
            SliceHeader = uint.MaxValue;
            HIdx = 0;
            Subdivision = 0;
            X = 0;
            Z = 0;
            HeightGrade = 0;
            Flag = 0;
            mSlopeU = 0;
            mSlopeV = 0;
            BoundaryXMin = 0;
            BoundaryXMax = 0;
            BoundaryZMin = 0;
            BoundaryZMax = 0;
        }
    }
    //data pool
    internal class MPPathNodePool : MPDataPool<MPPathNode>
    {
        public static void AllocatePool(int len)
        {
            for (int i = 0; i < len; ++i)
                mqPool.Enqueue(new MPPathNode());
        }
        public static MPPathNode Pop(uint header, int dx, int dz, uint ih, int sub, int maxSub, 
            float centerx, float centerz, ulong val)
        {
            MPPathNode node = null;
            if (mqPool.Count > 0)
                node = mqPool.Dequeue();
            if (node == null)
                node = new MPPathNode();
            node.Reset(header, dx, dz, ih, sub, maxSub, centerx, centerz, val);
            return node;
        }
    }
    //prior queue member
    internal class MPFrontierNode : MPQueueMember<MPFrontierNode>, IMPPoolItem
    {
        public MPPathNode Node { get; set; }
        public MPFrontierNode()
        {
            Priority = 0;
        }
        void IMPPoolItem.Reset()
        {
            Priority = 0;
            Next = null;
        }
    }
    internal class MPFrontierPool : MPDataPool<MPFrontierNode>
    {
        public static MPFrontierNode Pop(MPPathNode p)
        {
            MPFrontierNode node = null;
            if (mqPool.Count > 0)
                node = mqPool.Dequeue();
            if (node == null)
                node = new MPFrontierNode();
            node.Node = p;
            return node;
        }
    }
    internal class MPFrontier : MPPriorityQueue<MPFrontierNode>
    {
        public void Clear()
        {
            MPFrontierNode curNode = mHead;
            while (curNode != null)
            {
                MPFrontierNode removal = curNode;
                curNode = curNode.Next;
                MPFrontierPool.Push(removal);
            }
            mHead = null;
        }
    }
    //neighbour
    public class MPNeigbours : MPArray<MPPathNode>
    {
        public Dictionary<uint, MPPathNode> PathNodeCache;
        public MPNeigbours(int len) : base(len)
        { }
        public bool Contains(uint uid)
        {
            for (int i = 0; i < Length; ++i)
                if (Data[i].SliceHeader == uid)
                    return true;
            return false;
        }
        public void Add(uint header, int dx, int dz, uint ih, int sub, int maxSub, float centerx, float centerz, ulong val)
        {
            uint uid = header + ih;
            if (PathNodeCache != null && PathNodeCache.ContainsKey(uid))
            {
                Add(PathNodeCache[uid]);
            }
            else
            {
                MPPathNode node = MPPathNodePool.Pop(header, dx, dz, ih, sub, maxSub, centerx, centerz, val);
                Add(node);
                //add to mem pool
                if (PathNodeCache != null)
                    PathNodeCache.Add(node.UID, node);
            }
        }
    }
    //
    public class MPAStarPath
    {
        //debug
        public Queue<MPPathNode> debugnodes = new Queue<MPPathNode>();
        public bool isDebug = false;
        public Queue<MPPathNode> pathResult = new Queue<MPPathNode>();
        //
        private Dictionary<uint, MPPathNode> mPathNodeCache = new Dictionary<uint, MPPathNode>();
        private MPNeigbours mNeighbourCache;
        private MPFrontier mFrontier = new MPFrontier();
        protected PillarData mData;
        //translated setting
        protected int mDetail = 0;
        protected int mMaxDeatailX = 0;
        protected int mMaxDeatailZ = 0;
        protected float mVolumeSizeX = 0;
        protected float mVolumeSizeZ = 0;
        protected float mDetailGridX = 0;
        protected float mDetailGridZ = 0;
        protected float VolumeCeiling { get { return mData.setting.heightValRange[1]; } }
        protected float VolumeFloor { get { return mData.setting.heightValRange[0]; } }
        protected float VolumeHSliceT { get { return mData.setting.heightPerGrade; } }
        protected float VolumeCenterX { get { return mData.setting.center[0]; } }
        protected float VolumeCenterZ { get { return mData.setting.center[2]; } }
        protected float GridX { get { return mData.setting.sliceSize[0]; } }
        protected float GridZ { get { return mData.setting.sliceSize[1]; } }
        //temp data struct
        Dictionary<uint, uint> camefrom = new Dictionary<uint, uint>();
        Dictionary<uint, float> cost_sofar = new Dictionary<uint, float>();
        //
        public MPAStarPath(PillarData data)
        {
            mData = data;
            mDetail = 1 << data.setting.subdivision;
            mMaxDeatailX = data.setting.maxX * mDetail;
            mMaxDeatailZ = data.setting.maxZ * mDetail;
            mVolumeSizeX = data.setting.maxX * GridX;
            mVolumeSizeZ = data.setting.maxZ * GridZ;
            mDetailGridX = GridX / mDetail;
            mDetailGridZ = GridZ / mDetail;
            //each pillar height slice max to 64
            mNeighbourCache = new MPNeigbours((mDetail + 1) * 4 * SliceAccessor.MaxHeightSliceCount);
            MPPathNodePool.AllocatePool(128);
        }
        protected virtual bool FindPath(MPPathNode srcNode, MPPathNode destNode)
        {
            camefrom.Clear();
            cost_sofar.Clear();
            //has to create a copy, for mem pool clean
            mFrontier.Enqueue(MPFrontierPool.Pop(srcNode));
            cost_sofar.Add(srcNode.UID, 0);
            mPathNodeCache.Add(srcNode.UID, srcNode);
            mPathNodeCache.Add(destNode.UID, destNode);
            mNeighbourCache.PathNodeCache = mPathNodeCache;
            //
            while (!mFrontier.IsEmpty)
            {
                MPFrontierNode frontier = mFrontier.Dequeue();
                MPPathNode current = frontier.Node;
                MPFrontierPool.Push(frontier);
                //check if I've got it
                if (current.UID == destNode.UID)
                    break;
                mNeighbourCache.Reset();
                mData.GetPathNeighbours(current, mMaxDeatailX, mMaxDeatailZ, mNeighbourCache);
                for (int i=0; i< mNeighbourCache.Length; ++i)
                {
                    MPPathNode n = mNeighbourCache.Data[i];
                    //add subdivision level to make path perfer bigger subdivision
                    float cost = cost_sofar[current.UID] + current.GetCost(n);
                    if (!cost_sofar.ContainsKey(n.UID) || cost < cost_sofar[n.UID])
                    {
                        cost_sofar[n.UID] = cost;
                        //add distance to goal for heuristic
                        MPFrontierNode next = MPFrontierPool.Pop(n);
                        next.Priority = cost + destNode.GetCost(next.Node);
                        mFrontier.Enqueue(next);
                        if (isDebug)
                            debugnodes.Enqueue(n);
                        if (camefrom.ContainsKey(n.UID))
                        {
                            camefrom[n.UID] = current.UID;
                        }
                        else
                            camefrom.Add(n.UID, current.UID);
                    }
                }
            }
            mFrontier.Clear();
            if (camefrom.ContainsKey(destNode.UID))
            {
                //output path
                uint currentUID = destNode.UID;
                pathResult.Enqueue(mPathNodeCache[destNode.UID]);
                while (currentUID != srcNode.UID)
                {
                    if (camefrom.ContainsKey(currentUID))
                    {
                        uint from = camefrom[currentUID];
                        pathResult.Enqueue(mPathNodeCache[from]);
                        currentUID = from;
                    }
                    else
                    {
                        break;
                    }
                }
                return true;
            }
            return false;

        }
        public void EndFindPath()
        {
            pathResult.Clear();
            debugnodes.Clear();
            isDebug = false;
            foreach (var node in mPathNodeCache.Values)
                MPPathNodePool.Push(node);
            mPathNodeCache.Clear();
        }
    }
}
