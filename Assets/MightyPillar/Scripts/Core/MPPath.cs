namespace MightyPillar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    //path node
    public class MPPathNode : MPQueueMember<MPPathNode>, IMPPoolItem
    {
        //id
        public uint UID { get { return SliceHeader + HIdx; } }
        //height slice idx
        public uint HIdx { get; private set; } 
        public int Subdivision { get; private set; }
        public uint SliceHeader { get; private set; }
        public ushort HeightGrade { get; private set; }
        public ushort Flag { get; private set; }
        //node center
        public float X { get; private set; }
        public float Z { get; private set; }
        //boundary detail cell range
        public int BoundaryXMin { get; private set; }
        public int BoundaryXMax { get; private set; }
        public int BoundaryZMin { get; private set; }
        public int BoundaryZMax { get; private set; }
        //detailed x, z
        //dx ~ (0, setting.maxX * power(2, subdivision)), dx ~ (0, setting.maxZ * power(2, subdivision))
        public void Reset(uint header, int dx, int dz, uint ih, int sub, int maxSub, float[] center, ushort hg, ushort f)
        {
            SliceHeader = header;
            HIdx = ih;
            Subdivision = sub;
            X = center[0];
            Z = center[1];
            HeightGrade = hg;
            Flag = f;
            Priority = 0;
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
        public void Reset(MPPathNode copy)
        {
            SliceHeader = copy.SliceHeader;
            HIdx = copy.HIdx;
            Subdivision = copy.Subdivision;
            X = copy.X;
            Z = copy.Z;
            HeightGrade = copy.HeightGrade;
            BoundaryXMin = copy.BoundaryXMin;
            BoundaryXMax = copy.BoundaryXMax;
            BoundaryZMin = copy.BoundaryZMin;
            BoundaryZMax = copy.BoundaryZMax;
        }
        void IMPPoolItem.Reset()
        {
            SliceHeader = uint.MaxValue;
            HIdx = 0;
            Subdivision = 0;
            X = 0;
            Z = 0;
            HeightGrade = 0;
            BoundaryXMin = 0;
            BoundaryXMax = 0;
            BoundaryZMin = 0;
            BoundaryZMax = 0;
            Priority = 0;
            Next = null;
        }
    }
    //data pool
    public class MPPathNodePool : MPDataPool<MPPathNode>
    {
        public static MPPathNode Pop(uint header, int dx, int dz, uint ih, int sub, int maxSub, float[] center, 
            ushort hg, ushort f)
        {
            MPPathNode node = null;
            if (mqPool.Count > 0)
                node = mqPool.Dequeue();
            if (node == null)
                node = new MPPathNode();
            node.Reset(header, dx, dz, ih, sub, maxSub, center, hg, f);
            return node;
        }
        public static MPPathNode Pop(MPPathNode copy)
        {
            MPPathNode node = null;
            if (mqPool.Count > 0)
                node = mqPool.Dequeue();
            if (node == null)
                node = new MPPathNode();
            node.Reset(copy);
            return node;
        }
    }
    public class MPNeigbours : MPArray<MPPathNode>
    {
        public MPNeigbours(int len) : base(len)
        { }
        public bool Contains(uint uid)
        {
            for (int i = 0; i < Length; ++i)
                if (Data[i].SliceHeader == uid)
                    return true;
            return false;
        }
    }
    //
    public static class MPAStarPath
    {
        private static Dictionary<uint, MPPathNode> _usedpathnodes = new Dictionary<uint, MPPathNode>();
        private static MPNeigbours _neighbours = new MPNeigbours(8);
        public static Queue<MPPathNode> debugnodes = new Queue<MPPathNode>();
        public static bool isDebug = false;
        public static Queue<MPPathNode> pathResult = new Queue<MPPathNode>();
        public static bool FindPath(PillarData data, MPPathNode srcNode, MPPathNode destNode)
        {
            //temp data struct
            Dictionary<uint, uint> camefrom = new Dictionary<uint, uint>();
            Dictionary<uint, float> cost_sofar = new Dictionary<uint, float>();
            MPPriorityQueue<MPPathNode> frontier = new MPPriorityQueue<MPPathNode>();
            int detail = 1 << data.setting.subdivision;
            int maxDeatailX = data.setting.maxX * detail;
            int maxDeatailZ = data.setting.maxZ * detail;
            _neighbours.Reallocate((detail + 1) * 4 * SliceAccessor.MaxHeightSliceCount); //each pillar height slice max to 64
            //has to create a copy, for mem pool clean
            MPPathNode start = MPPathNodePool.Pop(srcNode);
            frontier.Enqeue(start);
            cost_sofar.Add(start.UID, 0);
            _usedpathnodes.Add(start.UID, start);
            //
            while (!frontier.IsEmpty)
            {
                MPPathNode current = frontier.Dequeue();
                //check if I've got it
                if (current.UID == destNode.UID)
                    break;
                _neighbours.Reset();
                data.GetPathNeighbours(current, maxDeatailX, maxDeatailZ, _neighbours);
                for(int i=0; i< _neighbours.Length; ++i)
                {
                    MPPathNode n = _neighbours.Data[i];
                    //add to mem pool
                    if (!_usedpathnodes.ContainsKey(n.UID))
                        _usedpathnodes.Add(n.UID, n);
                    //add subdivision level to make path perfer bigger subdivision
                    float cost = cost_sofar[current.UID] + Math.Abs(current.X - n.X) + Math.Abs(current.Z - n.Z) +
                        Math.Abs(current.HeightGrade - n.HeightGrade) +
                        (data.setting.subdivision - n.Subdivision);
                    if (!cost_sofar.ContainsKey(n.UID) || cost < cost_sofar[n.UID])
                    {
                        cost_sofar[n.UID] = cost;
                        //add distance to goal for heuristic
                        n.Priority = cost + Math.Abs(destNode.X - n.X) + Math.Abs(destNode.Z - n.Z) +
                            Math.Abs(destNode.HeightGrade - n.HeightGrade);
                        frontier.Enqeue(n);
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
            if (camefrom.ContainsKey(destNode.UID))
            {
                //output path
                uint currentUID = destNode.UID;
                pathResult.Enqueue(_usedpathnodes[destNode.UID]);
                while (currentUID != srcNode.UID)
                {
                    if (camefrom.ContainsKey(currentUID))
                    {
                        uint from = camefrom[currentUID];
                        pathResult.Enqueue(_usedpathnodes[from]);
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
        public static void EndFindPath()
        {
            pathResult.Clear();
            debugnodes.Clear();
            isDebug = false;
            foreach (var node in _usedpathnodes.Values)
                MPPathNodePool.Push(node);
            _usedpathnodes.Clear();
        }
    }
}
