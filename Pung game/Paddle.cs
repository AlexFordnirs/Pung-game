using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Input;

namespace Pung_game
{
    public enum PaddleSide : uint
    {
        None,
        Left,
        Right
    };

    public enum PaddleCollision
    {
        None,
        WithTop,
        WithFront,
        WithBottom
    };

    public class Paddle
    {

        private Texture2D _sprite;
        private DateTime _lastCollisiontime = DateTime.MinValue;
        private TimeSpan _minCollisionTimeGap = TimeSpan.FromSeconds(0.2);

        public readonly PaddleSide Side;
        public int Score = 0;
        public Vector2 Position = new Vector2();
        public int TopmostY { get; private set; }               
        public int BottommostY { get; private set; }

        public Rectangle TopCollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), new Point(GameGeometry.PaddleSize.X, 4)); }
        }

        public Rectangle BottomCollisionArea
        {
            get
            {
                return new Rectangle(
                    (int)Position.X, FrontCollisionArea.Bottom,
                    GameGeometry.PaddleSize.X, 4
                );
            }
        }

        public Rectangle FrontCollisionArea
        {
            get
            {
                Point pos = Position.ToPoint();
                pos.Y += 4;
                Point size = new Point(GameGeometry.PaddleSize.X, GameGeometry.PaddleSize.Y - 8);

                return new Rectangle(pos, size);
            }
        }

        public Paddle(PaddleSide side)
        {
            Side = side;
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("paddle.png");
        }


        public void Initialize()
        {

            int x;
            if (Side == PaddleSide.Left)
                x = GameGeometry.GoalSize;
            else if (Side == PaddleSide.Right)
                x = GameGeometry.PlayArea.X - GameGeometry.PaddleSize.X - GameGeometry.GoalSize;
            else
                throw new Exception("Side is not `Left` or `Right`");

            Position = new Vector2(x, (GameGeometry.PlayArea.Y / 2) - (GameGeometry.PaddleSize.Y / 2));
            Score = 0;

  
            TopmostY = 0;
            BottommostY = GameGeometry.PlayArea.Y - GameGeometry.PaddleSize.Y;
        }


        public void ClientSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float dist = timeDelta * GameGeometry.PaddleSpeed;

            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Up))
                Position.Y -= dist;
            else if (kbs.IsKeyDown(Keys.Down))
                Position.Y += dist;

            if (Position.Y < TopmostY)
                Position.Y = TopmostY;
            else if (Position.Y > BottommostY)
                Position.Y = BottommostY;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);
        }

        public bool Collides(Ball ball, out PaddleCollision typeOfCollision)
        {
            typeOfCollision = PaddleCollision.None;

            if (DateTime.Now < (_lastCollisiontime.Add(_minCollisionTimeGap)))
                return false;

            if (ball.CollisionArea.Intersects(TopCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithTop;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            if (ball.CollisionArea.Intersects(BottomCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithBottom;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            if (ball.CollisionArea.Intersects(FrontCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithFront;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            return false;
        }
    }
}
