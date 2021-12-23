using System;
using Microsoft.Xna.Framework;

namespace Pung_game
{

    public static class GameGeometry
    {
        public static readonly Point PlayArea = new Point(320, 240);    
        public static readonly Vector2 ScreenCenter                     
            = new Vector2(PlayArea.X / 2f, PlayArea.Y / 2f);
        public static readonly Point BallSize = new Point(8, 8);        
        public static readonly Point PaddleSize = new Point(8, 44);    
        public static readonly int GoalSize = 12;                       
        public static readonly float PaddleSpeed = 100f;                
    }
}
