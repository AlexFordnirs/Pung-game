using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace Pung_game
{
    public enum ArenaState
    {
        NotRunning,
        WaitingForPlayers,
        NotifyingGameStart,
        InGame,
        GameOver
    }

    public class Arena
    {
        public ThreadSafe<ArenaState> State { get; private set; } = new ThreadSafe<ArenaState>();
        private Ball _ball = new Ball();
        public PlayerInfo LeftPlayer { get; private set; } = new PlayerInfo();      
        public PlayerInfo RightPlayer { get; private set; } = new PlayerInfo();     
        private object _setPlayerLock = new object();
        private Stopwatch _gameTimer = new Stopwatch();

      
        private PongServer _server;
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);

       
        private ConcurrentQueue<NetworkMessage> _messages = new ConcurrentQueue<NetworkMessage>();

       
        private ThreadSafe<bool> _stopRequested = new ThreadSafe<bool>(false);

        
        private Thread _arenaThread;
        private Random _random = new Random();
        public readonly int Id;
        private static int _nextId = 1;

        public Arena(PongServer server)
        {
            _server = server;
            Id = _nextId++;
            State.Value = ArenaState.NotRunning;

           
            LeftPlayer.Paddle = new Paddle(PaddleSide.Left);
            RightPlayer.Paddle = new Paddle(PaddleSide.Right);
        }


        public bool TryAddPlayer(IPEndPoint playerIP)
        {
            if (State.Value == ArenaState.WaitingForPlayers)
            {
                lock (_setPlayerLock)
                {
 
                    if (!LeftPlayer.IsSet)
                    {
                        LeftPlayer.Endpoint = playerIP;
                        return true;
                    }

             
                    if (!RightPlayer.IsSet)
                    {
                        RightPlayer.Endpoint = playerIP;
                        return true;
                    }
                }
            }

            return false;
        }

        public void Start()
        {
            State.Value = ArenaState.WaitingForPlayers;

            _arenaThread = new Thread(new ThreadStart(_arenaRun));
            _arenaThread.Start();
        }

        public void Stop()
        {
            _stopRequested.Value = true;
        }

        private void _arenaRun()
        {
            Console.WriteLine("[{0:000}] Waiting for players", Id);
            GameTime gameTime = new GameTime();

            TimeSpan notifyGameStartTimeout = TimeSpan.FromSeconds(2.5);
            TimeSpan sendGameStateTimeout = TimeSpan.FromMilliseconds(1000f / 30f); 

            bool running = true;
            bool playerDropped = false;
            while (running)
            {
                NetworkMessage message;
                bool haveMsg = _messages.TryDequeue(out message);

                switch (State.Value)
                {
                    case ArenaState.WaitingForPlayers:
                        if (haveMsg)
                        {
                            _handleConnectionSetup(LeftPlayer, message);
                            _handleConnectionSetup(RightPlayer, message);

                            if (LeftPlayer.HavePaddle && RightPlayer.HavePaddle)
                            {
                                _notifyGameStart(LeftPlayer, new TimeSpan());
                                _notifyGameStart(RightPlayer, new TimeSpan());

                                State.Value = ArenaState.NotifyingGameStart;
                            }
                        }
                        break;

                    case ArenaState.NotifyingGameStart:
                        _notifyGameStart(LeftPlayer, notifyGameStartTimeout);
                        _notifyGameStart(RightPlayer, notifyGameStartTimeout);

                        if (haveMsg && (message.Packet.Type == PacketType.GameStartAck))
                        {
                            if (message.Sender.Equals(LeftPlayer.Endpoint))
                                LeftPlayer.Ready = true;
                            else if (message.Sender.Equals(RightPlayer.Endpoint))
                                RightPlayer.Ready = true;
                        }

                        if (LeftPlayer.Ready && RightPlayer.Ready)
                        {
                            _ball.Initialize();
                            LeftPlayer.Paddle.Initialize();
                            RightPlayer.Paddle.Initialize();

                            _sendGameState(LeftPlayer, new TimeSpan());
                            _sendGameState(RightPlayer, new TimeSpan());

                            State.Value = ArenaState.InGame;
                            Console.WriteLine("[{0:000}] Starting Game", Id);
                            _gameTimer.Start();
                        }

                        break;

                    case ArenaState.InGame:
                        TimeSpan now = _gameTimer.Elapsed;
                        gameTime = new GameTime(now, now - gameTime.TotalGameTime);

                        if (haveMsg)
                        {
                            switch (message.Packet.Type)
                            {
                                case PacketType.PaddlePosition:
                                    _handlePaddleUpdate(message);
                                    break;

                                case PacketType.Heartbeat:
                                    HeartbeatAckPacket hap = new HeartbeatAckPacket();
                                    PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;
                                    _sendTo(player, hap);

                                    player.LastPacketReceivedTime = message.ReceiveTime;
                                    break;
                            }
                        }

                        _ball.ServerSideUpdate(gameTime);
                        _checkForBallCollisions();

                        _sendGameState(LeftPlayer, sendGameStateTimeout);
                        _sendGameState(RightPlayer, sendGameStateTimeout);
                        break;
                }

                if (haveMsg && (message.Packet.Type == PacketType.Bye))
                {
                    PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;
                    running = false;
                    Console.WriteLine("[{0:000}] Quit detected from {1} at {2}",
                        Id, player.Paddle.Side, _gameTimer.Elapsed);

                    if (player.Paddle.Side == PaddleSide.Left)
                    {
                        if (RightPlayer.IsSet)
                            _server.SendBye(RightPlayer.Endpoint);
                    }
                    else
                    {
                        if (LeftPlayer.IsSet)
                            _server.SendBye(LeftPlayer.Endpoint);
                    }
                }

                playerDropped |= _timedOut(LeftPlayer);
                playerDropped |= _timedOut(RightPlayer);

                Thread.Sleep(1);

                running &= !_stopRequested.Value;
                running &= !playerDropped;
            }

            _gameTimer.Stop();
            State.Value = ArenaState.GameOver;
            Console.WriteLine("[{0:000}] Game Over, total game time was {1}", Id, _gameTimer.Elapsed);

            if (_stopRequested.Value)
            {
                Console.WriteLine("[{0:000}] Notifying Players of server shutdown", Id);

                if (LeftPlayer.IsSet)
                    _server.SendBye(LeftPlayer.Endpoint);
                if (RightPlayer.IsSet)
                    _server.SendBye(RightPlayer.Endpoint);
            }

            _server.NotifyDone(this);
        }

        public void JoinThread()
        {
            _arenaThread.Join(100);
        }

        public void EnqueMessage(NetworkMessage nm)
        {
            _messages.Enqueue(nm);
        }

        private void _sendTo(PlayerInfo player, Packet packet)
        {
            _server.SendPacket(packet, player.Endpoint);
            player.LastPacketSentTime = DateTime.Now;
        }

        private bool _timedOut(PlayerInfo player)
        {
  
            if (player.LastPacketReceivedTime == DateTime.MinValue)
                return false;


            bool timeoutDetected = (DateTime.Now > (player.LastPacketReceivedTime.Add(_heartbeatTimeout)));
            if (timeoutDetected)
                Console.WriteLine("[{0:000}] Timeout detected on {1} Player at {2}", Id, player.Paddle.Side, _gameTimer.Elapsed);

            return timeoutDetected;
        }

        private void _handleConnectionSetup(PlayerInfo player, NetworkMessage message)
        {
            bool sentByPlayer = message.Sender.Equals(player.Endpoint);
            if (sentByPlayer)
            {
                player.LastPacketReceivedTime = message.ReceiveTime;

                switch (message.Packet.Type)
                {
                    case PacketType.RequestJoin:
                        Console.WriteLine("[{0:000}] Join Request from {1}", Id, player.Endpoint);
                        _sendAcceptJoin(player);
                        break;

                    case PacketType.AcceptJoinAck:
                        player.HavePaddle = true;
                        break;

                    case PacketType.Heartbeat:
                        HeartbeatAckPacket hap = new HeartbeatAckPacket();
                        _sendTo(player, hap);
                        if (!player.HavePaddle)
                            _sendAcceptJoin(player);

                        break;
                }
            }
        }

        public void _sendAcceptJoin(PlayerInfo player)
        {
            AcceptJoinPacket ajp = new AcceptJoinPacket();
            ajp.Side = player.Paddle.Side;
            _sendTo(player, ajp);
        }
        private void _notifyGameStart(PlayerInfo player, TimeSpan retryTimeout)
        {
            if (player.Ready)
                return;

            if (DateTime.Now >= (player.LastPacketSentTime.Add(retryTimeout)))
            {
                GameStartPacket gsp = new GameStartPacket();
                _sendTo(player, gsp);
            }
        }

        private void _sendGameState(PlayerInfo player, TimeSpan resendTimeout)
        {
            if (DateTime.Now >= (player.LastPacketSentTime.Add(resendTimeout)))
            {
                GameStatePacket gsp = new GameStatePacket();
                gsp.LeftY = LeftPlayer.Paddle.Position.Y;
                gsp.RightY = RightPlayer.Paddle.Position.Y;
                gsp.BallPosition = _ball.Position;
                gsp.LeftScore = LeftPlayer.Paddle.Score;
                gsp.RightScore = RightPlayer.Paddle.Score;

                _sendTo(player, gsp);
            }
        }

        private void _playSoundEffect(string sfxName)
        {
            PlaySoundEffectPacket packet = new PlaySoundEffectPacket();
            packet.SFXName = sfxName;

            _sendTo(LeftPlayer, packet);
            _sendTo(RightPlayer, packet);
        }

        private void _handlePaddleUpdate(NetworkMessage message)
        {
            PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;

            if (message.Packet.Timestamp > player.LastPacketReceivedTimestamp)
            {
                player.LastPacketReceivedTimestamp = message.Packet.Timestamp;
                player.LastPacketReceivedTime = message.ReceiveTime;

                PaddlePositionPacket ppp = new PaddlePositionPacket(message.Packet.GetBytes());
                player.Paddle.Position.Y = ppp.Y;
            }
        }
  

        private void _checkForBallCollisions()
        {
        
            float ballY = _ball.Position.Y;
            if ((ballY <= _ball.TopmostY) || (ballY >= _ball.BottommostY))
            {
                _ball.Speed.Y *= -1;
                _playSoundEffect("ball-hit");
            }

            float ballX = _ball.Position.X;
            if (ballX <= _ball.LeftmostX)
            {
                RightPlayer.Paddle.Score += 1;
                Console.WriteLine("[{0:000}] Right Player scored ({1} -- {2}) at {3}",
                    Id, LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                _playSoundEffect("score");
            }
            else if (ballX >= _ball.RightmostX)
            {
                LeftPlayer.Paddle.Score += 1;
                Console.WriteLine("[{0:000}] Left Player scored ({1} -- {2}) at {3}",
                    Id, LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                _playSoundEffect("score");
            }

            PaddleCollision collision;
            if (LeftPlayer.Paddle.Collides(_ball, out collision))
                _processBallHitWithPaddle(collision);
            if (RightPlayer.Paddle.Collides(_ball, out collision))
                _processBallHitWithPaddle(collision);

        }

        private void _processBallHitWithPaddle(PaddleCollision collision)
        {
            if (collision == PaddleCollision.None)
                return;

            _ball.Speed.X *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);
            _ball.Speed.Y *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);

            _ball.Speed.X *= -1;

            if ((collision == PaddleCollision.WithTop) || (collision == PaddleCollision.WithBottom))
                _ball.Speed.Y *= -1;

            _playSoundEffect("ballHit");
        }

        private float _map(float x, float a, float b, float p, float q)
        {
            return p + (x - a) * (q - p) / (b - a);
        }
    }
}