using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MightyPillar;

internal class debugPlane : IMPPoolItem
{
    private GameObject mGo;
    public debugPlane(GameObject go)
    {
        mGo = go;
    }
    public void Reset(MPDebugPlane plane)
    {
        mGo.SetActive(true);
        mGo.transform.position = plane.center;
        mGo.transform.localScale = plane.size;
    }
    void IMPPoolItem.Reset()
    {
        mGo.SetActive(false);
    }
}

internal class debugPlanePool : MPDataPool<debugPlane>
{
    public static debugPlane Pop(UnityEngine.Object template, MPDebugPlane plane)
    {
        debugPlane d = null;
        if (mqPool.Count > 0)
            d = mqPool.Dequeue();
        if (d == null)
        {
            GameObject go = MonoBehaviour.Instantiate(template) as GameObject;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            d = new debugPlane(go);
        }
        d.Reset(plane);
        return d;
    }
}

public class MPDemo : MonoBehaviour
{
    public float BoundHeight = 1f;
    public float JumpableHeight = 5f;
    public UnityEngine.Object Anchor;
    public UnityEngine.Object DebugQuad;
    public float MoveSpeed = 10f;
    private byte mMoveFlag = 0;
    private RaycastHit mHitCache = new RaycastHit();
    private GameObject mAnchorGo;
    private PillarRawData mPillar;
    private Stack<MPPathResult> mCurrentPath = new Stack<MPPathResult>();
    private debugPlanePool mdebugPlanePool = new debugPlanePool();
    private Queue<debugPlane> mactiveplances = new Queue<debugPlane>();
    // Start is called before the first frame update
    void Start()
    {
        MPDataCreator creator = gameObject.GetComponent<MPDataCreator>();
        if (creator != null)
        {
            string path = string.Format("{0}/MightyPillar/Resources/{1}.bytes", Application.dataPath, creator.DataName);
            mPillar = MPFileUtil.LoadData(path, creator.DataName);
            mPillar.setting.boundHeight = BoundHeight;
            mPillar.setting.jumpableHeight = JumpableHeight;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Camera.main == null)
            return;
        if (Input.GetKeyDown("w"))
        {
            mMoveFlag |= 0x01;
        }
        if (Input.GetKeyUp("w"))
        {
            mMoveFlag &= 0xfe;
        }
        if (Input.GetKeyDown("s"))
        {
            mMoveFlag |= 0x02;
        }
        if (Input.GetKeyUp("s"))
        {
            mMoveFlag &= 0xfd;
        }
        if (Input.GetKeyDown("d"))
        {
            mMoveFlag |= 0x04;
        }
        if (Input.GetKeyUp("d"))
        {
            mMoveFlag &= 0xfb;
        }
        if (Input.GetKeyDown("a"))
        {
            mMoveFlag |= 0x08;
        }
        if (Input.GetKeyUp("a"))
        {
            mMoveFlag &= 0xf7;
        }
        if (mMoveFlag > 0)
        {
            Vector3 delta = Vector3.zero;
            if ((mMoveFlag & 0x01) > 0)
                delta += Vector3.forward;
            if ((mMoveFlag & 0x02) > 0)
                delta += Vector3.back;
            if ((mMoveFlag & 0x04) > 0)
                delta += Vector3.right;
            if ((mMoveFlag & 0x08) > 0)
                delta += Vector3.left;
            Camera.main.transform.position += delta;
        }
        UpdatePath();
        if (Input.GetMouseButtonDown(0) && Anchor != null)
        {//spawn or move anchor
            Ray checkRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(checkRay, out mHitCache))
            {
                if (mAnchorGo == null)
                    mAnchorGo = Instantiate(Anchor) as GameObject;
                mAnchorGo.transform.position = mHitCache.point + mAnchorGo.transform.localScale.y * Vector3.up;
            }
        }
        if (Input.GetMouseButtonDown(1) && mAnchorGo != null && mPillar != null && mCurrentPath.Count == 0)
        {//move to destination
            Ray checkRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(checkRay, out mHitCache))
            {
                if (DebugQuad != null)
                {
                    while (mactiveplances.Count > 0)
                    {
                        debugPlanePool.Push(mactiveplances.Dequeue());
                    }
                    Queue<MPDebugPlane> qDebug = new Queue<MPDebugPlane>();
                    MPDataAccessor.FindPath(mPillar, mAnchorGo.transform.position, mHitCache.point, mCurrentPath, qDebug);
                    while(qDebug.Count > 0)
                    {
                        debugPlane p = debugPlanePool.Pop(DebugQuad, qDebug.Dequeue());
                        mactiveplances.Enqueue(p);
                    }
                }
                else
                    MPDataAccessor.FindPath(mPillar, mAnchorGo.transform.position, mHitCache.point, mCurrentPath);
            }
        }
    }
    private void UpdatePath()
    {
        if (mCurrentPath.Count == 0)
        {
            return;
        }
        while(mCurrentPath.Count > 0)
        {
            MPPathResult node = mCurrentPath.Peek();
            Vector3 dis = node.Pos - mAnchorGo.transform.position;
            float moveMag = Time.deltaTime * MoveSpeed;
            if (dis.magnitude < moveMag)
            {
                if (mCurrentPath.Count == 1)
                    mAnchorGo.transform.position = node.Pos;
                node = mCurrentPath.Pop();
                MPPathResultPool.Push(node);
            }
            else
            {
                mAnchorGo.transform.position += dis.normalized * moveMag;
                break;
            }
        }
    }
}
