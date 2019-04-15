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
    public void DestorySelf()
    {
        MonoBehaviour.Destroy(mGo);
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
            go.hideFlags = HideFlags.HideInHierarchy;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            d = new debugPlane(go);
        }
        d.Reset(plane);
        return d;
    }
    public static void Clear()
    {
        while(mqPool.Count > 0)
        {
            debugPlane plane = mqPool.Dequeue();
            plane.DestorySelf();
        }
    }
}

public class MPDemo : MonoBehaviour
{
    public float BoundHeight = 1f;
    public float JumpableHeight = 5f;
    public UnityEngine.Object Anchor;
    public UnityEngine.Object DebugQuad;
    public UnityEngine.Object DynamicObstacle;
    public float MoveSpeed = 10f;
    private byte mMoveFlag = 0;
    private bool mbAddObject = false;
    private BoxCollider mPendingObstacle;
    private RaycastHit mHitCache = new RaycastHit();
    private GameObject mAnchorGo;
    private PillarData mPillar;
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
    private void OnDestroy()
    {
        debugPlanePool.Clear();
    }

    // Update is called once per frame
    void Update()
    {
        if (Camera.main == null)
            return;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            mMoveFlag |= 0x01;
        }
        if (Input.GetKeyUp(KeyCode.W) || Input.GetKeyUp(KeyCode.UpArrow))
        {
            mMoveFlag &= 0xfe;
        }
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            mMoveFlag |= 0x02;
        }
        if (Input.GetKeyUp(KeyCode.S) || Input.GetKeyUp(KeyCode.DownArrow))
        {
            mMoveFlag &= 0xfd;
        }
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            mMoveFlag |= 0x04;
        }
        if (Input.GetKeyUp(KeyCode.D) || Input.GetKeyUp(KeyCode.RightArrow))
        {
            mMoveFlag &= 0xfb;
        }
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            mMoveFlag |= 0x08;
        }
        if (Input.GetKeyUp(KeyCode.A) || Input.GetKeyUp(KeyCode.LeftArrow))
        {
            mMoveFlag &= 0xf7;
        }
        if (Input.GetKeyDown(KeyCode.LeftShift))
            mbAddObject = true;
        if (Input.GetKeyUp(KeyCode.LeftShift))
            mbAddObject = false;
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
        if (Input.mouseScrollDelta.y != 0)
        {
            Camera.main.transform.position += Input.mouseScrollDelta.y * Camera.main.transform.forward;
        }
        UpdatePath();
        UpdateDynamicObstacle();
        if (Input.GetMouseButtonDown(0) && !mbAddObject && Anchor != null)
        {//spawn or move anchor
            Ray checkRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(checkRay, out mHitCache))
            {
                if (mAnchorGo == null)
                    mAnchorGo = Instantiate(Anchor) as GameObject;
                mAnchorGo.transform.position = mHitCache.point;
            }
        }
        if (Input.GetMouseButtonDown(0) && mbAddObject && DynamicObstacle != null)
        {//spawn or move anchor
            Ray checkRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(checkRay, out mHitCache))
            {
                GameObject go = Instantiate(DynamicObstacle) as GameObject;
                go.transform.position = mHitCache.point;
                mPendingObstacle = go.GetComponentInChildren<BoxCollider>();
                //add obstacles in next frame otherwise we can't physics cast it
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
    private void UpdateDynamicObstacle()
    {
        if (mPendingObstacle != null)
        {
            MPDataAccessor.DynamicAddPillar(mPillar, mPendingObstacle.transform.position, mPendingObstacle.bounds);
            mPendingObstacle = null;
        }
    }
}
