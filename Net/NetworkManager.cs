using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using LuaInterface;
using Network;
using System.IO;
using System.Net;
public class NetworkManager : MonoBehaviorSingleton<NetworkManager>
{
    NetworkSocket SocketClient = null;// new NetworkSocket();
    string mAddr = "";
    short mPort = 0;
    
    public delegate void NetMsgProcessType(object ob);
    private Dictionary<int, NetMsgProcessType> mMsgProcessMap = new Dictionary<int, NetMsgProcessType>();
    private Dictionary<int, LuaFunction> mMsgLuaProcessMap = new Dictionary<int, LuaFunction>();
    public void Register(MSG_DEFINE msg, NetMsgProcessType pt)
    {
        mMsgProcessMap[(int)msg] = pt;
    }
    public void UnRegister(MSG_DEFINE msg)
    {
        mMsgProcessMap.Remove((int)msg);
    }

    public void RegisterLuaNetMsg(int msgId, LuaFunction luaFunc)
    {
        mMsgLuaProcessMap[msgId] = luaFunc;
    }
    public void UnRegisterLuaNetMsg(int msgId)
    {
        mMsgLuaProcessMap.Remove(msgId);
    }
    public int ThreadRate
    {
        get
        {
            if (SocketClient != null)
                return SocketClient.ThreadRate;
            return 0;
        }
    }
    protected void Awake()
    {
        base.Awake();
        NetMsgTypeRegister.Register();
        gameObject.AddComponent<HeartbeatHandler>();
    }
    protected void OnDestroy()
    {
        if (SocketClient != null)
        {
            SocketClient.Close();
        }
        base.OnDestroy();
    }


    public void OnConnectSucc()
    {
        NetworkMessageManager.Instance.OnConnectOK();
        //NetworkManager.AddEvent((int)DefaultMessageIDTypes.ID_CONNECTION_REQUEST_ACCEPTED, new ByteBuffer());
    }

    /// <summary>
    /// 连接失败
    /// </summary>
    public void OnConnectFailed()
    {
        Debug.LogError("ConnectSuccCallBack");
        //NetworkManager.AddEvent((int)DefaultMessageIDTypes.ID_CONNECTION_ATTEMPT_FAILED, new ByteBuffer());
    }

    public void Connect(string addr, short port)
    {
        mAddr = addr;
        mPort = port;
        StartCoroutine("DoConnect");
    }
    IEnumerator DoConnect()
    {
        if(SocketClient != null)
        {
            SocketClient.Close();
        }
        SocketClient = new NetworkSocket();
        SocketClient.SetIP(mAddr, mPort);
        SocketClient.OnLostConnectCallBack = this.DisConnectCallBack;
        SocketClient.OnPacket = this.OnPacket;

        SocketClient.Connect();
        while (SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECTING)
        {
            yield return null;
        }
        if (SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECT)
        {
            OnConnectSucc();
        }
        else if (SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECTFAILED)
        {
            OnConnectFailed();
        }
    }

    bool IsReconnect = false;
    public void Reconnect()
    {
        if (IsReconnect)
            return;
        StartCoroutine(DoReconnect());  
    }
    IEnumerator DoReconnect()
    {
        IsReconnect = true;
        yield return new WaitForSeconds(0.1f);

        int nReconnectCount = 0;
        while (nReconnectCount < 5)
        {
            nReconnectCount++;
            SocketClient.Connect();
            while (SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECTING)
            {
                yield return null;
            }
            if (SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECT)
            {
                NetworkMessageManager.Instance.OnConnectOK();
                break;
            }
        }
        //超过重连次数的处理
    }
    /// <summary>
    /// 断开连接
    /// </summary>
    public void DisConnectCallBack()
    {
        //NetworkManager.AddEvent((int)DefaultMessageIDTypes.ID_DISCONNECTION_NOTIFICATION, new ByteBuffer());
    }

    public void OnReconnect()
    {
        Debug.Log("重新连接。。。。。。。");
        //NetworkSocket.OnSetState(NetworkSocket.ConnectState.STATE_CONNECTING);
    }


    /// <summary>
    /// 判断请求的是否是同一个地址和端口
    /// </summary>
    /// <param name="addr">ip地址</param>
    /// <param name="port">端口</param>
    /// <returns></returns>
    public bool IsSameIpAndPort(string addr, short port)
    {
        return SocketClient.IsSameIpAndPort(addr, port);
    }

    /// <summary>
    /// 发送消息接口
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="msgId"></param>
    public void SendMsg(ref ByteBuffer buffer, short msgId)
    {
        SocketClient.SendMsg(buffer.ToBytes());
    }

    byte[] sendbt = new byte[512];
    public void SendMsg(MSG_DEFINE id, object ob)
    {
        short msgId = (short)id;
        int len = 0;
        if (ob != null)
            len = ProtoBufSerialize.Instance.Serialize(ob, sendbt);
        SocketClient.SendMsg(msgId, sendbt, len);
    }

    public bool IsConnect()
    {
        return SocketClient != null && SocketClient.LinkState == NetworkSocket.ConnectState.STATE_CONNECT;
    }

    private void OnPacket(int msgId, byte[] data)
    {
    //    Debug.Log("msg Id :" + msgId + "   data Length :" + data.Length);
        NetMsgProcessType pt = null;
        if (mMsgProcessMap.TryGetValue(msgId, out pt))
        {
            pt(data == null ? null : ProtoBufSerialize.Instance.DeSerialize(msgId, data));
        }
        else
        {
            MessageProcess.Instance.OnHandleMessage(msgId, ref data);
        }
        LuaFunction func = null;
        if(mMsgLuaProcessMap.TryGetValue(msgId, out func))
        {
            LuaStringBuffer luab = null;
            if (data != null)
                luab = new LuaStringBuffer(data);
            else
                luab = new LuaStringBuffer(new byte[0]);
            func.Call(luab);
        }
    }
    

    void Update()
    {
        if(SocketClient != null)
            SocketClient.Update();
    }
}
