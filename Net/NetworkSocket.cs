using UnityEngine;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System;
using Network;
using System.IO;

/*TCP网络连接*/

public class NetworkSocket 
{
    public enum ConnectState
    {
        STATE_NOTCONNECT,
        STATE_CONNECTING,
        STATE_CONNECTFAILED,
        STATE_CONNECT,
        STATE_LOSTCONNECT,
        STATE_EXIT,
    }

    public delegate void LinkCallBack();
    public LinkCallBack OnLostConnectCallBack = null;
    BinaryReader reader;
    MemoryStream memStream = null;
    byte []mSendBuffer = new byte[512];
    bool mIsMayRead = false;
    public delegate void MsgDispatchType(int msgId, byte[] data);
    public MsgDispatchType OnPacket;
    public ConnectState LinkState
    {
        get { return mState; }
    }
    ConnectState mState = ConnectState.STATE_NOTCONNECT;
    //public void Update();

    public int ThreadRate = 0;
    bool mIsExit = false;
    private Socket mSocket = null;
    private byte[] buffer = new byte[512];
    private string mIPAddress = "";
    private short mPort = 0;
 
    private Thread mThread = null;
    long _sendBits = 0;
    long _recvBits = 0;
    long mSendBits
    {
        set
        {
            _sendBits = value;
            GameEvent evt = EventDispatcher.Instance.GetEvent((int)(EventID.Print_Net_Msg));
            evt.SetArg<long>(0, _sendBits);
            evt.SetArg<long>(1, _recvBits);
            EventDispatcher.Instance.Dispatch((int)(EventID.Print_Net_Msg));
        }
        get { return _sendBits; }
    }
    long mRecvBits
    {
        set
        {
            _recvBits = value;
            GameEvent evt = EventDispatcher.Instance.GetEvent((int)(EventID.Print_Net_Msg));
            evt.SetArg<long>(0, _sendBits);
            evt.SetArg<long>(1, _recvBits);
            EventDispatcher.Instance.Dispatch((int)(EventID.Print_Net_Msg));
        }
        get { return _recvBits; }
    }
    public NetworkSocket()
    {
        memStream = new MemoryStream();
        reader = new BinaryReader(memStream);
        mThread = new Thread(this.OnRecvData);
        mThread.Priority = System.Threading.ThreadPriority.AboveNormal;
        mThread.Start();
    }

    public void SetIP(string ip, int port)
    {
        if (IsSameIpAndPort(ip,port) && mSocket != null && mSocket.Connected)
        {
            Debugger.LogError("重复登录。。。。。");
        }
        mIPAddress = ip;
        mPort = (short)port;
    }
    public void Connect()
    {
        if (mState == ConnectState.STATE_CONNECTING)
        {
            return;
        }
        ConnectState oldState = mState;
        mState = ConnectState.STATE_CONNECTING;
        if (oldState == ConnectState.STATE_CONNECT)
        {
            mSocket.Shutdown(SocketShutdown.Both);
            mSocket.Disconnect(true);
        }
        mSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        //mSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.SendTimeout, 10);
        mSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        mSocket.BeginConnect(mIPAddress, mPort, OnConnectCallBack, null);
    }
    void OnConnectCallBack(IAsyncResult ar)
    {
        if (!ar.IsCompleted)
            return;
        if (mSocket.Connected)
        {
            mState = ConnectState.STATE_CONNECT;
        }
        else
        {
            mState = ConnectState.STATE_CONNECTFAILED;
        }
    }

    public bool IsConnect()
    {
        return mSocket.Connected;
    }

    public bool IsSameIpAndPort(string ip, int port)
    {
        return string.Equals(ip, mIPAddress) && (port == mPort);
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public void Close()
    {
        OnLostConnectCallBack = null;
        OnPacket = null;
        if (mSocket != null )
        {
            if (mSocket.Connected)
            {
                mSocket.Shutdown(SocketShutdown.Both);
                mSocket.Disconnect(true);
            }
            mSocket.Close();
        }
        mIsExit = true;
        mThread.Abort();
    }
    private long RemainingBytes()
    {
        return memStream.Length - memStream.Position;
    }

    public void Update()
    {
        if (!mIsMayRead)
            return;
        
        lock (memStream)
        {
            memStream.Seek(0, SeekOrigin.Begin);
            while (RemainingBytes() > 3 && !mIsExit)
            {
                short messageLen = reader.ReadInt16();
                messageLen = IPAddress.NetworkToHostOrder(messageLen);

                int readLenth = messageLen - 2;
                if (RemainingBytes() >= readLenth)
                {
                    mRecvBits += messageLen;
                    short msgId = reader.ReadInt16();
                    msgId = IPAddress.NetworkToHostOrder(msgId);
                    if (readLenth > 2)
                        OnPacket(msgId, reader.ReadBytes(readLenth-2));
                    else
                        OnPacket(msgId, new byte[0]);
                }
                else
                {
                    memStream.Position -= 2;
                    break;
                }
            }
            if (memStream.Position > 0)
            {
                if(RemainingBytes() == 0)
                {
                    memStream.SetLength(0);     //Clear
                }
                else
                {
                    //创建一个新的
                    byte[] leftover = reader.ReadBytes((int)RemainingBytes());
                    memStream.SetLength(0);     //Clear
                    memStream.Write(leftover, 0, leftover.Length);
                }
            }
            mIsMayRead = false;
        }

    }

    void CheckHeartBeat()
    {
        memStream.Seek(0, SeekOrigin.Begin);
        long msgLen = RemainingBytes();
        while (msgLen >= 4)
        {
            short messageLen = reader.ReadInt16();
            messageLen = IPAddress.NetworkToHostOrder(messageLen);
            if(messageLen > 4)
            {
                memStream.Position += (messageLen - 2);
            }
            else
            {
                short msgId = reader.ReadInt16();
                msgId = IPAddress.NetworkToHostOrder(msgId);
                if(msgId == (int)MSG_DEFINE.CMD_CLIENT_GAME_HEARTBEAT_RESP)
                {
                    HeartbeatHandler.Instance.ReceiveTime = DateTime.Now;
                    break;
                }
            }

            msgLen = RemainingBytes();
        }
        memStream.Seek(0, SeekOrigin.End);
    }
    void OnRecvData()
    {
        int length = 0;
        int rateCount = 0;
        DateTime startTime = DateTime.Now;
        while (!mIsExit)
        {
            TimeSpan span = DateTime.Now - startTime;
            if(span.Seconds > 1)
            {
                ThreadRate = rateCount;
                rateCount = 0;
                startTime = DateTime.Now;
            }
            else
            {
                rateCount++;
            }
            if (mState == ConnectState.STATE_EXIT)
            {
                break;    //退出线程
            }
            if (mState != ConnectState.STATE_CONNECT)
            {
                Thread.Sleep(10);
                continue;
            }
            try
            {
                length = mSocket.Receive(buffer, buffer.Length, 0);
            }
            catch (SocketException se)
            {
                Debug.Log("@@@@@@@@@@@@@@@@@@disconnenct");
                if(mState == ConnectState.STATE_CONNECT)
                {
                    mState = ConnectState.STATE_LOSTCONNECT;
                    if(OnLostConnectCallBack != null)
                    {
                        OnLostConnectCallBack();
                    }
                }
                //DisConnect();
            }

            if (length > 0)
            {
                lock (memStream)
                {
                    memStream.Seek(0, SeekOrigin.End);
                    memStream.Write(buffer, 0, length);
                    CheckHeartBeat();
                    mIsMayRead = true;
                }
            }
        }
    }

    public bool SendMsg(short id, byte[] data, int length)
    {
        if (mSocket == null || !mSocket.Connected) return false;
        bool result = false;
        short sendLength = (short)(length + 4);
        id = IPAddress.HostToNetworkOrder(id);
        short sMsgLen = IPAddress.HostToNetworkOrder(sendLength);
        if (mSendBuffer.Length < sendLength)
        {
            mSendBuffer = new byte[sendLength];
        }
        byte [] bts = BitConverter.GetBytes(sMsgLen);
        Array.Copy(bts, 0, mSendBuffer, 0, 2);
        bts = BitConverter.GetBytes(id);
        Array.Copy(bts, 0, mSendBuffer, 2, 2);
        Array.Copy(data, 0, mSendBuffer, 4, length);

        try
        {
            int n = mSocket.Send(mSendBuffer, sendLength, SocketFlags.None);
            if (n < 1)
                result = false;
            mSendBits += n;
        }
        catch (Exception ee)
        {
            Debug.LogError(ee.ToString());
            result = false;
            if (mState == ConnectState.STATE_CONNECT)
            {
                mState = ConnectState.STATE_LOSTCONNECT;
                if (OnLostConnectCallBack != null)
                    OnLostConnectCallBack();
            }
        }
        return result;
    }
    public bool SendMsg(byte[] data)
    {
        if (mSocket == null || !mSocket.Connected) return false;
        bool result = false;
        if (data == null || data.Length < 0)
            return result;
        try
        {
            int n = mSocket.Send(data);
            if (n < 1)
                result = false;
            mSendBits += n;
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            result = false;
            if (mState == ConnectState.STATE_CONNECT)
            {
                mState = ConnectState.STATE_LOSTCONNECT;
                if (OnLostConnectCallBack != null)
                    OnLostConnectCallBack();
            }
        }
        return result;
    }
    
}
