using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pung_game
{
    public class PongServer
    {
        private UdpClient _udpClient;
        public readonly int Port;
        Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages
            = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Tuple<Packet, IPEndPoint>> _outgoingMessages
            = new ConcurrentQueue<Tuple<Packet, IPEndPoint>>();
        private ConcurrentQueue<IPEndPoint> _sendByePacketTo
            = new ConcurrentQueue<IPEndPoint>();
        private ConcurrentDictionary<Arena, byte> _activeArenas           
            = new ConcurrentDictionary<Arena, byte>();
        private ConcurrentDictionary<IPEndPoint, Arena> _playerToArenaMap
            = new ConcurrentDictionary<IPEndPoint, Arena>();
        private Arena _nextArena;

        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);

        public PongServer(int port)
        {
            Port = port;

            _udpClient = new UdpClient(Port, AddressFamily.InterNetwork);
        }
        public void Start()
        {
            _running.Value = true;
        }
        public void Shutdown()
        {
            if (_running.Value)
            {
                Console.WriteLine("[Server] Shutdown requested by user.");

                Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
                foreach (Arena arena in arenas)
                    arena.Stop();
                _running.Value = false;
            }
        }
        public void Close()
        {
            _networkThread?.Join(TimeSpan.FromSeconds(10));
            _udpClient.Close();
        }
        private void _addNewArena()
        {
            _nextArena = new Arena(this);
            _nextArena.Start();
            _activeArenas.TryAdd(_nextArena, 0);
        }
        public void NotifyDone(Arena arena)
        {
            Arena a;
            if (arena.LeftPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.LeftPlayer.Endpoint, out a);
            if (arena.RightPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.RightPlayer.Endpoint, out a);
            byte b;
            _activeArenas.TryRemove(arena, out b);
        }

        public void Run()
        {

            if (_running.Value)
            {

                Console.WriteLine("[Server] Running Ping Game");

                _networkThread = new Thread(new ThreadStart(_networkRun));
                _networkThread.Start();

                _addNewArena();
            }

            bool running = _running.Value;
            while (running)
            {

                NetworkMessage nm;
                bool have = _incomingMessages.TryDequeue(out nm);
                if (have)
                {

                    if (nm.Packet.Type == PacketType.RequestJoin)
                    {

                        bool added = _nextArena.TryAddPlayer(nm.Sender);
                        if (added)
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);

                        if (!added)
                        {
                            _addNewArena();

                            _nextArena.TryAddPlayer(nm.Sender);
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);
                        }

                        _nextArena.EnqueMessage(nm);
                    }
                    else
                    {

                        Arena arena;
                        if (_playerToArenaMap.TryGetValue(nm.Sender, out arena))
                            arena.EnqueMessage(nm);
                    }
                }
                else
                    Thread.Sleep(1);   
                running &= _running.Value;
            }
        }

        private void _networkRun()
        {
            if (!_running.Value)
                return;

            Console.WriteLine("[Server] Waiting for UDP datagrams on port {0}", Port);

            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;
                int numToDisconnect = _sendByePacketTo.Count;

                if (canRead)
                {

                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref ep);            
                    NetworkMessage nm = new NetworkMessage();
                    nm.Sender = ep;
                    nm.Packet = new Packet(data);
                    nm.ReceiveTime = DateTime.Now;

                    _incomingMessages.Enqueue(nm);
                }

                for (int i = 0; i < numToWrite; i++)
                {

                    Tuple<Packet, IPEndPoint> msg;
                    bool have = _outgoingMessages.TryDequeue(out msg);
                    if (have)
                        msg.Item1.Send(_udpClient, msg.Item2);

                }

                for (int i = 0; i < numToDisconnect; i++)
                {
                    IPEndPoint to;
                    bool have = _sendByePacketTo.TryDequeue(out to);
                    if (have)
                    {
                        ByePacket bp = new ByePacket();
                        bp.Send(_udpClient, to);
                    }
                }

                if (!canRead && (numToWrite == 0) && (numToDisconnect == 0))
                    Thread.Sleep(1);
            }

            Console.WriteLine("[Server] Done listening for UDP datagrams");

            Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
            if (arenas.Count > 0)
            {
                Console.WriteLine("[Server] Waiting for active Areans to finish...");
                foreach (Arena arena in arenas)
                    arena.JoinThread();
            }
            if (_sendByePacketTo.Count > 0)
            {
                Console.WriteLine("[Server] Notifying remaining clients of shutdown...");
                IPEndPoint to;
                bool have = _sendByePacketTo.TryDequeue(out to);
                while (have)
                {
                    ByePacket bp = new ByePacket();
                    bp.Send(_udpClient, to);
                    have = _sendByePacketTo.TryDequeue(out to);
                }
            }
        }

        public void SendPacket(Packet packet, IPEndPoint to)
        {
            _outgoingMessages.Enqueue(new Tuple<Packet, IPEndPoint>(packet, to));
        }

        public void SendBye(IPEndPoint to)
        {
            _sendByePacketTo.Enqueue(to);
        }




        public static PongServer server;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            server?.Shutdown();
        }

        public static void Main(string[] args)
        {
            int port = 4000;//int.Parse(args[0].Trim());
            server = new PongServer(port);


            Console.CancelKeyPress += InterruptHandler;

            server.Start();
            server.Run();
            server.Close();
        }
    }
}