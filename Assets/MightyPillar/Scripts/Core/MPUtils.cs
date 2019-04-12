namespace MightyPillar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    //data pools
    public interface IMPPoolItem
    {
        void Reset();
    }

    public class MPDataPool<T> where T : IMPPoolItem
    {
        protected static Queue<T> mqPool = new Queue<T>();
        public static void Push(T item)
        {
            if (item == null)
                return;
            item.Reset();
            mqPool.Enqueue(item);
        }
    }
    //array
    public class MPArray<T>
    {
        public T[] Data;
        public int Length { get; private set; }
        public MPArray(int len)
        {
            Reallocate(len);
        }
        public void Reallocate(int len)
        {
            if (Data != null && len < Data.Length)
                return;
            Data = new T[len];
            Length = 0;
        }
        public void Reset()
        {
            Length = 0;
        }
        public void Add(T item)
        {
            if (Data == null || Length >= Data.Length)
            {
                MPLog.LogError("MPArray overflow : " + typeof(T));
            }
            Data[Length] = item;
            ++Length;
        }
    }
    //PriorityQueue
    public class MPQueueMember<T>
    {
        public float Priority = 0;
        public T Next;
    }
    public class MPPriorityQueue<T> where T : MPQueueMember<T>
    {
        protected T mHead;
        public bool IsEmpty
        {
            get { return mHead == null; }
        }
        public T Peek()
        {
            return mHead;
        }
        public T Dequeue()
        {
            T item = mHead;
            mHead = item.Next;
            item.Next = null;
            return item;
        }
        public void Enqeue(T item)
        {
            if (mHead == null)
            {
                mHead = item;
                return;
            }
            else if (item.Priority <= mHead.Priority)
            {
                item.Next = mHead;
                mHead = item;
                return;
            }
            else if (mHead.Next == null)
            {
                mHead.Next = item;
                return;
            }
            T check = mHead;
            while (check.Next != null)
            {
                if (item.Priority <= check.Next.Priority)
                {
                    item.Next = check.Next;
                    check.Next = item;
                    return;
                }
                else
                {
                    check = check.Next;
                }
            }
            if (check.Next == null)
                check.Next = item;
            else
                MPLog.LogError("item is not add into queue");
        }
    }
}
