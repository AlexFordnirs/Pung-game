using System;
using System.Net;

namespace Pung_game
{
    public class PlayerInfo
    {
        public Paddle Paddle;
        public IPEndPoint Endpoint;
        public DateTime LastPacketReceivedTime = DateTime.MinValue;     
        public DateTime LastPacketSentTime = DateTime.MinValue;         
        public long LastPacketReceivedTimestamp = 0;                    
        public bool HavePaddle = false;
        public bool Ready = false;

        public bool IsSet
        {
            get { return Endpoint != null; }
        }
    }
}

