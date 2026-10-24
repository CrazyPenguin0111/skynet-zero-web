using System;
using System.Collections.Generic;

namespace SkynetZero
{
	public class MoveGenerator
	{
		public static readonly int EMPTYSQUARE = (int)Board.Piece.EMPTY;
		public static readonly int WKING = (int)Board.Piece.WKING;
		public static readonly int BKING = (int)Board.Piece.BKING;
		public static readonly int UNMOVEDROOK = (int)Board.Piece.WROOK * -1;

        public static readonly CoordFive[] NULLMOVESET = { };

        public static readonly CoordFive[] ROOKMOVESET = {
            // spatial
            new CoordFive(1, 0, 0, 0),
            new CoordFive(0, 1, 0, 0),
            new CoordFive(-1, 0, 0, 0),
            new CoordFive(0, -1, 0, 0),
            // temporal
            new CoordFive(0, 0, 0, 1),
            new CoordFive(0, 0, -1, 0),
            new CoordFive(0, 0, 0, -1)
            };

            public static readonly CoordFive[] BISHOPMOVESET = {
                // pure spatial
                new CoordFive(1, 1, 0, 0),
                new CoordFive(1, -1, 0, 0),
                new CoordFive(-1, 1, 0, 0),
                new CoordFive(-1, -1, 0, 0),
                // pure Temporal
                new CoordFive(0, 0, 1, 1),
                new CoordFive(0, 0, 1, -1),
                new CoordFive(0, 0, -1, 1),
                new CoordFive(0, 0, -1, -1),
                // +L
                new CoordFive(1, 0, 0, 1),
                new CoordFive(-1, 0, 0, 1),
                new CoordFive(0, 1, 0, 1),
                new CoordFive(0, -1, 0, 1),
                // -L
                new CoordFive(1, 0, 0, -1),
                new CoordFive(-1, 0, 0, -1),
                new CoordFive(0, 1, 0, -1),
                new CoordFive(0, -1, 0, -1),
                // -T
                new CoordFive(1, 0, -1, 0),
                new CoordFive(-1, 0, -1, 0),
                new CoordFive(0, 1, -1, 0),
                new CoordFive(0, -1, -1, 0)
            };

            public static readonly CoordFive[] UnicornMoveset = {
                new CoordFive(1, 1, 0, 1),
                new CoordFive(1, 0, 1, 1),
                new CoordFive(0, 1, 1, 1),
                new CoordFive(-1, 1, 0, 1),
                new CoordFive(-1, 0, 1, 1),
                new CoordFive(0, -1, 1, 1),
                new CoordFive(1, -1, 0, 1),
                new CoordFive(1, 0, -1, 1),
                new CoordFive(0, 1, -1, 1),
                new CoordFive(1, 1, -1, 0),
                new CoordFive(1, 1, 0, -1),
                new CoordFive(1, 0, 1, -1),
                new CoordFive(0, 1, 1, -1),
                new CoordFive(-1, -1, 0, 1),
                new CoordFive(-1, 0, -1, 1),
                new CoordFive(0, -1, -1, 1),
                new CoordFive(-1, 1, -1, 0),
                new CoordFive(-1, 1, 0, -1),
                new CoordFive(-1, 0, 1, -1),
                new CoordFive(0, -1, 1, -1),
                new CoordFive(1, -1, -1, 0),
                new CoordFive(1, -1, 0, -1),
                new CoordFive(1, 0, -1, -1),
                new CoordFive(0, 1, -1, -1),
                new CoordFive(-1, -1, -1, 0),
                new CoordFive(-1, -1, 0, -1),
                new CoordFive(-1, 0, -1, -1),
                new CoordFive(0, -1, -1, -1)
            };

            public static readonly CoordFive[] DragonMoveset = {
                new CoordFive(1, 1, 1, 1),
                new CoordFive(-1, 1, 1, 1),
                new CoordFive(1, -1, 1, 1),
                new CoordFive(1, 1, -1, 1),
                new CoordFive(1, 1, 1, -1),
                new CoordFive(-1, -1, 1, 1),
                new CoordFive(-1, 1, -1, 1),
                new CoordFive(-1, 1, 1, -1),
                new CoordFive(1, -1, -1, 1),
                new CoordFive(1, -1, 1, -1),
                new CoordFive(1, 1, -1, -1),
                new CoordFive(1, -1, -1, -1),
                new CoordFive(-1, 1, -1, -1),
                new CoordFive(-1, -1, 1, -1),
                new CoordFive(-1, -1, -1, 1),
                new CoordFive(-1, -1, -1, -1)
            };

            public static readonly CoordFive[] KNIGHTMOVESET = {
                // pure Spatial
                new CoordFive(1, 2, 0, 0),
                new CoordFive(2, 1, 0, 0),
                new CoordFive(-1, 2, 0, 0),
                new CoordFive(-2, 1, 0, 0),
                new CoordFive(-2, -1, 0, 0),
                new CoordFive(-1, -2, 0, 0),
                new CoordFive(1, -2, 0, 0),
                new CoordFive(2, -1, 0, 0),
                // pure temporal
                new CoordFive(0, 0, 1, 2),
                new CoordFive(0, 0, 2, 1),
                new CoordFive(0, 0, -1, 2),
                new CoordFive(0, 0, -2, 1),
                new CoordFive(0, 0, -2, -1),
                new CoordFive(0, 0, -1, -2),
                new CoordFive(0, 0, 1, -2),
                new CoordFive(0, 0, 2, -1),
                // Half Spatial/temporal
                // +L
                new CoordFive(2, 0, 0, 1),
                new CoordFive(-2, 0, 0, 1),
                new CoordFive(0, 2, 0, 1),
                new CoordFive(0, -2, 0, 1),
                new CoordFive(1, 0, 0, 2),
                new CoordFive(-1, 0, 0, 2),
                new CoordFive(0, 1, 0, 2),
                new CoordFive(0, -1, 0, 2),
                // -L
                new CoordFive(2, 0, 0, -1),
                new CoordFive(-2, 0, 0, -1),
                new CoordFive(0, 2, 0, -1),
                new CoordFive(0, -2, 0, -1),
                new CoordFive(1, 0, 0, -2),
                new CoordFive(-1, 0, 0, -2),
                new CoordFive(0, 1, 0, -2),
                new CoordFive(0, -1, 0, -2),
                // -T
                new CoordFive(2, 0, -1, 0),
                new CoordFive(-2, 0, -1, 0),
                new CoordFive(0, 2, -1, 0),
                new CoordFive(0, -2, -1, 0),
                new CoordFive(1, 0, -2, 0),
                new CoordFive(-1, 0, -2, 0),
                new CoordFive(0, 1, -2, 0),
                new CoordFive(0, -1, -2, 0)
            };

            public static readonly CoordFive[] PRINCESSMOVESET = {
                // ROOK
                // spatial
                new CoordFive(1, 0, 0, 0),
                new CoordFive(0, 1, 0, 0),
                new CoordFive(-1, 0, 0, 0),
                new CoordFive(0, -1, 0, 0),
                // temporal
                new CoordFive(0, 0, 0, 1),
                new CoordFive(0, 0, -1, 0),
                new CoordFive(0, 0, 0, -1),
                // Bishop
                // pure spatial
                new CoordFive(1, 1, 0, 0),
                new CoordFive(1, -1, 0, 0),
                new CoordFive(-1, 1, 0, 0),
                new CoordFive(-1, -1, 0, 0),
                // pure Temporal
                new CoordFive(0, 0, 1, 1),
                new CoordFive(0, 0, 1, -1),
                new CoordFive(0, 0, -1, 1),
                new CoordFive(0, 0, -1, -1),
                // +L
                new CoordFive(1, 0, 0, 1),
                new CoordFive(-1, 0, 0, 1),
                new CoordFive(0, 1, 0, 1),
                new CoordFive(0, -1, 0, 1),
                // -L
                new CoordFive(1, 0, 0, -1),
                new CoordFive(-1, 0, 0, -1),
                new CoordFive(0, 1, 0, -1),
                new CoordFive(0, -1, 0, -1),
                // -T
                new CoordFive(1, 0, -1, 0),
                new CoordFive(-1, 0, -1, 0),
                new CoordFive(0, 1, -1, 0),
                new CoordFive(0, -1, -1, 0)
            };

            public static readonly CoordFive[] KINGMOVESET = {
                // pure Spatial
                new CoordFive(1, 0, 0, 0),
                new CoordFive(1, 1, 0, 0),
                new CoordFive(1, -1, 0, 0),
                new CoordFive(-1, 0, 0, 0),
                new CoordFive(-1, 1, 0, 0),
                new CoordFive(-1, -1, 0, 0),
                new CoordFive(0, 1, 0, 0),
                new CoordFive(0, -1, 0, 0),
                // +L board
                new CoordFive(1, 0, 0, 1),
                new CoordFive(1, 1, 0, 1),
                new CoordFive(1, -1, 0, 1),
                new CoordFive(-1, 0, 0, 1),
                new CoordFive(-1, 1, 0, 1),
                new CoordFive(-1, -1, 0, 1),
                new CoordFive(0, 1, 0, 1),
                new CoordFive(0, -1, 0, 1),
                new CoordFive(0, 0, 0, 1),
                // -L board
                new CoordFive(1, 0, 0, -1),
                new CoordFive(1, 1, 0, -1),
                new CoordFive(1, -1, 0, -1),
                new CoordFive(-1, 0, 0, -1),
                new CoordFive(-1, 1, 0, -1),
                new CoordFive(-1, -1, 0, -1),
                new CoordFive(0, 1, 0, -1),
                new CoordFive(0, -1, 0, -1),
                new CoordFive(0, 0, 0, -1),
                // -T board
                new CoordFive(1, 0, -1, 0),
                new CoordFive(1, 1, -1, 0),
                new CoordFive(1, -1, -1, 0),
                new CoordFive(-1, 0, -1, 0),
                new CoordFive(-1, 1, -1, 0),
                new CoordFive(-1, -1, -1, 0),
                new CoordFive(0, 1, -1, 0),
                new CoordFive(0, -1, -1, 0),
                new CoordFive(0, 0, -1, 0),
                // -T,-L
                new CoordFive(1, 0, -1, -1),
                new CoordFive(1, 1, -1, -1),
                new CoordFive(1, -1, -1, -1),
                new CoordFive(-1, 0, -1, -1),
                new CoordFive(-1, 1, -1, -1),
                new CoordFive(-1, -1, -1, -1),
                new CoordFive(0, 1, -1, -1),
                new CoordFive(0, -1, -1, -1),
                new CoordFive(0, 0, -1, -1),
                // -T,+L
                new CoordFive(1, 0, -1, 1),
                new CoordFive(1, 1, -1, 1),
                new CoordFive(1, -1, -1, 1),
                new CoordFive(-1, 0, -1, 1),
                new CoordFive(-1, 1, -1, 1),
                new CoordFive(-1, -1, -1, 1),
                new CoordFive(0, 1, -1, 1),
                new CoordFive(0, -1, -1, 1),
                new CoordFive(0, 0, -1, 1),
                // +T,+L
                new CoordFive(1, 0, 1, 1),
                new CoordFive(1, 1, 1, 1),
                new CoordFive(1, -1, 1, 1),
                new CoordFive(-1, 0, 1, 1),
                new CoordFive(-1, 1, 1, 1),
                new CoordFive(-1, -1, 1, 1),
                new CoordFive(0, 1, 1, 1),
                new CoordFive(0, -1, 1, 1),
                new CoordFive(0, 0, 1, 1),
                // +T,-L
                new CoordFive(1, 0, 1, -1),
                new CoordFive(1, 1, 1, -1),
                new CoordFive(1, -1, 1, -1),
                new CoordFive(-1, 0, 1, -1),
                new CoordFive(-1, 1, 1, -1),
                new CoordFive(-1, -1, 1, -1),
                new CoordFive(0, 1, 1, -1),
                new CoordFive(0, -1, 1, -1),
                new CoordFive(0, 0, 1, -1)
            };

            public static readonly CoordFive[] whitePawnMovement = {
                new CoordFive(0, 1, 0, 0),
                new CoordFive(0, 0, 0, -1)
            };

            public static readonly CoordFive[] whitePawnAttack = {
                new CoordFive(1, 1, 0, 0),
                new CoordFive(-1, 1, 0, 0),
                new CoordFive(0, 0, 1, -1),
                new CoordFive(0, 0, -1, -1)
            };

            public static readonly CoordFive[] blackPawnMovement = {
                new CoordFive(0, -1, 0, 0),
                new CoordFive(0, 0, 0, 1)
            };

            public static readonly CoordFive[] blackPawnattack = {
                new CoordFive(1,-1,0,0),
                new CoordFive(-1,-1,0,0),
                new CoordFive(0,0,1,1),
                new CoordFive(0,0,-1,1)
            };

        
        public static readonly CoordFive[] whiteBrawnattack = {
                //Pawn Captures
                new CoordFive(1,1,0,0),
                new CoordFive(-1,1,0,0),
                new CoordFive(0,0,1,-1),
                new CoordFive(0,0,-1,-1),
                //Brawn Specific Captures
                new CoordFive(0,1,-1,0),
                //new CoordFive(0,1,1,0),
                new CoordFive(0,1,0,-1),
                new CoordFive(1,0,0,-1),
                new CoordFive(-1,0,0,-1),
        };
        
        public static readonly CoordFive[] blackBrawnattack = {
                //Pawn Captures
                new CoordFive(1,-1,0,0),
                new CoordFive(-1,-1,0,0),
                new CoordFive(0,0,1,1),
                new CoordFive(0,0,-1,1),
                //Brawn Captures
                new CoordFive(0,-1,-1,0),
                new CoordFive(0,-1,1,0),
                new CoordFive(0,-1,0,1),
                new CoordFive(1,0,0,1),
                new CoordFive(-1,0,0,1),
                
        };
        
        
        //Jank Solution to the fact that pawns and brawns can move in so many ways, for reverse lookup(Searching for a source from a destination)
        //Notice the 1 or two movement. This could fail if there is an inproper configuration, ie. the reverse lookup is destined to fail(this doesnt have any validations)
        //However say 2 pawns are in a line, This code will always find the one in front based of this description(so it should work fine, given that the reverse lookup is
        //non ambiguous and actually exitst)
        public static readonly CoordFive[] whitePawnRLkup = {
                //Pawn Movement
                new CoordFive(0,1,0,0),
                new CoordFive(0,0,0,-1),
                new CoordFive(0,2,0,0),
                new CoordFive(0,0,0,-2),
                //Pawn Attack
                new CoordFive(1,1,0,0),
                new CoordFive(-1,1,0,0),
                new CoordFive(0,0,1,-1),
                new CoordFive(0,0,-1,-1)
        };
        
        public static readonly CoordFive[] blackPawnRLkup = {
                //Pawn Movement
                new CoordFive(0,-1,0,0),
                new CoordFive(0,0,0,1),
                new CoordFive(0,-2,0,0),
                new CoordFive(0,0,0,2),
                //Pawn Attack
                new CoordFive(1,-1,0,0),
                new CoordFive(-1,-1,0,0),
                new CoordFive(0,0,1,1),
                new CoordFive(0,0,-1,1)
        };
        
        public static readonly CoordFive[] whiteBrawnRLkup = {
                //Pawn Movement
                new CoordFive(0,1,0,0),
                new CoordFive(0,0,0,-1),
                new CoordFive(0,2,0,0),
                new CoordFive(0,0,0,-2),
                //Pawn Captures
                new CoordFive(1,1,0,0),
                new CoordFive(-1,1,0,0),
                new CoordFive(0,0,1,-1),
                new CoordFive(0,0,-1,-1),
                //Brawn Specific Captures
                new CoordFive(0,1,-1,0),
                new CoordFive(0,1,1,0),
                new CoordFive(0,1,0,-1),
                new CoordFive(1,0,0,-1),
                new CoordFive(-1,0,0,-1),
        };
        
        public static readonly CoordFive[] blackBrawnRLkup = {
                //Pawn Movement
                new CoordFive(0,-1,0,0),
                new CoordFive(0,0,0,1),
                new CoordFive(0,-2,0,0),
                new CoordFive(0,0,0,2),
                //Pawn Captures
                new CoordFive(1,-1,0,0),
                new CoordFive(-1,-1,0,0),
                new CoordFive(0,0,1,1),
                new CoordFive(0,0,-1,1),
                //Brawn Captures
                new CoordFive(0,-1,-1,0),
                new CoordFive(0,-1,1,0),
                new CoordFive(0,-1,0,1),
                new CoordFive(1,0,0,1),
                new CoordFive(-1,0,0,1),
        };

        //Impossible to avoid magic number antipattern below (or so i think)
        //If I were to do for instantce case board.piece.pawn.ordinal()
        //java complains its no constant :(
        //Perhaps changing this to an if else is something I want to do
        //However, as long as I strictly add new pieces in a predictable pattern, this is fine
        //TODO re add the numpieces. (although i dont imagine ever needing to add in more fairy pieces.)
        /// <summary>
        /// Take a piece and turns it into an array of movement vectors
        /// </summary>
        /// <param name="piece">ordinal integer relating to the piece enum defined in board</param>
        /// <returns>an array of vectors, or empty array if nothing is found.</returns>
        public static CoordFive[] GetMoveVectors(int piece) 
        {
            piece = piece < 0 ? piece * -1 : piece;
            switch(piece) 
            {
            case 1:
                return whitePawnMovement;
            case 13:
                return blackPawnMovement;
            case 2:
            case 14:
                return KNIGHTMOVESET;
            case 3:
            case 15:
                return BISHOPMOVESET;
            case 4:
            case 16:
                return ROOKMOVESET;
            case 5:
            case 17:
                return PRINCESSMOVESET;
            case 6:
            case 18:
                //this case is the queen, but it has the same movement vectors as a king.
                return KINGMOVESET;
            case 7:
            case 19:
                return KINGMOVESET;
            case 8:
            case 20:
                return UnicornMoveset;
            case 9:
            case 21:
                return DragonMoveset;
            case 10:
                return whitePawnMovement;
            case 22:
                return blackPawnMovement;
            case 11:
            case 23://Royal Queen
            case 12:
            case 24://Common King
                return KINGMOVESET;
            case 0:
            default:
                return NULLMOVESET;
            }
        }
        
        /// <summary>
        /// Takes in a piece code and returns whether the piece is a rider.
        /// </summary>
        /// <param name="piece">Piece to check</param>
        /// <returns>returns trus if the piece is a rider and otherwise returns false</returns>
        public static bool PieceIsRider(int piece) 
        {
            piece = piece < 0 ? piece * -1 : piece;
            switch(piece) 
            {
            case 1 + 12://Pawn
            case 1:
            case 2 + 12://Knight
            case 2:
            case 7 + 12://King
            case 7:
            case 10 + 12://Brawn
            case 10:
            case 12 + 12://common king
            case 12:
                return false;
            default: 
                return true;
            }
        }
        
        //Determines who or what piece counts toward checkmate. currently only the king and royal queen but flexable if you want to add more royal pieces.
        /// <summary>
        /// Takes in a piece and returns whether it is royal. Any Royal piece counts toward checkmate. Technically for example can change this to any piece being royal.
        /// </summary>
        /// <param name="piece">piece to check whether not it is considered royal</param>
        /// <returns>boolean, true if royal false otherwise</returns>
        public static bool PieceIsRoyal(int piece) 
        { //TODO replace this with Board.Piece for clarity.
            piece = piece < 0 ? piece * -1 : piece;
            switch(piece) 
            {
            case (int)Board.Piece.WKING + 12://King
            case 7:
            case 11 + 12://Royal Queen
            case 11:
                return true;
            default: 
                return false;
            }
        }

        public bool CanCaptureSquare(GameState g, bool color, CoordFive origin, CoordFive target, int pieceType)
		{
			bool rider = Board.GetColorBool(pieceType);
			if (pieceType <= 0 || pieceType > (Board.NUMTYPES) * 3)
			{
				return false;
			}
			CoordFive vectorTo = CoordFive.Sub(target, origin);
			// TODO finish this function.
			return false;
		}

        public static bool ValidatePath(GameState g, Move m, int piece)
        {
            if (m.Origin.Color != m.Dest.Color) return false;
            int absolute = Math.Abs(piece);
            // Pawn pushes, captures, double steps and en passant have distinct
            // rules. Reuse their small destination list, including brawn travel.
            if (absolute is (int)Board.Piece.WPAWN or (int)Board.Piece.BPAWN or
                (int)Board.Piece.WBRAWN or (int)Board.Piece.BBRAWN)
                return GetMoves(piece, g, m.Origin).Any(dest => dest.EqualsFull(m.Dest));

            // Cached search moves must still fit the piece and current path.
            // Direction lookup avoids regenerating every ray for each candidate.
            int dx = m.Dest.X - m.Origin.X, dy = m.Dest.Y - m.Origin.Y;
            int dt = m.Dest.T - m.Origin.T, dl = m.Dest.L - m.Origin.L;
            if (!AttackDirection(piece, dx, dy, dt, dl, out int distance)) return false;
            int sx = dx / distance, sy = dy / distance, st = dt / distance, sl = dl / distance;
            for (int step = 1; step < distance; step++)
                if (GetAttackSquare(g, m.Origin.X + step * sx, m.Origin.Y + step * sy,
                    m.Origin.T + step * st, m.Origin.L + step * sl, m.Origin.Color) != EMPTYSQUARE)
                    return false;
            return true;
        }

        public static bool ValidateSpatialPath(Board b, int piece, Move attack) 
        {
            CoordFive attackVector = CoordFive.Sub(attack.Dest, attack.Origin);
            attackVector.Flatten();
            CoordFive[] vectors = Math.Abs(piece) switch
            {
                (int)Board.Piece.WPAWN => whitePawnAttack,
                (int)Board.Piece.BPAWN => blackPawnattack,
                (int)Board.Piece.WBRAWN => whiteBrawnattack,
                (int)Board.Piece.BBRAWN => blackBrawnattack,
                _ => GetMoveVectors(piece)
            };
            if (!arrContains(vectors, attackVector))
            {
                return false;
            }
            CoordFive index = attack.Origin.Clone();
            index.Add(attackVector);
            if(!PieceIsRider(piece)) {
                if(index.Equals(attack.Dest)) {
                    return true;
                }
                else {
                    return false;
                }
            }
            while (!index.Equals(attack.Dest)) {
                if (b.GetSquareSafe(index) != EMPTYSQUARE) {
                    return false;
                }
                index.Add(attackVector);
            }//TODO validate if you didn't capture your own piece.
            return true;
        }

        public static List<CoordFive> GetMoves(int piece, GameState g, CoordFive source)
		{
			bool unMoved = false;
			if (piece < 0)
			{
				unMoved = true;
				piece *= -1;
			}
			if (piece == (int)Board.Piece.WPAWN || piece == (int)Board.Piece.BPAWN)
			{
				return GetPawnMoves(piece, g, source, unMoved);
			}
			if (piece == (int)Board.Piece.WBRAWN || piece == (int)Board.Piece.BBRAWN)
			{
				List<CoordFive> moves = GetPawnMoves(piece, g, source, unMoved);
				moves.AddRange(GetCaptures(piece, g, source));
				return moves;
			}
			if (piece == WKING || piece == BKING)
			{
				List<CoordFive> moves = new List<CoordFive>();
				if (unMoved)
				{
					CoordFive? rookLocq = KingCanCastle(g.GetBoard(source), source, true);
					CoordFive? rookLock = KingCanCastle(g.GetBoard(source), source, false);
					if (rookLocq != null)
					{
						moves.Add(rookLocq);
					}
					if (rookLock != null)
					{
						moves.Add(rookLock);
					}
				}
				moves.AddRange(GetLeaperMovesAndCaptures(g, source, GetMoveVectors(piece)));
				return moves;
			}
			if (PieceIsRider(piece))
			{
				return GetRiderMoves(g, source, GetMoveVectors(piece));
			}
			else
			{
				return GetLeaperMovesAndCaptures(g, source, GetMoveVectors(piece));
			}
		}

        public static List<CoordFive> GetRiderMoves(GameState g, CoordFive sourceCoord, CoordFive[] movementVec)
		{
			List<CoordFive> moveList = new List<CoordFive>();
			foreach (CoordFive cf in movementVec)
			{
				if (cf.IsSpatial())
				{
					moveList.AddRange(GetSpatialRiderMoves(g, sourceCoord, cf));
				}
				else
				{
					moveList.AddRange(GetTemporalRiderMoves(g, sourceCoord, cf));
				}
			}
			return moveList;
		}

        public static List<CoordFive> GetTemporalRiderMoves(GameState g, CoordFive sourceCoord, CoordFive movementVec)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			CoordFive currSquare = CoordFive.Add(sourceCoord, movementVec);
			int currPiece = g.GetSquare(currSquare);
			while (currPiece != Board.ERRORSQUARE)
			{
				if (currPiece == EMPTYSQUARE)
				{
					destCoords.Add(currSquare.Clone());
				}
				else
				{
					bool currColor = Board.GetColorBool(currPiece);
					if (currColor == sourceCoord.Color)
					{
						break;
					}
					destCoords.Add(currSquare);
					break;
				}
				currSquare.Add(movementVec);
				currPiece = g.GetSquare(currSquare);
			}
			return destCoords;
		}

        public static List<CoordFive> GetSpatialRiderMoves(GameState g, CoordFive sourceCoord, CoordFive movementVec)//TODO make this say something like GenerateRideMovesSpatial
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			if (!movementVec.IsSpatial())
			{
				return null;
			}
			Board b = g.GetBoard(sourceCoord);
			CoordFive currSquare = CoordFive.Add(sourceCoord, movementVec);
			while (b.IsInBounds(currSquare))
			{
				int currPiece = b.GetSquare(currSquare);
				if (currPiece != EMPTYSQUARE)
				{
					bool currColor = Board.GetColorBool(currPiece);
					if (currColor == sourceCoord.Color)
					{
						break;
					}
					destCoords.Add(currSquare);
					break;
				}
				destCoords.Add(currSquare.Clone());
				currSquare.Add(movementVec);
			}
			return destCoords;
		}

        public static List<CoordFive> GetLeaperMovesAndCaptures(GameState g, CoordFive sourceCoord, CoordFive[] movementVec)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			foreach (CoordFive leap in movementVec)
			{
                CoordFive leapDestination = CoordFive.Add(sourceCoord, leap);
				int piece = g.GetSquare(leapDestination);
				if (piece == Board.ERRORSQUARE)
				{
					continue;
				}
				if (piece == EMPTYSQUARE)
				{
					destCoords.Add(leapDestination);
				}
				else if (Board.GetColorBool(piece) != sourceCoord.Color)
				{
					destCoords.Add(leapDestination);
				}
			}
			return destCoords;
		}

        public static List<CoordFive> GetLeaperCaptures(GameState g, CoordFive sourceCoord, CoordFive[] movementVec)
        {
            var destinations = new List<CoordFive>();
            foreach (CoordFive leap in movementVec)
            {
                int x = sourceCoord.X + leap.X, y = sourceCoord.Y + leap.Y;
                int time = sourceCoord.T + leap.T, layer = sourceCoord.L + leap.L;
                int piece = GetAttackSquare(g, x, y, time, layer, sourceCoord.Color);
                if (piece != EMPTYSQUARE && piece != Board.ERRORSQUARE && Board.GetColorBool(piece) != sourceCoord.Color)
                    destinations.Add(new CoordFive(x, y, time, layer, sourceCoord.Color));
            }
            return destinations;
        }

		public static List<CoordFive> GetLeaperMoves(GameState g, CoordFive sourceCoord, CoordFive[] movementVec)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			foreach (CoordFive leap in movementVec)
			{
				int piece = g.GetSquare(CoordFive.Add(sourceCoord, leap));//TODO Cache Result
				if (piece == Board.ERRORSQUARE)
				{
					continue;
				}
				if (piece == EMPTYSQUARE)
				{
					destCoords.Add(CoordFive.Add(sourceCoord, leap));
				}
			}
			return destCoords;
		}

        private static List<CoordFive> GetPawnMoves(int piece, GameState g, CoordFive source, bool unmoved)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			if (unmoved)
			{
				destCoords.AddRange(GetSliderMoves(g, source, GetMoveVectors(piece), 2));
			}
			else
			{
				destCoords.AddRange(GetLeaperMoves(g, source, GetMoveVectors(piece)));
			}
			CoordFive[] Movementvec;
			if (Board.GetColorBool(piece))
			{
				Movementvec = whitePawnAttack;
			}
			else
			{
				Movementvec = blackPawnattack;
			}
			destCoords.AddRange(GetLeaperCaptures(g, source, Movementvec));
			if (g.GetBoard(source).EnPassentSquare != null)
			{
				CoordFive enPassent = g.GetBoard(source).EnPassentSquare;
				CoordFive left;
				CoordFive right;
				if (source.Color)
				{
					left = CoordFive.Add(source, whitePawnAttack[0]);
					right = CoordFive.Add(source, whitePawnAttack[1]);
				}
				else
				{
					left = CoordFive.Add(source, blackPawnattack[0]);
					right = CoordFive.Add(source, blackPawnattack[1]);
				}
				if (left.SpatialEquals(enPassent))
				{
					destCoords.Add(left);
				}
				else if (right.SpatialEquals(enPassent))
				{
					destCoords.Add(right);
				}
			}
			return destCoords;
		}

        private static List<CoordFive> GetSliderMoves(GameState g, CoordFive sourceCoord, CoordFive[] movementVec, int range)
		{
			if (range <= 0)
            {
				return null;
            }
			List<CoordFive> destCoords = new List<CoordFive>();
			foreach (CoordFive vec in movementVec)
			{
				CoordFive newsrc = CoordFive.Add(sourceCoord, vec);
				int rangeLeft = range - 1;
				while (rangeLeft >= 0 && g.GetSquare(newsrc) == EMPTYSQUARE)
				{
					destCoords.Add(newsrc.Clone());
					rangeLeft--;
					newsrc.Add(vec);
				}
			}
			return destCoords;
		}

		//public static List<CoordFive> GetAllCheckingPieces(GameState g)
		//{
		//	List<CoordFive> attackingPieces = new List<CoordFive>();
		//	foreach (Timeline t in g.Multiverse)
		//	{
		//		if (t.ColorPlayable != g.Color)
		//		{
		//			attackingPieces.AddRange(GetCheckingPieces(g, new CoordFive(0, 0, t.TEnd, t.Layer, !g.Color)));
		//		}
		//	}
		//	return attackingPieces;
		//}

		//public static List<CoordFive> GetCheckingPieces(GameState g, CoordFive spatialCoord)
		//{
		//	List<CoordFive> attackingPieces = new List<CoordFive>();
		//	Board b = g.GetBoard(spatialCoord);
		//	if (b == null)
		//	{
		//		throw new Exception("BOARD IS NULL");;
		//		return attackingPieces;
		//	}
		//	for (int x = 0; x < g.Width; x++)
		//	{
		//		for (int y = 0; y < g.Height; y++)
		//		{
		//			int piece = b.GetSquareSafe(x, y);
		//			if (piece != 0 && Board.GetColorBool(piece) == spatialCoord.Color)
		//			{
		//				CoordFive currSquare = new CoordFive(x, y, spatialCoord.T, spatialCoord.L, spatialCoord.Color);
		//				List<CoordFive> currSquareCaps = GetCaptures(piece, g, currSquare);
		//				foreach (CoordFive square in currSquareCaps)
		//				{
		//					int attackedPiece = g.GetSquare(square);
		//					attackedPiece = attackedPiece < 0 ? attackedPiece * -1 : attackedPiece;
		//					if (PieceIsRoyal(attackedPiece))
		//					{
		//						attackingPieces.Add(currSquare);
		//					}
		//				}
		//			}
		//		}
		//	}

		//	return attackingPieces;
		//}


		//for all the check indicator.
		/// <summary>
		/// 
		/// </summary>
		/// <param name="g">gamestate to check</param>
		/// <param name="color">color to check. For example if true(white) you are checking who is checking white</param>
		/// <returns></returns>
		public static bool HasCurrentThreats(GameState g, bool defender)
		{
			return HasCurrentThreats(g, defender, null);
		}

		private static bool HasCurrentThreats(GameState g, bool defender, CurrentThreatProbe? probe,
			FrontierThreatProbe? frontierProbe = null)
		{
			List<Timeline>? modified = null;
			foreach (Timeline t in g.Multiverse)
			{
				if (t.ColorPlayable == defender && t.Layer >= g.MinActiveTL &&
					t.Layer <= g.MaxActiveTL && t.TEnd <= g.Present)
				{
					t.AddNullMove();
					modified ??= new List<Timeline>();
					modified.Add(t);
				}
			}
			try
			{
				return frontierProbe != null ? frontierProbe.HasPlayableThreats(g) :
					probe == null ? ScanPlayableThreats(g, defender, null) : probe.HasNewThreats(g);
			}
			finally
			{
				if (modified != null)
				{
					foreach (var t in modified)
					{
						t.UndoMove();
					}
				}
			}
		}

		public static List<Move> GetCurrentThreats(GameState g, bool defender)
		{
			List<Timeline>? modified = null;
			foreach (Timeline t in g.Multiverse)
			{
				if (t.ColorPlayable == defender && t.Layer >= g.MinActiveTL &&
					t.Layer <= g.MaxActiveTL && t.TEnd <= g.Present)
				{
					t.AddNullMove();
					modified ??= new List<Timeline>();
					modified.Add(t);
				}
			}
			try
			{
				return GetPlayableThreats(g, defender);
			}
			finally
			{
				if (modified != null)
				{
					foreach (var t in modified)
					{
						t.UndoMove();
					}
				}
			}
		}

		private static List<Move> GetPlayableThreats(GameState g, bool defender)
		{
			List<Move> moves = new List<Move>();
			ScanPlayableThreats(g, defender, moves);
			return moves;
		}

		private readonly record struct PendingAttack(int X, int Y, int Time, int Layer, CoordFive Vector, bool Rider);

		// Valid only for append-only successors while the original position remains unchanged.
		internal sealed class CurrentThreatProbe
		{
			private readonly GameState original;
			private readonly bool defender;
			private readonly bool existingThreat;
			private readonly Dictionary<(int Layer, int Time), List<PendingAttack>> frontier = new();

			internal CurrentThreatProbe(GameState original, bool defender)
			{
				this.original = original;
				this.defender = defender;
				existingThreat = ScanPlayableThreats(original, defender, null, frontier);
			}

			internal bool HasThreats(GameState successor) => HasCurrentThreats(successor, defender, this);

			internal bool HasNewThreats(GameState successor) => existingThreat ||
				MoveGenerator.HasNewThreats(original, successor, defender, frontier);
		}

		// Unlike the own-king probe above, a checking-move probe must discard old
		// attacks when their source frontier advances. Keep the baseline per source
		// board; only an unchanged playable attacker can reuse its frozen paths.
		// The owner keeps the original fixed and supplies append-only successors.
		internal sealed class FrontierThreatProbe(GameState original, bool defender)
		{
			private sealed record LineThreat(bool Existing,
				Dictionary<(int Layer, int Time), List<PendingAttack>> Missing);
			private readonly Dictionary<int, LineThreat> lines = new();

			internal bool HasThreats(GameState successor) =>
				ReferenceEquals(original, successor) ? HasCurrentThreats(successor, defender) :
					HasCurrentThreats(successor, defender, null, this);

			internal bool HasPlayableThreats(GameState successor)
			{
				foreach (Timeline line in successor.Multiverse)
				{
					if (line.ColorPlayable == defender) continue;
					Timeline? previous = original.GetTimeline(line.Layer);
					if (previous == null || previous.ColorPlayable == defender || previous.TEnd != line.TEnd ||
						!ReferenceEquals(previous.GetPlayableBoard(), line.GetPlayableBoard()))
					{
						if (ScanTimelineThreats(successor, line, defender)) return true;
						continue;
					}
					if (!lines.TryGetValue(line.Layer, out LineThreat? cached))
					{
						var missing = new Dictionary<(int Layer, int Time), List<PendingAttack>>();
						cached = new(ScanTimelineThreats(original, previous, defender, null, missing), missing);
						lines.Add(line.Layer, cached);
					}
					if (cached.Existing) return true;
					// Existing squares cannot change in an append-only successor.
					// A previously blocked ray matters only if its first missing
					// board now exists; continue from that square through any new boards.
					foreach (var (board, pending) in cached.Missing)
					{
						if (GetAttackSquare(successor, 0, 0, board.Time, board.Layer, !defender) == Board.ERRORSQUARE)
							continue;
						foreach (PendingAttack attack in pending)
						{
							int x = attack.X, y = attack.Y, time = attack.Time, layer = attack.Layer;
							while (true)
							{
								int target = GetAttackSquare(successor, x, y, time, layer, !defender);
									if (target == Board.ERRORSQUARE) break;
								if (target != EMPTYSQUARE)
								{
									if (Board.GetColorBool(target) == defender && PieceIsRoyal(target)) return true;
									break;
								}
								if (!attack.Rider) break;
								x += attack.Vector.X;
								y += attack.Vector.Y;
								time += attack.Vector.T;
								layer += attack.Vector.L;
							}
						}
					}
				}
				return false;
			}
		}

		// A null output requests only the first threat, without allocating capture lists.
		private static bool ScanPlayableThreats(GameState g, bool defender, List<Move>? moves = null,
			Dictionary<(int Layer, int Time), List<PendingAttack>>? frontier = null)
		{
			bool found = false;
			for (int i = g.MinTL; i <= g.MaxTL; i++)
			{
				Timeline t = g.GetTimeline(i);
				if (t.ColorPlayable != defender && ScanTimelineThreats(g, t, defender, moves, frontier))
				{
					if (moves == null) return true;
					found = true;
				}
			}
			return found;
		}

		private static bool ScanTimelineThreats(GameState g, Timeline t, bool defender, List<Move>? moves = null,
			Dictionary<(int Layer, int Time), List<PendingAttack>>? frontier = null)
		{
			bool found = false;
			int i = t.Layer;
			Board b = t.GetPlayableBoard();
			for (int x = 0; x < g.Width; x++)
			{
				for (int y = 0; y < g.Height; y++)
				{
					int piece = b.GetSquareSafe(x, y);
					if (piece != EMPTYSQUARE && Board.GetColorBool(piece) != defender)
					{
						piece = Math.Abs(piece);
						CoordFive[] vectors = piece switch
						{
							(int)Board.Piece.WPAWN => whitePawnAttack,
							(int)Board.Piece.BPAWN => blackPawnattack,
							(int)Board.Piece.WBRAWN => whiteBrawnattack,
							(int)Board.Piece.BBRAWN => blackBrawnattack,
							_ => GetMoveVectors(piece)
						};
						bool rider = PieceIsRider(piece);
						foreach (CoordFive vector in vectors)
						{
							int dx = x + vector.X;
							int dy = y + vector.Y;
							int time = t.TEnd + vector.T;
							int layer = i + vector.L;
							bool spatial = vector.IsSpatial();
							while (true)
							{
								int target = spatial ? b.GetSquareSafe(dx, dy) :
									GetAttackSquare(g, dx, dy, time, layer, !defender);
								if (target == Board.ERRORSQUARE)
								{
									if (frontier != null && dx >= 0 && dx < g.Width && dy >= 0 && dy < g.Height)
									{
										if (!frontier.TryGetValue((layer, time), out var pending))
										{
											pending = new List<PendingAttack>();
											frontier.Add((layer, time), pending);
										}
										pending.Add(new PendingAttack(dx, dy, time, layer, vector, rider));
									}
									break;
								}
								if (target != EMPTYSQUARE)
								{
									if (Board.GetColorBool(target) == defender && PieceIsRoyal(target))
									{
										if (moves == null) return true;
										moves.Add(new Move(new CoordFive(x, y, t.TEnd, i, !defender),
											new CoordFive(dx, dy, time, layer, !defender)));
										found = true;
									}
									break;
								}
								if (!rider) break;
								dx += vector.X;
								dy += vector.Y;
								time += vector.T;
								layer += vector.L;
							}
						}
					}
				}
			}
			return found;
		}

		private static bool HasNewThreats(GameState original, GameState successor, bool defender,
			Dictionary<(int Layer, int Time), List<PendingAttack>> frontier)
		{
			// Moves append boards, leaving existing opponent-playable boards and their capture paths intact.
			// Only new attackers and paths previously stopped by a missing board can introduce new threats.
			foreach (Timeline t in successor.Multiverse)
			{
				if (t.ColorPlayable == defender) continue;
				Timeline? previous = original.GetTimeline(t.Layer);
				if (previous != null && previous.ColorPlayable != defender) continue;
				if (ScanTimelineThreats(successor, t, defender)) return true;
				if (!frontier.TryGetValue((t.Layer, t.TEnd), out var pending)) continue;
				foreach (PendingAttack attack in pending)
				{
					int x = attack.X, y = attack.Y, time = attack.Time, layer = attack.Layer;
					while (true)
					{
						int target = GetAttackSquare(successor, x, y, time, layer, !defender);
						if (target == Board.ERRORSQUARE) break;
						if (target != EMPTYSQUARE)
						{
							if (Board.GetColorBool(target) == defender && PieceIsRoyal(target)) return true;
							break;
						}
						if (!attack.Rider) break;
						x += attack.Vector.X;
						y += attack.Vector.Y;
						time += attack.Vector.T;
						layer += attack.Vector.L;
					}
				}
			}
			return false;
		}

		private static int GetAttackSquare(GameState g, int x, int y, int time, int layer, bool color)
		{
			if (x < 0 || x >= g.Width || y < 0 || y >= g.Height || layer < g.MinTL || layer > g.MaxTL)
			{
				return Board.ERRORSQUARE;
			}
			Timeline timeline = g.Multiverse[layer - g.MinTL];
			if (color)
			{
				return time < timeline.WhiteStart || time > timeline.WhiteEnd ? Board.ERRORSQUARE :
					timeline.WBoards[time - timeline.WhiteStart].GetSquare(x, y);
			}
			return time < timeline.BlackStart || time > timeline.BlackEnd ? Board.ERRORSQUARE :
				timeline.BBoards[time - timeline.BlackStart].GetSquare(x, y);
		}

		private static bool IsKingSafeAfterMove(GameState g, GameState successor, Move move,
			Dictionary<(int Layer, int Time), List<PendingAttack>> frontier)
		{
			Timeline origin = successor.GetTimeline(move.Origin.L)!;
			Timeline dest = successor.GetTimeline(move.Dest.L)!;
			int originBoards = origin.WBoards.Count + origin.BBoards.Count;
			int destBoards = dest.WBoards.Count + dest.BBoards.Count;
			int minActive = successor.MinActiveTL, maxActive = successor.MaxActiveTL;
			try
			{
				int piece = Math.Abs(g.GetSquare(move.Origin));
				if ((piece == WKING || piece == BKING) && move.Type == Move.SPATIALMOVE &&
					Math.Abs(move.Dest.X - move.Origin.X) == 2)
				{
					origin.AddNullMove();
					bool inCheck = HasNewThreats(g, successor, move.Origin.Color, frontier);
					origin.UndoMove();
					if (inCheck) return false;
					CoordFive transit = move.Origin.Clone();
					transit.X += Math.Sign(move.Dest.X - move.Origin.X);
					if (!IsKingSafeAfterMove(g, successor, new Move(move.Origin.Clone(), transit), frontier))
					{
						return false;
					}
				}
				if (!successor.MakeSilentMove(move.Clone())) return false;
				// Unplayed boards can still answer their own checks later in this multi-board turn.
				return !HasNewThreats(g, successor, move.Origin.Color, frontier);
			}
			finally
			{
				// Reuse only this private simulation; never apply/undo candidates on the caller's game.
				while (successor.MinTL < g.MinTL)
				{
					successor.Multiverse.RemoveAt(0);
					successor.MinTL++;
				}
				while (successor.MaxTL > g.MaxTL)
				{
					successor.Multiverse.RemoveAt(successor.Multiverse.Count - 1);
					successor.MaxTL--;
				}
				while (origin.WBoards.Count + origin.BBoards.Count > originBoards) origin.UndoMove();
				while (dest.WBoards.Count + dest.BBoards.Count > destBoards) dest.UndoMove();
				successor.MinActiveTL = minActive;
				successor.MaxActiveTL = maxActive;
			}
		}

		/// <summary>Gets all moves from a playable board.</summary>
		public static List<Move> GetAllMoves(GameState g, CoordFive boardCoord)
		{
			List<Move> moves = new List<Move>();
			Board b = g.GetBoard(boardCoord);
			Timeline timeline = g.GetTimeline(boardCoord.L);
			
			if (b == null || timeline == null || boardCoord.Color != g.Color ||
				!timeline.IsMostRecentTime(boardCoord.T, boardCoord.Color))
			{
				return new List<Move>();
			}
			for (int x = 0; x < g.Width; x++)
			{
				for (int y = 0; y < g.Height; y++)
				{					
					int piece = b.GetSquareSafe(x, y);
					if (piece != EMPTYSQUARE && Board.GetColorBool(piece) == boardCoord.Color)
					{
						CoordFive srcLocation = new CoordFive(x, y, boardCoord.T, boardCoord.L ,boardCoord.Color);
						List<CoordFive> moveLocations = GetMoves(piece, g, srcLocation.Clone());
						if (moveLocations == null)
						{
							continue;
						}
						foreach (CoordFive dest in moveLocations)
						{
							var move = new Move(srcLocation.Clone(), dest);
							// Movement-only candidates; production search also applies RoyalLegality.
							// Castling cannot start in, pass through, or finish in check.
							if (!IsCastlingMove(piece, move) || CanCastle(g, move)) moves.Add(move);
						}
					}
				}
			}
			return moves;
		}

	

		public static List<CoordFive> GetCaptures(int piece, GameState g, CoordFive source)
		{
			if (piece < 0)
			{
				piece *= -1;
			}
			if (piece == (int)Board.Piece.WPAWN)
			{
				return GetLeaperCaptures(g, source, whitePawnAttack);
			}
			if (piece == (int)Board.Piece.BPAWN)
			{
				return GetLeaperCaptures(g, source, blackPawnattack);
			}
			if (piece == (int)Board.Piece.WBRAWN)
			{
				return GetLeaperCaptures(g, source, whiteBrawnattack);
			}
			if (piece == (int)Board.Piece.BBRAWN)
			{
				return GetLeaperCaptures(g, source, blackBrawnattack);
			}
			if (PieceIsRider(piece))
			{
				return GetRiderCaptures(g, source, GetMoveVectors(piece));
			}
			else
			{
				return GetLeaperCaptures(g, source, GetMoveVectors(piece));
			}
		}

        private static List<CoordFive> GetRiderCaptures(GameState g, CoordFive source, CoordFive[] moveVectors)
        {
            var captures = new List<CoordFive>();
            Board? spatialBoard = null;
            foreach (CoordFive vector in moveVectors)
            {
                int x = source.X + vector.X, y = source.Y + vector.Y;
                int time = source.T + vector.T, layer = source.L + vector.L;
                bool spatial = vector.IsSpatial();
                if (spatial) spatialBoard ??= g.GetBoard(source);
                // Most directions terminate without a capture. Keep the ray in
                // scalar coordinates and allocate only a returned destination;
                // resolve the source board once for all eight spatial rays.
                while (true)
                {
                    int piece = spatial ? spatialBoard!.GetSquareSafe(x, y) :
                        GetAttackSquare(g, x, y, time, layer, source.Color);
                    if (piece == Board.ERRORSQUARE) break;
                    if (piece != EMPTYSQUARE)
                    {
                        if (Board.GetColorBool(piece) != source.Color)
                            captures.Add(new CoordFive(x, y, time, layer, source.Color));
                        break;
                    }
                    x += vector.X; y += vector.Y;
                    time += vector.T; layer += vector.L;
                }
            }
            return captures;
        }

		public static List<CoordFive>[] GetRiderMovesAndCaps(GameState g, CoordFive sourceCoord, CoordFive[] movementVec)
		{
			List<CoordFive> moveList = new List<CoordFive>();
			List<CoordFive> capList = new List<CoordFive>();
			foreach (CoordFive cf in movementVec)
			{
				if (cf.IsSpatial())
				{
					List<CoordFive>[] list = GetSpatialRiderMovesAndCaptures(g, sourceCoord, cf);
					moveList.AddRange(list[0]);
					capList.AddRange(list[1]);
				}
				else
				{
					List<CoordFive>[] list = MoveGenerator.GetTemporalRiderMovesAndCaptures(g, sourceCoord, cf);
					moveList.AddRange(list[0]);
					capList.AddRange(list[1]);
				}
			}
			return new List<CoordFive>[] { moveList, capList };
		}

		public static List<CoordFive>[] GetSpatialRiderMovesAndCaptures(GameState g, CoordFive sourceCoord, CoordFive movementVec)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			List<CoordFive> capCoords = new List<CoordFive>();
			if (!movementVec.IsSpatial())
			{
				return null;
			}
			Board b = g.GetBoard(sourceCoord);
			CoordFive currSquare = CoordFive.Add(sourceCoord, movementVec);
			while (b.IsInBounds(currSquare))
			{
				int currPiece = b.GetSquare(currSquare);
				if (currPiece != EMPTYSQUARE)
				{
					bool currColor = Board.GetColorBool(currPiece);
					if (currColor == sourceCoord.Color)
					{
						break;
					}
					destCoords.Add(currSquare.Clone());
					capCoords.Add(currSquare.Clone());
					break;
				}
				destCoords.Add(currSquare.Clone());
				currSquare.Add(movementVec);
			}
			return new List<CoordFive>[] { destCoords, capCoords };
		}
 
		private static List<CoordFive>[] GetTemporalRiderMovesAndCaptures(GameState g, CoordFive sourceCoord, CoordFive movementVec)
		{
			List<CoordFive> destCoords = new List<CoordFive>();
			List<CoordFive> capCoords = new List<CoordFive>();
			CoordFive currSquare = CoordFive.Add(sourceCoord, movementVec);
			int currPiece = g.GetSquare(currSquare);
			while (currPiece != Board.ERRORSQUARE)
			{
				if (currPiece == EMPTYSQUARE)
				{
					destCoords.Add(currSquare.Clone());
				}
				else
				{
					bool currColor = Board.GetColorBool(currPiece);
					if (currColor == sourceCoord.Color)
					{
						break;
					}
					destCoords.Add(currSquare.Clone());
					capCoords.Add(currSquare.Clone());
					break;
				}
				currSquare.Add(movementVec);
				currPiece = g.GetSquare(currSquare);
			}
			return new List<CoordFive>[] { destCoords, capCoords };
		}

        internal static bool IsCastlingMove(int piece, Move move) =>
            (Math.Abs(piece) == WKING || Math.Abs(piece) == BKING) &&
            move.Type == Move.SPATIALMOVE && move.Origin.L == move.Dest.L &&
            move.Origin.T == move.Dest.T && move.Origin.Color == move.Dest.Color &&
            move.Origin.Y == move.Dest.Y && Math.Abs(move.Dest.X - move.Origin.X) == 2;

        // Shared by generation and execution so malformed castling cannot scan beyond the board.
        internal static CoordFive? GetCastlingRook(Board board, Move move)
        {
            int king = board.GetSquareSafe(move.Origin);
            int expectedKing = move.Origin.Color ? WKING : BKING;
            if (king != -expectedKing || !IsCastlingMove(king, move) ||
                board.GetSquareSafe(move.Dest) != EMPTYSQUARE) return null;
            int direction = Math.Sign(move.Dest.X - move.Origin.X);
            var rook = move.Origin.Clone();
            rook.X += direction;
            while (board.GetSquareSafe(rook) == EMPTYSQUARE) rook.X += direction;
            int expectedRook = move.Origin.Color ? (int)Board.Piece.WROOK : (int)Board.Piece.BROOK;
            return board.GetSquareSafe(rook) == -expectedRook && Math.Abs(rook.X - move.Origin.X) > 2
                ? rook : null;
        }

        public static CoordFive? KingCanCastle(Board b, CoordFive kingSquare, bool kside)
        {
            if (b == null) return null;
            var destination = kingSquare.Clone();
            int direction = kside ? 1 : -1;
            destination.X += 2 * direction;
            if (GetCastlingRook(b, new Move(kingSquare, destination)) == null) return null;
            var target = kingSquare.Clone();
            for (int step = 0; step <= 2; step++, target.X += direction)
                if (IsSquareAttacked(b, target)) return null;
            return destination;
        }

        internal static bool CanCastle(GameState game, Move move)
        {
            if (!IsCastlingMove(game.GetSquare(move.Origin), move)) return false;
            var line = game.GetTimeline(move.Origin.L);
            if (line == null || !line.IsMostRecentTime(move.Origin.T, move.Origin.Color)) return false;
            var destination = KingCanCastle(line.GetPlayableBoard(), move.Origin, move.Dest.X > move.Origin.X);
            if (destination == null || !destination.EqualsFull(move.Dest)) return false;
            var frontier = new Dictionary<(int Layer, int Time), List<PendingAttack>>();
            if (ScanPlayableThreats(game, move.Origin.Color, null, frontier)) return false;
            return IsKingSafeAfterMove(game, game.Clone(), move, frontier);
        }
      
        // Exact movement-vector membership without building a Move/CoordFive or
        // scanning every queen vector for every piece/target pair. Entries cover
        // all spatial and temporal directions, including asymmetric pawn attacks.
        private static readonly bool[][] attackDirections = CreateAttackDirections();
        private readonly record struct SpatialAttack(int X, int Y, uint Riders, uint Leapers);
        private static readonly SpatialAttack[] spatialAttacks = CreateSpatialAttacks();

        private static SpatialAttack[] CreateSpatialAttacks()
        {
            var result = new List<SpatialAttack>();
            for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
            {
                uint riders = 0, leapers = 0;
                for (int piece = 1; piece < attackDirections.Length; piece++)
                    if (attackDirections[piece][(((x + 2) * 5 + y + 2) * 5 + 2) * 5 + 2])
                    {
                        if (PieceIsRider(piece)) riders |= 1u << piece;
                        else leapers |= 1u << piece;
                    }
                if ((riders | leapers) != 0) result.Add(new(x, y, riders, leapers));
            }
            return result.ToArray();
        }

        private static bool[][] CreateAttackDirections()
        {
            var table = new bool[2 * Board.NUMTYPES + 1][];
            for (int piece = 0; piece < table.Length; piece++)
            {
                table[piece] = new bool[625];
                var vectors = piece switch
                {
                    (int)Board.Piece.WPAWN => whitePawnAttack,
                    (int)Board.Piece.BPAWN => blackPawnattack,
                    (int)Board.Piece.WBRAWN => whiteBrawnattack,
                    (int)Board.Piece.BBRAWN => blackBrawnattack,
                    _ => GetMoveVectors(piece)
                };
                foreach (var v in vectors)
                    table[piece][(((v.X + 2) * 5 + v.Y + 2) * 5 + v.T + 2) * 5 + v.L + 2] = true;
            }
            return table;
        }

        private static bool AttackDirection(int piece, int dx, int dy, int dt, int dl, out int distance)
        {
            piece = Math.Abs(piece);
            distance = 1;
            if (piece == 0 || piece >= attackDirections.Length) return false;
            if (PieceIsRider(piece))
            {
                distance = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Max(Math.Abs(dt), Math.Abs(dl)));
                if (distance == 0 || dx % distance != 0 || dy % distance != 0 || dt % distance != 0 || dl % distance != 0)
                    return false;
                dx /= distance; dy /= distance; dt /= distance; dl /= distance;
            }
            if (dx < -2 || dx > 2 || dy < -2 || dy > 2 || dt < -2 || dt > 2 || dl < -2 || dl > 2) return false;
            return attackDirections[piece][(((dx + 2) * 5 + dy + 2) * 5 + dt + 2) * 5 + dl + 2];
        }

        public static bool IsSquareAttacked(Board b, CoordFive target, bool attackerColor) =>
            IsSquareAttacked(b, target, attackerColor, -1);

        // Evaluate a spatial attack with one board square treated as empty.
        // Royal-pressure analysis uses this to expose rays through the royal's
        // origin without cloning and mutating the complete board.
        internal static bool IsSquareAttacked(Board b, CoordFive target, bool attackerColor, int ignoredSquare)
        {
            if (b == null) return false;
            // Trace incoming rays from the target. Only the first occupied square
            // can be a rider; leapers have exactly one possible origin per vector.
            // These masks come from the same vectors as the forward move checks.
            uint whitePieces = (1u << (Board.NUMTYPES + 1)) - 2;
            uint colorMask = attackerColor ? whitePieces : whitePieces << Board.NUMTYPES;
            foreach (var attack in spatialAttacks)
            {
                int x = target.X - attack.X, y = target.Y - attack.Y;
                uint allowed = (attack.Riders | attack.Leapers) & colorMask;
                while (x >= 0 && x < b.Width && y >= 0 && y < b.Height)
                {
                    int index = y * b.Width + x;
                    int piece = index == ignoredSquare ? EMPTYSQUARE : b.Brd[index];
                    if (piece != EMPTYSQUARE)
                    {
                        int absolute = Math.Abs(piece);
                        if (absolute < attackDirections.Length && (allowed & (1u << absolute)) != 0) return true;
                        break;
                    }
                    allowed = attack.Riders & colorMask;
                    if (allowed == 0) break;
                    x -= attack.X; y -= attack.Y;
                }
            }
            return false;
        }

        // Spatial exchange analysis needs one named piece's attack, not every
        // attack on the board. Avoid allocating a Move and normalized coordinates.
        internal static bool AttacksSpatial(Board board, int piece, int x, int y, CoordFive target)
        {
            int dx = target.X - x, dy = target.Y - y;
            if (!AttackDirection(piece, dx, dy, 0, 0, out int distance)) return false;
            int stepX = dx / distance, stepY = dy / distance;
            for (int step = 1; step < distance; step++)
                if (board.GetSquareSafe(x + step * stepX, y + step * stepY) != EMPTYSQUARE) return false;
            return true;
        }

        private static bool IsSquareAttacked(Board b, CoordFive target) => IsSquareAttacked(b, target, !target.Color);

        public static bool IsSquareAttacked(GameState state, CoordFive target, bool attackerColor)
        {
            if (state == null || target == null) return false;
            Board b = state.GetBoard(target);
            if (b != null && IsSquareAttacked(b, target, attackerColor))
            {
                return true;
            }

            for (int l = state.MinTL; l <= state.MaxTL; l++)
            {
                Timeline t = state.GetTimeline(l);
                if (t == null) continue;
                if (t.Layer == target.L && t.TEnd == target.T) continue;
                Board tb = t.GetPlayableBoard();
                if (tb == null) continue;
                int dt = target.T - t.TEnd, dl = target.L - t.Layer;
                int timeDistance = Math.Abs(dt), layerDistance = Math.Abs(dl);
                int temporalDistance = Math.Max(timeDistance, layerDistance);
                bool riderPossible = dt == 0 || dl == 0 || timeDistance == layerDistance;
                if (temporalDistance > 2 && !riderPossible) continue;
                // Temporal displacement fixes a rider's ray length. Its spatial
                // displacement can only be -distance, 0 or +distance. Nearer
                // boards also admit leapers, whose coordinates are within +/-2.
                int radius = temporalDistance > 2 ? temporalDistance : 2;
                int stride = temporalDistance > 2 ? temporalDistance : 1;
                for (int dx = -radius; dx <= radius; dx += stride)
                {
                    int x = target.X - dx;
                    if (x < 0 || x >= state.Width) continue;
                    for (int dy = -radius; dy <= radius; dy += stride)
                    {
                        int y = target.Y - dy;
                        if (y < 0 || y >= state.Height) continue;
                        int piece = tb.GetSquareSafe(x, y);
                        if (piece != EMPTYSQUARE && piece != Board.ERRORSQUARE && Board.GetColorBool(piece) == attackerColor)
                        {
                            if (!AttackDirection(piece, dx, dy, dt, dl, out int distance)) continue;
                            bool clear = true;
                            int stepX = dx / distance, stepY = dy / distance, stepT = dt / distance, stepL = dl / distance;
                            for (int step = 1; step < distance; step++)
                                if (GetAttackSquare(state, x + step * stepX, y + step * stepY,
                                    t.TEnd + step * stepT, t.Layer + step * stepL, attackerColor) != EMPTYSQUARE)
                                { clear = false; break; }
                            if (clear) return true;
                        }
                    }
                }
            }
            return false;
        }


        
        //this will return the origin square given a coord, used for parsing the moves such as Nf3
        //where you would not know the destination and would have to search for it.
        //this only works for spatial moves, really full coordinates should be entered for anything other than a spatial move.
        public static CoordFive ReverseLookup(GameState g, CoordFive destSquare, int pieceType, int rank, int file) {
            Board b = g.GetBoard(destSquare);
            if(b == null) {
                return null;
            }
            if(PieceIsRider(pieceType)) 
            {
                CoordFive[] moveVecs = GetMoveVectors(pieceType);
                foreach(CoordFive vector in moveVecs) 
                {
                    if(vector.IsSpatial()) 
                    {
                        CoordFive result = destSquare.Clone();
                        while(true) 
                        {
                            result = CoordFive.Sub(result, vector);
                            int square = b.GetSquareSafe(result);
                            square = square < 0 ? square * -1 : square;
                            if(square == EMPTYSQUARE) 
                            {
                                continue;
                            }
                            if(square == Board.ERRORSQUARE) 
                            {
                                break;
                            }
                            if(square == pieceType) 
                            {
                                if( (file == -1 || result.X == file) && (rank == -1 || result.Y == rank)) 
                                {
                                    return result;
                                }
                            }
                            break;
                        }
                    }
                }
            }else {
                CoordFive[] moveVecs = GetMoveVectors(pieceType);
                if(pieceType == (int) Board.Piece.WPAWN) 
                {
                    moveVecs = whitePawnRLkup;
                }
                else if(pieceType == (int) Board.Piece.WPAWN + Board.NUMTYPES) 
                {
                    moveVecs = blackPawnRLkup;
                }
                else if(pieceType == (int) Board.Piece.WBRAWN) 
                {
                    moveVecs = whiteBrawnRLkup;
                }
                else if(pieceType == (int) Board.Piece.WBRAWN + Board.NUMTYPES) 
                {
                    moveVecs = blackBrawnRLkup;
                }
                foreach(CoordFive vector in moveVecs) 
                {
                    if(vector.IsSpatial()) 
                    {
                        CoordFive result = CoordFive.Sub(destSquare, vector);
                        if(b.GetSquareSafe(result) == pieceType || b.GetSquareSafe(result) * -1 == pieceType) 
                        {
                            if( (file == -1 || result.X == file) && (rank == -1 || result.Y == rank)) 
                            {
                                return result;
                            }
                        }
                    }
                }
            }
            //Parse For castling
            if(pieceType == WKING || pieceType == BKING) 
            {
                CoordFive[] CastleLkup = {
                        new CoordFive(2,0,0,0),
                        new CoordFive(-2,0,0,0)
                };
                foreach(CoordFive vector in CastleLkup) 
                {
                    CoordFive result = CoordFive.Sub(destSquare, vector);
                    if(b.GetSquareSafe(result) == pieceType || b.GetSquareSafe(result) * -1 == pieceType) 
                    {
                        if( (file == -1 || result.X == file) && (rank == -1 || result.Y == rank)) 
                        {
                            return result;
                        }
                    }
                }		
            }
            return null;
        }

    public static bool arrContains(CoordFive[] array, CoordFive target) 
    {
        foreach (CoordFive element in array) 
        {
            if (element.Equals(target)) 
            {
                return true;
            }
        }
        return false;
    }

    public static CoordFive FindPiece(Board b, int target) 
    {
		for (int x = 0; x < b.Width; x++) 
        {
			for (int y = 0; y < b.Height; y++) 
            {
				if(b.GetSquare(x,y) == target) 
                {
					return new CoordFive(x,y,0,0);
				}
			}
		}
		return null;
	}

    }
}
