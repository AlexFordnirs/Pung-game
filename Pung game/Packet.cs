using System;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;

namespace Pung_game
{
    public enum PacketType : uint
    {
        RequestJoin = 1,    
        AcceptJoin,         
        AcceptJoinAck,      
        Heartbeat,          
        HeartbeatAck,       
        GameStart,          
        GameStartAck,       
        PaddlePosition,     
        GameState,          
        PlaySoundEffect,    
        Bye,                
    }

    public class Packet
    {
 
        public PacketType Type;
        public long Timestamp;                  
        public byte[] Payload = new byte[0];

        public Packet(PacketType type)
        {
            this.Type = type;
            Timestamp = DateTime.Now.Ticks;
        }

        public Packet(byte[] bytes)
        {
            int i = 0;
            this.Type = (PacketType)BitConverter.ToUInt32(bytes, 0);
            i += sizeof(PacketType);
            Timestamp = BitConverter.ToInt64(bytes, i);
            i += sizeof(long);
            Payload = bytes.Skip(i).ToArray();
        }

        public byte[] GetBytes()
        {
            int ptSize = sizeof(PacketType);
            int tsSize = sizeof(long);

            int i = 0;
            byte[] bytes = new byte[ptSize + tsSize + Payload.Length];

            BitConverter.GetBytes((uint)this.Type).CopyTo(bytes, i);
            i += ptSize;

            BitConverter.GetBytes(Timestamp).CopyTo(bytes, i);
            i += tsSize;

            Payload.CopyTo(bytes, i);
            i += Payload.Length;

            return bytes;
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}]",
                this.Type, new DateTime(Timestamp), Payload.Length);
        }

        public void Send(UdpClient client, IPEndPoint receiver)
        {
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length, receiver);
        }

        public void Send(UdpClient client)
        {
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length);
        }
    }


    public class RequestJoinPacket : Packet
    {
        public RequestJoinPacket()
            : base(PacketType.RequestJoin)
        {
        }
    }

    public class AcceptJoinPacket : Packet
    {
        public PaddleSide Side
        {
            get { return (PaddleSide)BitConverter.ToUInt32(Payload, 0); }
            set { Payload = BitConverter.GetBytes((uint)value); }
        }

        public AcceptJoinPacket()
            : base(PacketType.AcceptJoin)
        {
            Payload = new byte[sizeof(PaddleSide)];
            Side = PaddleSide.None;
        }

        public AcceptJoinPacket(byte[] bytes)
            : base(bytes)
        {
        }
    }

    public class AcceptJoinAckPacket : Packet
    {
        public AcceptJoinAckPacket()
            : base(PacketType.AcceptJoinAck)
        {
        }
    }

    public class HeartbeatPacket : Packet
    {
        public HeartbeatPacket()
            : base(PacketType.Heartbeat)
        {
        }
    }

    public class HeartbeatAckPacket : Packet
    {
        public HeartbeatAckPacket()
            : base(PacketType.HeartbeatAck)
        {
        }
    }

    public class GameStartPacket : Packet
    {
        public GameStartPacket()
            : base(PacketType.GameStart)
        {
        }
    }

    public class GameStartAckPacket : Packet
    {
        public GameStartAckPacket()
            : base(PacketType.GameStartAck)
        {
        }
    }

    public class PaddlePositionPacket : Packet
    {
        public float Y
        {
            get { return BitConverter.ToSingle(Payload, 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 0); }
        }

        public PaddlePositionPacket()
            : base(PacketType.PaddlePosition)
        {
            Payload = new byte[sizeof(float)];

            Y = 0;
        }

        public PaddlePositionPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  Y={3}]",
                this.Type, new DateTime(Timestamp), Payload.Length, Y);
        }
    }


    public class GameStatePacket : Packet
    {

        private static readonly int _leftYIndex = 0;
        private static readonly int _rightYIndex = 4;
        private static readonly int _ballPositionIndex = 8;
        private static readonly int _leftScoreIndex = 16;
        private static readonly int _rightScoreIndex = 20;


        public float LeftY
        {
            get { return BitConverter.ToSingle(Payload, _leftYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftYIndex); }
        }

        public float RightY
        {
            get { return BitConverter.ToSingle(Payload, _rightYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightYIndex); }
        }


        public Vector2 BallPosition
        {
            get
            {
                return new Vector2(
                    BitConverter.ToSingle(Payload, _ballPositionIndex),
                    BitConverter.ToSingle(Payload, _ballPositionIndex + sizeof(float))
                );
            }
            set
            {
                BitConverter.GetBytes(value.X).CopyTo(Payload, _ballPositionIndex);
                BitConverter.GetBytes(value.Y).CopyTo(Payload, _ballPositionIndex + sizeof(float));
            }
        }


        public int LeftScore
        {
            get { return BitConverter.ToInt32(Payload, _leftScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftScoreIndex); }
        }


        public int RightScore
        {
            get { return BitConverter.ToInt32(Payload, _rightScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightScoreIndex); }
        }

        public GameStatePacket()
            : base(PacketType.GameState)
        {

            Payload = new byte[24];


            LeftY = 0;
            RightY = 0;
            BallPosition = new Vector2();
            LeftScore = 0;
            RightScore = 0;
        }

        public GameStatePacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  LeftY={3}" +
                "\n  RightY={4}" +
                "\n  BallPosition={5}" +
                "\n  LeftScore={6}" +
                "\n  RightScore={7}]",
                this.Type, new DateTime(Timestamp), Payload.Length, LeftY, RightY, BallPosition, LeftScore, RightScore);
        }
    }


    public class PlaySoundEffectPacket : Packet
    {
        public string SFXName
        {
            get { return Encoding.UTF8.GetString(Payload); }
            set { Payload = Encoding.UTF8.GetBytes(value); }
        }

        public PlaySoundEffectPacket()
            : base(PacketType.PlaySoundEffect)
        {
            SFXName = "";
        }

        public PlaySoundEffectPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  SFXName={3}",
                this.Type, new DateTime(Timestamp), Payload.Length, SFXName);
        }
    }

    public class ByePacket : Packet
    {
        public ByePacket()
            : base(PacketType.Bye)
        {
        }
    }

}