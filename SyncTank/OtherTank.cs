using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using ClientMsg;
using System;

public class MoveSyncData
{
    public Vector3 Position;
    public float DeltaTime;
    public float MoveAngle;
}

public class OtherTank : BaseTank
{
    private Queue<MoveSyncData> mSyncQueue = new Queue<MoveSyncData>();

    private MoveSyncData mNextSyncData = null;

    private Vector3 mTargetDir = Vector3.zero;
    private int mRevBulletId = 0;
    private float mRotateSpeed = 0f;
    private bool bIsInView = false;

    private float mPositionY = 0.0f;

    #region 处理服务器消息
    public override void HandleEndMoveMessage()
    {
        StopMoveAnimation();
        PlayBodyAnimation(0, 1.0f);
    }

    public override void HandleFireMessage(System.Object obj)
    {
        if (mTankMainGun != null)
            mTankMainGun.OnRecieveFireMessage(obj);
    }

    public override void HandleMoveMessage(System.Object obj)
    {
        TankMoveBroadResp Resp = (TankMoveBroadResp)obj;
        MoveSyncData moveSyncData = new MoveSyncData();

        //mPositionY = mPositionY < 0 ? Resp.position.y : mPositionY;

        moveSyncData.Position = new Vector3(Resp.position.x, mPositionY, Resp.position.z);
        moveSyncData.DeltaTime = Resp.stamp / 1000.0f;

        moveSyncData.MoveAngle = Resp.angle;

        PlayMoveAnimation();

        AddSyncData(moveSyncData);

        //Debug.LogError("---------move message , position : " + moveSyncData.Position + "------Angle : " + moveSyncData.MoveAngle);
    }

    public override void HandleTurretMessage(System.Object obj)
    {
        float newAngle = (float)obj;

        if (mTankTurretController != null)
        {
            mTankTurretController.ProcessSyncAngleMessage(newAngle);
        }
    }
    #endregion

    //int mCount = 0;
    private void AddSyncData(MoveSyncData moveSyncData)
    {
        //Debug.LogError("--------Player id : " + mTankData.PlayerId + "---------sync cmd queue count : " + mSyncQueue.Count + "------------");
        mSyncQueue.Enqueue(moveSyncData);
    }

	// Use this for initialization
	void Start () 
    {
	}

    private float AngleNormalize(float angle)
    {
        float outAngle = angle;

        if (outAngle > 180.0f)
        {
            outAngle -= 360.0f;
        }
        else if (outAngle < -180.0f)
        {
            outAngle += 360.0f;
        }

        return outAngle;
    }

    private float CalcSpeedParam()
    {
        if (mSyncQueue.Count <= 3) { return 1.0f; }

        //Debug.LogError("--------mSyncQueue count : " + mSyncQueue.Count + "-------------");

        float totalLeftTime = 0.0f;
        foreach (MoveSyncData syncData in mSyncQueue)
        {
            totalLeftTime += syncData.DeltaTime;
        }
        float param = totalLeftTime / 0.3f;

        return param;
    }

	// Update is called once per frame
	void Update () 
    {
        if (mSyncQueue.Count == 0 && mNextSyncData == null)
        {          
            return;
        }

        Vector3 vCurPos = transform.position;
        float nowAngle = transform.rotation.eulerAngles.y;

        if (mNextSyncData == null)
        {
            float param = CalcSpeedParam();

            mNextSyncData = mSyncQueue.Dequeue();

            Vector3 dir = mNextSyncData.Position - transform.position;      
            
            //dir.y = 0;
            
            mMoveSpeed = dir.magnitude / mNextSyncData.DeltaTime * param;

            dir.Normalize();
            mTargetDir = dir;
            float angle = mNextSyncData.MoveAngle - nowAngle;
            
            mRotateSpeed = AngleNormalize(angle) / mNextSyncData.DeltaTime;
        }

        float deltaTime = Time.deltaTime;

        float moveDis = deltaTime * mMoveSpeed;
        float rotateAngle = deltaTime * mRotateSpeed;

        float sqrMagnitude = (vCurPos - mNextSyncData.Position).sqrMagnitude;

        if (moveDis * moveDis == sqrMagnitude)
        //if (moveDis * moveDis <= sqrMagnitude && Mathf.Abs(moveDis * moveDis - sqrMagnitude) < 0.01f)
        {
            vCurPos = mNextSyncData.Position;
            nowAngle = mNextSyncData.MoveAngle;
            mNextSyncData = null;
            moveDis = 0f;
            transform.position = vCurPos;
            transform.rotation = Quaternion.Euler(0f, nowAngle, 0f);
            
            return;
        }

        while (moveDis * moveDis > sqrMagnitude)
        {
            if (mSyncQueue.Count == 0)
            {
                vCurPos = mNextSyncData.Position;
                nowAngle = mNextSyncData.MoveAngle;
                mNextSyncData = null;
                moveDis = 0f;
                transform.position = vCurPos;
                transform.rotation = Quaternion.Euler(0f, nowAngle, 0f);
                
                return;
            }
            else
            {
                Vector3 dir = mNextSyncData.Position - vCurPos;

                //dir.y = 0;
                deltaTime -= dir.magnitude / mMoveSpeed;
                vCurPos = mNextSyncData.Position;
                nowAngle = mNextSyncData.MoveAngle;
               
                float param = CalcSpeedParam();

                mNextSyncData = mSyncQueue.Dequeue();
                dir = mNextSyncData.Position - vCurPos;


                mMoveSpeed = dir.magnitude / mNextSyncData.DeltaTime * param;

                //dir.y = 0;
                dir.Normalize();

                mTargetDir = dir;
                
                float angle = mNextSyncData.MoveAngle - nowAngle;

                mRotateSpeed = AngleNormalize(angle) / mNextSyncData.DeltaTime;                
                
                moveDis = deltaTime * mMoveSpeed;
                rotateAngle = deltaTime * mRotateSpeed;
            }

            sqrMagnitude = (vCurPos - mNextSyncData.Position).sqrMagnitude;
        }

        transform.position = vCurPos + mTargetDir * moveDis;
        transform.rotation = Quaternion.Euler(0f, nowAngle + rotateAngle, 0f);

        OnTankPositionUpdate(transform.position, transform.rotation.eulerAngles.y, CameraController.Instance.GetCurrentEulerY());
        PlayMoveAnimation();
        PlayBodyAnimation((int)mMoveSpeed, 1.0f);
	}
}
