using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;


namespace Pung_game
{

    public class Ball
    {
        public static Vector2 InitialSpeed = new Vector2(60f, 60f);
        private Texture2D _sprite;
        private Random _random = new Random();     

        public Vector2 Position = new Vector2();
        public Vector2 Speed;
        public int LeftmostX { get; private set; }     
        public int RightmostX { get; private set; }
        public int TopmostY { get; private set; }
        public int BottommostY { get; private set; }

        public Rectangle CollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), GameGeometry.BallSize); }
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("ball.png");
        }

        public void Initialize()
        {
            Rectangle playAreaRect = new Rectangle(new Point(0, 0), GameGeometry.PlayArea);
            Position = playAreaRect.Center.ToVector2();
            Position = Vector2.Subtract(Position, GameGeometry.BallSize.ToVector2() / 2f);

            Speed = InitialSpeed;

            if (_random.Next() % 2 == 1)
                Speed.X *= -1;
            if (_random.Next() % 2 == 1)
                Speed.Y *= -1;

            LeftmostX = 0;
            RightmostX = playAreaRect.Width - GameGeometry.BallSize.X;
            TopmostY = 0;
            BottommostY = playAreaRect.Height - GameGeometry.BallSize.Y;
        }

        public void ServerSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;

            Position = Vector2.Add(Position, timeDelta * Speed);
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);

        }
    }
}