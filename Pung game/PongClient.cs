using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;

namespace Pung_game
{
    public enum ClientState
    {
        NotConnected,
        EstablishingConnection,
        WaitingForGameStart,
        InGame,
        GameOver,
    }

    class PongClient : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        private UdpClient _udpClient;
        public readonly string ServerHostname;
        public readonly int ServerPort;

        private DateTime _lastPacketReceivedTime = DateTime.MinValue;     
        private DateTime _lastPacketSentTime = DateTime.MinValue;         
        private long _lastPacketReceivedTimestamp = 0;                    
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);
        private TimeSpan _sendPaddlePositionTimeout = TimeSpan.FromMilliseconds(1000f / 30f);  

        private Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages
            = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Packet> _outgoingMessages
            = new ConcurrentQueue<Packet>();

        private Ball _ball;
        private Paddle _left;
        private Paddle _right;
        private Paddle _ourPaddle;
        private float _previousY;

        private Texture2D _establishingConnectionMsg;
        private Texture2D _waitingForGameStartMsg;
        private Texture2D _gamveOverMsg;

        private SoundEffect _ballHitSFX;
        private SoundEffect _scoreSFX;

        private ClientState _state = ClientState.NotConnected;
        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);
        private ThreadSafe<bool> _sendBye = new ThreadSafe<bool>(false);

        public PongClient(string hostname, int port)
        {
            Content.RootDirectory = "Content";

            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = GameGeometry.PlayArea.X;
            _graphics.PreferredBackBufferHeight = GameGeometry.PlayArea.Y;
            _graphics.IsFullScreen = false;
            _graphics.ApplyChanges();

            _ball = new Ball();
            _left = new Paddle(PaddleSide.Left);
            _right = new Paddle(PaddleSide.Right);

            ServerHostname = hostname;
            ServerPort = port;
            _udpClient = new UdpClient(ServerHostname, ServerPort);
        }

        protected override void Initialize()
        {
            base.Initialize();
            _left.Initialize();
            _right.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(_graphics.GraphicsDevice);

            _ball.LoadContent(Content);
            _left.LoadContent(Content);
            _right.LoadContent(Content);

            _establishingConnectionMsg = Content.Load<Texture2D>("establishing-connection-msg.png");
            _waitingForGameStartMsg = Content.Load<Texture2D>("waiting-for-game-start-msg.png");
            _gamveOverMsg = Content.Load<Texture2D>("game-over-msg.png");

            _ballHitSFX = Content.Load<SoundEffect>("ball-hit.wav");
            _scoreSFX = Content.Load<SoundEffect>("score.wav");
        }

        protected override void UnloadContent()
        {
            _networkThread?.Join(TimeSpan.FromSeconds(2));
            _udpClient.Close();

            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Escape))
            {
                if ((_state == ClientState.EstablishingConnection) ||
                    (_state == ClientState.WaitingForGameStart) ||
                    (_state == ClientState.InGame))
                {
                    _sendBye.Value = true;
                }

                _running.Value = false;
                _state = ClientState.GameOver;
                Exit();
            }

            if (_timedOut())
                _state = ClientState.GameOver;

            NetworkMessage message;
            bool haveMsg = _incomingMessages.TryDequeue(out message);

            if (haveMsg && (message.Packet.Type == PacketType.Bye))
            {
                _running.Value = false;
                _state = ClientState.GameOver;
            }

            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    _sendRequestJoin(TimeSpan.FromSeconds(1));
                    if (haveMsg)
                        _handleConnectionSetupResponse(message.Packet);
                    break;

                case ClientState.WaitingForGameStart:
                    _sendHeartbeat(TimeSpan.FromSeconds(0.2));

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.AcceptJoin:
                                _sendAcceptJoinAck();
                                break;

                            case PacketType.HeartbeatAck:
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameStart:
                                _sendGameStartAck();
                                _state = ClientState.InGame;
                                break;
                        }

                    }
                    break;

                case ClientState.InGame:
                    _sendHeartbeat(TimeSpan.FromSeconds(0.2));

                    _previousY = _ourPaddle.Position.Y;
                    _ourPaddle.ClientSideUpdate(gameTime);
                    _sendPaddlePosition(_sendPaddlePositionTimeout);

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.GameStart:
                                _sendGameStartAck();
                                break;

                            case PacketType.HeartbeatAck:
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameState:
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                {
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;

                                    GameStatePacket gsp = new GameStatePacket(message.Packet.GetBytes());
                                    _left.Score = gsp.LeftScore;
                                    _right.Score = gsp.RightScore;
                                    _ball.Position = gsp.BallPosition;

                                    if (_ourPaddle.Side == PaddleSide.Left)
                                        _right.Position.Y = gsp.RightY;
                                    else
                                        _left.Position.Y = gsp.LeftY;
                                }

                                break;

                            case PacketType.PlaySoundEffect:
                                break;
                        }
                    }

                    break;

                case ClientState.GameOver:
                    break;
            }

            base.Update(gameTime);
        }

        public void Start()
        {
            _running.Value = true;
            _state = ClientState.EstablishingConnection;

            _networkThread = new Thread(new ThreadStart(_networkRun));
            _networkThread.Start();
        }

        protected override void Draw(GameTime gameTime)
        {
            _graphics.GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin();

            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    _drawCentered(_establishingConnectionMsg);
                    Window.Title = String.Format("Pong -- Connecting to {0}:{1}", ServerHostname, ServerPort);
                    break;

                case ClientState.WaitingForGameStart:
                    _drawCentered(_waitingForGameStartMsg);
                    Window.Title = String.Format("Pong -- Waiting for 2nd Player");
                    break;

                case ClientState.InGame:
                    _ball.Draw(gameTime, _spriteBatch);
                    _left.Draw(gameTime, _spriteBatch);
                    _right.Draw(gameTime, _spriteBatch);

                    _updateWindowTitleWithScore();
                    break;

                case ClientState.GameOver:
                    _drawCentered(_gamveOverMsg);
                    _updateWindowTitleWithScore();
                    break;
            }

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private void _drawCentered(Texture2D texture)
        {
            Vector2 textureCenter = new Vector2(texture.Width / 2, texture.Height / 2);
            _spriteBatch.Draw(texture, GameGeometry.ScreenCenter, null, null, textureCenter);
        }

        private void _updateWindowTitleWithScore()
        {
            string fmt = (_ourPaddle.Side == PaddleSide.Left) ?
                "[{0}] -- Pong -- {1}" : "{0} -- Pong -- [{1}]";
            Window.Title = String.Format(fmt, _left.Score, _right.Score);
        }

        private void _networkRun()
        {
            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;

                if (canRead)
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref ep);              

                    NetworkMessage nm = new NetworkMessage();
                    nm.Sender = ep;
                    nm.Packet = new Packet(data);
                    nm.ReceiveTime = DateTime.Now;

                    _incomingMessages.Enqueue(nm);

                    //Console.WriteLine("RCVD: {0}", nm.Packet);
                }

                for (int i = 0; i < numToWrite; i++)
                {
                    Packet packet;
                    bool have = _outgoingMessages.TryDequeue(out packet);
                    if (have)
                        packet.Send(_udpClient);

                    //Console.WriteLine("SENT: {0}", packet);
                }

                if (!canRead && (numToWrite == 0))
                    Thread.Sleep(1);
            }

            if (_sendBye.Value)
            {
                ByePacket bp = new ByePacket();
                bp.Send(_udpClient);
                Thread.Sleep(1000);     
            }
        }

        private void _sendPacket(Packet packet)
        {
            _outgoingMessages.Enqueue(packet);
            _lastPacketSentTime = DateTime.Now;
        }

        private void _sendRequestJoin(TimeSpan retryTimeout)
        {
            if (DateTime.Now >= (_lastPacketSentTime.Add(retryTimeout)))
            {
                RequestJoinPacket gsp = new RequestJoinPacket();
                _sendPacket(gsp);
            }
        }

        private void _sendAcceptJoinAck()
        {
            AcceptJoinAckPacket ajap = new AcceptJoinAckPacket();
            _sendPacket(ajap);
        }

        private void _handleConnectionSetupResponse(Packet packet)
        {
            if (packet.Type == PacketType.AcceptJoin)
            {
                if (_ourPaddle == null)
                {
                    AcceptJoinPacket ajp = new AcceptJoinPacket(packet.GetBytes());
                    if (ajp.Side == PaddleSide.Left)
                        _ourPaddle = _left;
                    else if (ajp.Side == PaddleSide.Right)
                        _ourPaddle = _right;
                    else
                        throw new Exception("Error, invalid paddle side given by server.");     
                }

                _sendAcceptJoinAck();

                _state = ClientState.WaitingForGameStart;
            }
        }

        private void _sendHeartbeat(TimeSpan resendTimeout)
        {
            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                HeartbeatPacket hp = new HeartbeatPacket();
                _sendPacket(hp);
            }
        }

        private void _sendGameStartAck()
        {
            GameStartAckPacket gsap = new GameStartAckPacket();
            _sendPacket(gsap);
        }

        private void _sendPaddlePosition(TimeSpan resendTimeout)
        {
            if (_previousY == _ourPaddle.Position.Y)
                return;

            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                PaddlePositionPacket ppp = new PaddlePositionPacket();
                ppp.Y = _ourPaddle.Position.Y;

                _sendPacket(ppp);
            }
        }

        private bool _timedOut()
        {
          
            if (_lastPacketReceivedTime == DateTime.MinValue)
                return false;

         
            return (DateTime.Now > (_lastPacketReceivedTime.Add(_heartbeatTimeout)));
        }

        public static void Main(string[] args)
        {
            string hostname = "localhost";//args[0].Trim();
            int port = 4000;//int.Parse(args[1].Trim());
            PongClient client = new PongClient(hostname, port);
            client.Start();
            client.Run();
        }
    }
}
