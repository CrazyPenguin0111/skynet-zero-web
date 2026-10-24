using System;
using System.Collections.Generic;
using static SkynetZero.Board;
using System.Drawing;

namespace SkynetZero
{
    public class GameState
    {
#if CLONE_IMPACT_STUDY
        // Study-only switch for reproducing the former clone behavior. This
        // symbol is absent from production builds, whose clones always retain
        // unfinished-turn state.
        internal static bool LegacyCloneModeForStudy;
#endif
        public static readonly bool WHITE = true;
        public static readonly bool BLACK = false;

        public List<Timeline> Multiverse;
        public bool EvenStart;
        // External 5DUCI matches use the opponent protocol's queen-only rule.
        // Ordinary training keeps the existing promotion choices.
        internal bool QueenPromotionOnly;
        // Browser adapter: preserve PGN variant promotion restrictions. Zero retains native defaults.
        internal int PromotionMask;
        internal bool PromotionAllowed(int type) => PromotionMask == 0 || (PromotionMask & (1 << type)) != 0;
        public bool Color;
        public int StartPresent;
        public int Present;
        public int Width;
        public int Height;
        public int MaxTL;
        public int MinTL;
        // Origin range is fixed for a game. Evaluators distinguish imported
        // initial timelines from branches created later, including after Clone.
        public int InitialMinTL { get; private set; }
        public int InitialMaxTL { get; private set; }
        public int MinActiveTL;
        public int MaxActiveTL;

        public List<Move> TurnMoves = new();
        public List<int> TurnTLS = new();

        public int TLHandicap;

        public bool kingWasTaken = false;
        public bool kingWasTakenByColor;
        internal DrawHistory? DrawHistory;

        public GameState(Timeline[] origins, int width, int height, bool evenStart, bool color, int minTL) :
        this(origins, width, height, evenStart, color, minTL, null)
        { }

        public GameState(Timeline[] origins, int width, int height, bool evenStart, bool color, int minTL,
                        Move[]? moves)
        {
            Multiverse = new List<Timeline>();
            this.Width = width;
            this.Height = height;
            this.MinTL = minTL;
            this.MaxTL = minTL + origins.Length - 1;
            InitialMinTL = this.MinTL;
            InitialMaxTL = this.MaxTL;
            this.Color = color;
            this.EvenStart = evenStart;
            if (evenStart)
            {
                TLHandicap = 1;
            }
            else
            {
                TLHandicap = 0;
            }
            foreach (Timeline t in origins)
            {
                Multiverse.Add(t);
            }
            if (moves == null)
            {
                InitVals();
                return;
            }
            //XXX Dont even think this constructor is used...
            foreach (Move m in moves)
            {
                if (!MakeSilentGenericMove(m))
                {
                    throw new Exception("Moves not properly added to gamestate.");
                }
            }
            InitVals();
        }

        public static void EncodeBoard(Board board, float[] boards, int ltOffset, bool IsWhite)
        {
            int[] brd = board.Brd;
            int height = board.Height;
            int width = board.Width;
            const int FEATURES = 15;
            for (int x = 0; x < height; x++)
            {
                int xOffset = ltOffset + x * (width * FEATURES);
                for (int y = 0; y < width; y++)
                {
                    int baseIdx = xOffset + y * FEATURES;
                    int piece = brd[y * width + x];
                    boards[baseIdx] = 1f;
                    int absPiece = Math.Abs(piece);
                    if (absPiece != 0)
                    {
                        int pt = absPiece > 12 ? absPiece - 12 : absPiece;
                        boards[baseIdx + pt] = 1f;
                        if ((absPiece < 13) == IsWhite)
                        {
                            boards[baseIdx + 13] = 1f;
                        }
                        if (piece > 0)
                        {
                            boards[baseIdx + 14] = 1f;
                        }
                    }
                }
            }
        }

        public void FillGameStateArray(float[] boards, int offset, int[] boardCoordLT, bool IsWhite)
        {
            int maxLine = boardCoordLT[0] + 2;
            int minLine = boardCoordLT[0] - 2;
            
            int minTurn = boardCoordLT[1] - 7;
            int maxTurnMinusOne = boardCoordLT[1];
            
            const int TURNS = 9, HEIGHT = 8, WIDTH = 8, FEATURES = 15;
            const int BOARD_SIZE = HEIGHT * WIDTH * FEATURES;
            
            for (int lineNum = minLine; lineNum <= maxLine; lineNum++)
            {
                var line = GetTimeline(lineNum);
                if (line != null)
                {
                    int l = lineNum - minLine;
                    for (int turnNum = minTurn; turnNum <= maxTurnMinusOne; turnNum++)
                    {
                        Board? board = null;
                        if (IsWhite)
                        {
                            if (turnNum >= line.WhiteStart && turnNum <= line.WhiteEnd)
                            {
                                board = line.WBoards[turnNum - line.WhiteStart];
                            }
                        }
                        else
                        {
                            if (turnNum >= line.BlackStart && turnNum <= line.BlackEnd)
                            {
                                board = line.BBoards[turnNum - line.BlackStart];
                            }
                        }

                        if (board != null)
                        {
                            int t = turnNum - minTurn;
                            int ltOffset = offset + (l * TURNS + t) * BOARD_SIZE;
                            EncodeBoard(board, boards, ltOffset, IsWhite);
                        }
                    }

                    Board? resultingBoard = line.GetPlayableBoard();
                    if (resultingBoard != null)
                    {
                        int ltOffset = offset + (l * TURNS + 8) * BOARD_SIZE;
                        EncodeBoard(resultingBoard, boards, ltOffset, IsWhite);
                    }
                }
            }
        }

        public void FillTurnTensorBase(float[] boards, int offset, int[] boardCoordLT, bool IsWhite)
        {
            int lineNum = boardCoordLT[0];
            var line = GetTimeline(lineNum);
            if (line == null) return;
            
            int minTurn = boardCoordLT[1] - 7;
            int maxTurnMinusOne = boardCoordLT[1];
            const int HEIGHT = 8, WIDTH = 8, FEATURES = 15;
            const int BOARD_SIZE = HEIGHT * WIDTH * FEATURES;

            for (int turnNum = minTurn; turnNum <= maxTurnMinusOne; turnNum++)
            {
                Board? board = null;
                if (IsWhite)
                {
                    if (turnNum >= line.WhiteStart && turnNum <= line.WhiteEnd)
                    {
                        board = line.WBoards[turnNum - line.WhiteStart];
                    }
                }
                else
                {
                    if (turnNum >= line.BlackStart && turnNum <= line.BlackEnd)
                    {
                        board = line.BBoards[turnNum - line.BlackStart];
                    }
                }

                if (board != null)
                {
                    int t = turnNum - minTurn;
                    int tOffset = offset + t * BOARD_SIZE;
                    EncodeBoard(board, boards, tOffset, IsWhite);
                }
            }
        }

        public static Board ApplySpatialMove(Board b, Move m)
        {
            Board newBoard = new Board(b);
            int pieceMoved = newBoard.GetSquare(m.Origin);
            pieceMoved = pieceMoved < 0 ? pieceMoved * -1 : pieceMoved;
            if (m.Type >= Move.PROMOTION)
            {
                pieceMoved = m.SpecialType;
            }
            if (MoveGenerator.IsCastlingMove(pieceMoved, m))
            {
                int king = pieceMoved;
                CoordFive direction = CoordFive.Sub(m.Dest, m.Origin);
                direction.Flatten();
                CoordFive index = MoveGenerator.GetCastlingRook(b, m)
                    ?? throw new ArgumentException("Castling requires an unmoved king and rook with a clear path.", nameof(m));
                int rook = newBoard.GetSquare(index) * -1;
                newBoard.SetSquare(m.Origin, Board.EMPTYSQUARE);
                newBoard.SetSquare(index, Board.EMPTYSQUARE);
                newBoard.SetSquare(m.Dest, king);
                newBoard.SetSquare(CoordFive.Sub(m.Dest, direction), rook);
                return newBoard;
            }
            newBoard.SetSquare(m.Origin, (int)Board.Piece.EMPTY);
            newBoard.SetSquare(m.Dest, pieceMoved);
            if (pieceMoved == (int)Board.Piece.WPAWN && m.Dest.Y == 7)
            {
                newBoard.SetSquare(m.Dest, (int)Board.Piece.WQUEEN);
            }
            if (pieceMoved == (int)Board.Piece.BPAWN && m.Dest.Y == 0)
            {
                newBoard.SetSquare(m.Dest, (int)Board.Piece.BQUEEN);
            }
            return newBoard;
        }

        public void FillTurnTensorArray(float[] boards, int offset, int[] boardCoordLT, bool IsWhite)
        {
            int lineNum = boardCoordLT[0];
            var line = GetTimeline(lineNum);
            if (line == null) return;
            
            int minTurn = boardCoordLT[1] - 7;
            int maxTurnMinusOne = boardCoordLT[1];
            const int HEIGHT = 8, WIDTH = 8, FEATURES = 15;
            const int BOARD_SIZE = HEIGHT * WIDTH * FEATURES;

            for (int turnNum = minTurn; turnNum <= maxTurnMinusOne; turnNum++)
            {
                Board? board = null;
                if (IsWhite)
                {
                    if (turnNum >= line.WhiteStart && turnNum <= line.WhiteEnd)
                    {
                        board = line.WBoards[turnNum - line.WhiteStart];
                    }
                }
                else
                {
                    if (turnNum >= line.BlackStart && turnNum <= line.BlackEnd)
                    {
                        board = line.BBoards[turnNum - line.BlackStart];
                    }
                }

                if (board != null)
                {
                    int t = turnNum - minTurn;
                    int tOffset = offset + t * BOARD_SIZE;
                    EncodeBoard(board, boards, tOffset, IsWhite);
                }
            }

            Board? resultingBoard = line.GetPlayableBoard();
            if (resultingBoard != null)
            {
                int tOffset = offset + 8 * BOARD_SIZE;
                EncodeBoard(resultingBoard, boards, tOffset, IsWhite);
            }
        }

        public void FillLineTensorArray(float[] boards, int offset, int[] boardCoordLT, bool IsWhite)
        {
            int maxLine = boardCoordLT[0] + 2;
            int minLine = boardCoordLT[0] - 2;
            
            const int HEIGHT = 8, WIDTH = 8, FEATURES = 15;
            const int BOARD_SIZE = HEIGHT * WIDTH * FEATURES;

            for (int lineNum = minLine; lineNum <= maxLine; lineNum++)
            {
                var line = GetTimeline(lineNum);
                if (line != null)
                {
                    Board? board = line.GetPlayableBoard();
                    if (board != null)
                    {
                        int l = lineNum - minLine;
                        int lOffset = offset + l * BOARD_SIZE;
                        EncodeBoard(board, boards, lOffset, IsWhite);
                    }
                }
            }
        }


        private void InitVals()
        {
            TurnMoves = new List<Move>();
            TurnTLS = new List<int>();
            DetermineActiveTLS();
            CalcPresent();
            StartPresent = Present;
        }
        public GameState Clone()
        {
            var timelines = new Timeline[Multiverse.Count];
            for (int i = 0; i < Multiverse.Count; i++)
            {
                timelines[i] = new Timeline(Multiverse[i]);
            }
            var clone = new GameState(timelines, this.Width, this.Height, this.EvenStart, this.Color, this.MinTL);
            clone.InitialMinTL = this.InitialMinTL;
            clone.InitialMaxTL = this.InitialMaxTL;
            clone.QueenPromotionOnly = this.QueenPromotionOnly;
            clone.PromotionMask = this.PromotionMask;
            clone.DrawHistory = this.DrawHistory;
#if CLONE_IMPACT_STUDY
            if (LegacyCloneModeForStudy)
            {
                clone.kingWasTaken = this.kingWasTaken;
                clone.kingWasTakenByColor = this.kingWasTakenByColor;
                return clone;
            }
#endif
            // A clone represents the same point inside a multiverse turn.  Search
            // and diagnostics may clone after one board has moved but before the
            // actor can submit; dropping this history changes completion and
            // quiescence semantics even though the board tensor stays identical.
            clone.StartPresent = this.StartPresent;
            clone.TurnMoves.AddRange(this.TurnMoves.Select(move => move.Clone()));
            clone.TurnTLS.AddRange(this.TurnTLS);
            clone.kingWasTaken = this.kingWasTaken;
            clone.kingWasTakenByColor = this.kingWasTakenByColor;
            return clone;
        }

        public int GetTimelineIndex(int layer)
        {
            return layer + (-1 * MinTL);
        }
        
        /// <summary>
        /// Gets timeline of index. Does bounds checking.
        /// </summary>
        /// <param name="layer">layer of timeline to get</param>
        /// <returns>timeline of the index layer</returns>
        public Timeline? GetTimeline(int layer)
        {
            int index = layer + (-1 * MinTL);
            if (index < 0 || index >= Multiverse.Count)
            {
                return null;
            }
            return Multiverse[index];
        }

        /// <summary>
        /// Gets board at temporal coordinate c Does Bounds checking.
        /// </summary>
        /// <param name="c">Coordinate to get</param>
        /// <returns>Board which matches coord.</returns>
        public Board? GetBoard(CoordFive c)
        {
            if (IsInBounds(c))
            {
                return GetTimeline(c.L)!.GetBoard(c);
            }
            return null;
        }

        /// <summary>
        /// Returns the piece int at given square
        /// </summary>
        /// <param name="c">Coord of piece to get</param>
        /// <returns>piece int of the square or error square if out of bounds</returns>
        public int GetSquare(CoordFive c)
        {
            if (IsInBounds(c))
            {
                return GetTimeline(c.L)!.GetSquare(c);
            }
            return Board.ERRORSQUARE;
        }

        /// <summary>
        /// Determines the active timelines and sets internal variables.
        /// should calculate after every branching move.
        /// </summary>
        public void DetermineActiveTLS()
        {
            // case 1 -- black has branched more.
            if (MaxTL < Math.Abs(MinTL))
            {
                MaxActiveTL = MaxTL;
                MinActiveTL = Math.Max(-1 - MaxActiveTL + TLHandicap, MinTL);
            }
            // case 2 -- white has branched more.
            else if (Math.Abs(MinTL) < MaxTL)
            {
                MinActiveTL = MinTL;
                MaxActiveTL = Math.Min(1 + Math.Abs(MinActiveTL) + TLHandicap, MaxTL);
            }
            // case 3 -- they have branched the same # of times
            else
            {
                MinActiveTL = MinTL;
                MaxActiveTL = MaxTL;
            }
        }

        /// <summary>
        /// Calculates the value of the present and sets the present variable in the gameState.
        /// </summary>
        /// <returns>returns the color which the present rests on.</returns>
        public bool CalcPresent()
        {
            Timeline minTL = GetTimeline(MinActiveTL)!;
            int presentTime = minTL.TEnd;
            bool presentColor = minTL.ColorPlayable;
            for (int i = MinActiveTL; i <= MaxActiveTL; i++)
            {
                Timeline? t = GetTimeline(i);
                if (t == null) continue;
                if (t.TEnd < presentTime)
                {
                    presentTime = t.TEnd;
                    presentColor = t.ColorPlayable;
                }
                if (t.TEnd == presentTime && !presentColor && t.ColorPlayable == GameState.WHITE)
                {
                    presentTime = t.TEnd;
                    presentColor = t.ColorPlayable;
                }
            }
            this.Present = presentTime;
            return presentColor;
        }

        /// <summary>
        /// Checks whether coordinate is in bounds
        /// </summary>
        /// <param name="c">c to check</param>
        /// <returns>true if in bounds otherwise false</returns>
        public bool IsInBounds(CoordFive c)
        {
            if (c.L > MaxTL || c.L < MinTL)
            {
                return false;
            }
            if (!Multiverse[c.L + (-1 * MinTL)].TimeExists(c))
            {
                return false;
            }
            if (c.X < 0 || c.X >= Width)
            {
                return false;
            }
            if (c.Y < 0 || c.Y >= Height)
            {
                return false;
            }
            return true;
        }

        public bool LayerExists(int layer)
        {
            return (layer >= MinTL && layer <= MaxTL);
        }

        public bool LayerIsActive(int layer)
        {
            if (layer < MinActiveTL || layer > MaxActiveTL)
            {
                return false;
            }
            return true;
        }

        public bool CoordIsPlayable(CoordFive c)
        {
            if (c == null || !LayerExists(c.L) || c.X < 0 || c.X > this.Width || c.Y < 0 || c.Y > this.Height)
                return false;
            return GetTimeline(c.L)!.IsMostRecentTime(c.T, c.Color);
        }

        protected bool ValidateMove(Move m)
        {
            if (!IsInBounds(m.Origin) || !IsInBounds(m.Dest) || !CoordIsPlayable(m.Origin) ||
                m.Origin.Color != Color || m.Dest.Color != Color)
            {
                return false;
            }
            Timeline? originTL = GetTimeline(m.Origin.L);
            if (originTL == null || originTL.ColorPlayable != this.Color)
            {
                return false;
            }
            int pieceMoved = this.GetSquare(m.Origin);
            int movedTo = this.GetSquare(m.Dest);
            if (pieceMoved == Board.EMPTYSQUARE || this.Color != Board.GetColorBool(pieceMoved))
            {
                return false;
            }
            if (m.SpecialType == Move.CASTLE || MoveGenerator.IsCastlingMove(pieceMoved, m))
            {
                return (m.SpecialType == Move.NORMALMOVE || m.SpecialType == Move.CASTLE) &&
                    MoveGenerator.CanCastle(this, m);
            }
            if (movedTo != Board.EMPTYSQUARE && Board.GetColorBool(movedTo) == this.Color)
            {
                return false;
            }
            if (m.SpecialType != Move.NORMALMOVE)
            {
                int promotedType = m.SpecialType - (Color ? 0 : Board.NUMTYPES);
                if (!PromotionAllowed(promotedType)) return false;
                if (QueenPromotionOnly && promotedType != (int)Board.Piece.WQUEEN) return false;
                int moverType = Math.Abs(pieceMoved) - (Color ? 0 : Board.NUMTYPES);
                if (promotedType < (int)Board.Piece.WKNIGHT || promotedType > Board.NUMTYPES ||
                    promotedType == (int)Board.Piece.WKING ||
                    moverType is not ((int)Board.Piece.WPAWN or (int)Board.Piece.WBRAWN) ||
                    m.Origin.L != m.Dest.L || m.Origin.T != m.Dest.T ||
                    m.Dest.Y != (Color ? Height - 1 : 0)) return false;
                // Promotion changes the result piece, never its movement path.
            }
            return MoveGenerator.ValidatePath(this, m, pieceMoved);
        }

        /// <summary>
        /// make a move without submitting turn, or effecting the temp move data
        /// structures. (In other words, it becomes up to the caller to handle undo's and
        /// other features. This will do no validation as it should only be called for
        /// setting up positions.
        /// </summary>
        /// <param name="m">Move to Make</param>
        /// <returns>Bool if the move was made.</returns>
        public bool MakeSilentMove(Move m)
        {
            if (!IsInBounds(m.Origin) || !IsInBounds(m.Dest))
            {
                return false;
            }
            if (m.SpecialType >= Move.PROMOTION)
            {
                return GetTimeline(m.Origin.L)!.Promote(m);
            }
            if (m.Type == Move.SPATIALMOVE)
            {
                int pieceMoved = GetSquare(m.Origin);
                pieceMoved = pieceMoved < 0 ? pieceMoved * -1 : pieceMoved;
                if (m.SpecialType == Move.CASTLE || MoveGenerator.IsCastlingMove(pieceMoved, m))
                {
                    return this.GetTimeline(m.Origin.L)!.CastleKing(m);
                }
                bool moveResult = GetTimeline(m.Origin.L)!.AddSpatialMove(m);
                return moveResult;
            }
            Timeline originT = GetTimeline(m.Origin.L)!;
            Timeline destT = GetTimeline(m.Dest.L)!;
            // if (originT.ColorPlayable != this.Color)
            // {
            //     throw new Exception("Move not playable: " + m.ToString());
            // }
            int jumpPieceMoved = originT.AddJumpingMove(m);
            Board? b = destT.AddJumpingMoveDest(m.Dest, jumpPieceMoved);
            if (b == null)
            {
                m.Type = Move.JUMPINGMOVE;
            }
            else
            {
                this.AddTimeline(b, m.Dest);
                m.Type = Move.BRANCHINGMOVE;
                DetermineActiveTLS();
            }
            return true;
        }


        /// <summary>
        /// make a move without submitting turn, or effecting the temp move data
        /// structures. (In other words, it becomes up to the caller to handle undo's and
        /// other features. This will do no validation as it should only be called for
        /// setting up positions. This also doesn't care whose turn it is when moving
        /// </summary>
        /// <param name="m">Move to Make</param>
        /// <returns>Bool if the move was made.</returns>
        public bool MakeSilentGenericMove(Move m)
        {
            if (!IsInBounds(m.Origin) || !IsInBounds(m.Dest))
            {
                return false;
            }
            if (m.SpecialType >= Move.PROMOTION)
            {
                return GetTimeline(m.Origin.L)!.Promote(m);
            }
            if (m.Type == Move.SPATIALMOVE)
            {
                int pieceMoved = GetSquare(m.Origin);
                pieceMoved = pieceMoved < 0 ? pieceMoved * -1 : pieceMoved;
                if (m.SpecialType == Move.CASTLE || MoveGenerator.IsCastlingMove(pieceMoved, m))
                {
                    return this.GetTimeline(m.Origin.L)!.CastleKing(m);
                }
                bool moveResult = GetTimeline(m.Origin.L)!.AddSpatialMove(m);
                return moveResult;
            }
            Timeline originT = GetTimeline(m.Origin.L)!;
            Timeline destT = GetTimeline(m.Dest.L)!;
            int jumpPieceMoved = originT.AddJumpingMove(m);
            Board? b = destT.AddJumpingMoveDest(m.Dest, jumpPieceMoved);
            if (b == null)
            {
                m.Type = Move.JUMPINGMOVE;
            }
            else
            {
                this.AddTimeline(b, m.Dest);
                m.Type = Move.BRANCHINGMOVE;
            }
            return true;
        }


        /// <summary>
        /// Makes a move using MakeSilentMove and then updates the present
        /// Validates the move completely, This move should be used externally.
        /// </summary>
        /// <param name="m">move to make</param>
        /// <returns>true if the move worked, false if it failed.</returns>
        public bool MakeMove(Move m, bool validate = true)
        {//TODO validate Pawns moving to first or last rank and not promoting.
            if (m == null)
            {
                return false;
            }
            if (validate)
            {
                if (!ValidateMove(m))
                {
                    return false;
                }
            }
            int destPiece = GetSquare(m.Dest);
            bool capturedRoyal = destPiece != Board.ERRORSQUARE && Board.IsRoyalPiece(destPiece);
            if (MakeSilentMove(m))
            {
                if (capturedRoyal)
                {
                    kingWasTaken = true;
                    kingWasTakenByColor = m.Origin.Color;
                }
                TurnMoves.Add(m);
                TurnTLS.Add(m.Origin.L);
                if (m.Type == Move.JUMPINGMOVE)
                {
                    TurnTLS.Add(m.Dest.L);
                    bool playableColor = GetTimeline(m.Dest.L)!.ColorPlayable;
                    GetTimeline(m.Dest.L)!.ColorPlayable = playableColor;
                }
                else if (m.Type == Move.BRANCHINGMOVE)
                {
                    //This ends up adding the wrong undo timeline if the destination was opposite color of current color turn.
                    int layerAdded = m.Dest.Color ? MaxTL : MinTL;
                    TurnTLS.Add(layerAdded);
                    bool playableColor = GetTimeline(m.Origin.L)!.ColorPlayable;
                    //GetTimeline(m.Origin.L).ColorPlayable = playableColor;
                    GetTimeline(layerAdded)!.ColorPlayable = playableColor;                    
                }
            }
            else
            {
                return false;
            }
            // bool originPlayableColor = GetTimeline(m.Origin.L).ColorPlayable;
            // GetTimeline(m.Origin.L).ColorPlayable = !originPlayableColor;
            
            CalcPresent();
            return true;
        }

        public bool MakeTurn(Move[] moves, bool silent=false)
        {
            foreach (Move m in moves)
            {
                MakeMove(m, false);//TDOO make this validate later.
            }
            if (silent == true) return true;
            if (!SubmitMoves())
            {
                UndoTempMoves();
                return false;
            }
            //Color = !Color; XXX Seems like this does the same thing as SubmitMoves..?
            return true;
        }

        public virtual bool SubmitMoves()
        {
            bool presColor = CalcPresent();
            if (!(presColor == Color)) 
            {
                if (DrawHistory != null) DrawHistory = DrawRules.Advance(this, DrawHistory);
                TurnTLS.Clear();
                TurnMoves.Clear();
                Color = !Color;
                StartPresent = Present;
                return true;
            }
            return false;
        }

        public void UndoTempMoves()
        {
            bool startcolor = this.Color;
            UndoTurn(TurnTLS.ToArray(), submitted: false);
            //The function may flip color, which we dont want. probably there is better way to handel this.
            this.Color = startcolor;
            TurnTLS.Clear();
            TurnMoves.Clear();
            kingWasTaken = false;
        }

        public bool UndoTurn(int[] tlmoved, bool submitted = true)
        {
            if (tlmoved.Length == 0)
            {
                return false;
            }
            if (submitted && DrawHistory?.Previous is { } priorDrawHistory)
                DrawHistory = priorDrawHistory;
            //This needs to be in reverse... In case you branch multiple times in a turn, will not be able to index properly
            for (int index = tlmoved.Length - 1; index >= 0; index--)
            {
                if (GetTimeline(tlmoved[index])!.UndoMove())
                {
                    // this means that the timeline had only one board.
                    Multiverse.RemoveAt(GetTimelineIndex(tlmoved[index]));
                    if (tlmoved[index] == MaxTL)
                    {
                        MaxTL--;
                    }
                    else if(tlmoved[index] == MinTL)
                    {
                        MinTL++;
                    }
                    else
                    {
                        throw new Exception("Non Outer Timeline Removed: " + tlmoved[index].ToString());
                    }
                }
            }
            Color = !Color;
            DetermineActiveTLS();
            CalcPresent();
            StartPresent = Present;
            return true;
        }

        public bool UndoTurn(List<int> tlmoved)
        {
            return UndoTurn(tlmoved.ToArray());
        }

        //protected bool IsInCheck()
        //{
        //    List<int> nullmoves = new List<int>();
        //    for (int i = MinActiveTL; i < MaxActiveTL; i++)
        //    {
        //        Timeline t = GetTimeline(i);
        //        if (t.ColorPlayable == Color && t.TEnd == StartPresent)
        //        {
        //            t.AddNullMove();
        //            nullmoves.Add(i);
        //        }
        //    }
        //    bool inCheck = OpponentCanCaptureKing();
        //    foreach (int i in nullmoves)
        //    {
        //        GetTimeline(i).UndoMove();
        //    }
        //    return inCheck;
        //}

        //protected bool OpponentCanCaptureKing()
        //{
        //    for (int i = MinTL; i <= MaxTL; i++)
        //    {
        //        Timeline t = GetTimeline(i);
        //        if (t.ColorPlayable != Color)
        //        {
        //            List<CoordFive> attackingPieces = MoveGenerator.GetCheckingPieces(this,
        //            new CoordFive(0, 0, t.TEnd, i, !this.Color));
        //            if (attackingPieces.Count != 0)
        //            {
        //                return true;
        //            }
        //        }
        //    }
        //    return false;
        //}

        private bool ValidateTurn(Move[] turn)
        {
            List<int> affectedTimelines = new List<int>();
            foreach (Move m in turn)
            {
                if (m == null)
                {
                    continue;
                }
                if (MakeSilentMove(m))
                {
                    if (m.Type == Move.SPATIALMOVE)
                    {
                        affectedTimelines.Add(m.Origin.L);
                    }
                    else if (m.Type == Move.JUMPINGMOVE)
                    {
                        affectedTimelines.Add(m.Origin.L);
                        affectedTimelines.Add(m.Dest.L);
                    }
                    else
                    {
                        affectedTimelines.Add(m.Origin.L);
                        if (Color)
                        {
                            affectedTimelines.Add(MaxTL);
                        }
                        else
                        {
                            affectedTimelines.Add(MinTL);
                        }
                    }
                }
                else
                {
                    continue;
                }
            }
            bool result = false;
            if (CalcPresent() != Color)
            {
                result = true;
            }
            if (this.UndoTurn(affectedTimelines.ToArray(), submitted: false))
            {
                Color = !Color;
            }
            return result;
        }

        /// <summary>
        /// A wrapper for the MoveGenerator GetMoves Function. This is used by the UI for ease of use
        /// </summary>
        /// <param name="origin">which piece to get the moves of</param>
        /// <returns>List of moves, empty if the </returns>
        public List<CoordFive> GetPossibleDestinations(CoordFive origin)
        {
            List<CoordFive> returnList = new List<CoordFive>();
            int piece = GetSquare(origin);
            if(piece == Board.EMPTYSQUARE || piece == Board.ERRORSQUARE)
            {
                return returnList;
            }
            return MoveGenerator.GetMoves(piece, this, origin);
        }

        /// <summary>
        /// A wrapper for getcurrent threats for ease of use.
        /// </summary>
        /// <returns></returns>
        public List<Move> GetCurrentThreats()
        {
            return MoveGenerator.GetCurrentThreats(this, this.Color);
        }


        public CoordFive? GetPresentCoordinate(int skip)
        {
            for (int i = MinActiveTL; i <= MaxActiveTL; i++)
            {
                Timeline? t = GetTimeline(i);
                if (t != null && t.ColorPlayable == this.Color && t.TEnd <= Present)
                {
                    return new CoordFive(0, 0, i, t.TEnd, t.ColorPlayable);
                }
            }
            //fallback
            Timeline? fallback = GetTimeline(MinActiveTL);
            if (fallback != null)
            {
                return new CoordFive(0, 0, MinActiveTL, fallback.TEnd, fallback.ColorPlayable);
            }
            return null;
        }

        //public bool IsDecided()
        //{
        //    DetermineActiveTLS();
        //    CalcPresent();
        //    if (OpponentCanCaptureKing())
        //    {
        //        return true;
        //    }
        //    // Get all the moves from the current place 
        //    List<List<Move>> allMoves = new List<List<Move>>();
        //    for (int i = MinTL; i <= MaxTL; i++)
        //    {
        //        List<Move> tlMoves = new List<Move>();
        //        Timeline t = GetTimeline(i);
        //        if (t.ColorPlayable != Color)
        //        {
        //            continue;
        //        }
        //        // the null move marks that the timeline is unmoved. Ie this is the case for
        //        // certain things.(inactive, future timelines etc.)
        //        tlMoves.Add(null);
        //        tlMoves.AddRange(MoveGenerator.GetAllMoves(this, new CoordFive(0, 0, t.TEnd, i, Color)));//TODO Test this line, i made it different from og 
        //        allMoves.Add(tlMoves);
        //    }
        //    //DO a first pass to get rid of any moves that immediatly put you in check
        //    //TODO this is too reductive, as its possible a branching move checks frome the origin TL to the branched TL(i think) and therefore, a different order would
        //    // allow the move to exist, this is simply supposed to look at single moves (like moving the king into the fire of a bishop etc.)
        //    foreach (List<Move> tlMoves in allMoves)
        //    {
        //        int i = 0;
        //        while (i < tlMoves.Count)
        //        {
        //            Move m = tlMoves[i];
        //            if (m == null)
        //            {
        //                i++;
        //                continue;
        //            }
        //            bool stat = this.MakeMove(m, false);
        //            CoordFive loc = new CoordFive(0, 0, m.Origin.T, m.Origin.L, !this.Color);
        //            if (!this.Color)
        //            {
        //                loc.T++;
        //            }
        //            if (MoveGenerator.GetCheckingPieces(this, loc).Count > 0)
        //            {
        //                tlMoves.RemoveAt(i);
        //                i--; //I assume I need this, but maybe not.
        //            }
        //            this.UndoTempMoves();
        //            i++;
        //        }
        //    }
        //    int[] curMove = new int[allMoves.Count];
        //    while (curMove[0] < allMoves[0].Count)
        //    {
        //        Move[] moves = new Move[curMove.Length];
        //        for (int getM = 0; getM < curMove.Length; getM++)
        //        {
        //            moves[getM] = allMoves[getM][curMove[getM]];
        //        }
        //        if (this.ValidatePermutationsReduced(moves))
        //        {
        //            return false;
        //        }
        //        int cursor = 0;
        //        while (true)
        //        {
        //            curMove[cursor]++;
        //            if (curMove[cursor] >= allMoves[cursor].Count)
        //            {
        //                curMove[cursor] = 0;
        //                cursor++;
        //                if (cursor >= curMove.Length)
        //                {
        //                    return true;
        //                }
        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }
        //    }
        //    return true;
        //}

        //public char Winner()
        //{
        //    if (!IsDecided())
        //    {
        //        return '\0';
        //    }
        //    if (FindAllLegalTurn().Count > 0)
        //    {
        //        return 'd';
        //    }
        //    return Color != WHITE ? 'w' : 'b';
        //}

        //public bool ValidatePermutationsReduced(Move[] moves)
        //{
        //    if (ValidateTurn(moves))
        //    {
        //        return true;
        //    }
        //    List<Move> spatialMoves = new List<Move>();
        //    List<Move> nonSpatialMoves = new List<Move>();
        //    foreach (Move m in moves)
        //    {
        //        if (m == null)
        //        {
        //            continue;
        //        }
        //        else if (m.Type == Move.SPATIALMOVE)
        //        {
        //            spatialMoves.Add(m);
        //        }
        //        else
        //        {
        //            nonSpatialMoves.Add(m);
        //        }
        //    }
        //    //The null will be a marker, so that we know where to input the spatial moves.
        //    //we permute all nonspatial moves as required to generate all unique turns
        //    //this optimization reduces duplicate turns createdd
        //    nonSpatialMoves.Add(null);
        //    var nonSpatialArray = nonSpatialMoves;
        //    bool plus = false;
        //    int[] c = new int[nonSpatialArray.Count];
        //    for (int i = 0; i < nonSpatialArray.Count;)
        //    {
        //        if (c[i] < i)
        //        {
        //            if (i % 2 == 0)
        //            {
        //                swap(nonSpatialArray, 0, i);
        //            }
        //            else
        //            {
        //                swap(nonSpatialArray, c[i], i);
        //            }
        //            if (ValidatePermutation(nonSpatialArray, spatialMoves))
        //            {
        //                return true;
        //            }
        //            plus = !plus;
        //            c[i]++;
        //            i = 0;
        //        }
        //        else
        //        {
        //            c[i] = 0;
        //            i++;
        //        }
        //    }
        //    return false;
        //}


        public bool ValidatePermutation(List<Move> nonSpatial, List<Move> spatial)
        {
            Move[] allMoves = new Move[nonSpatial.Count + spatial.Count - 1];
            int nonspatialIndex = 0;
            for (int i = 0; i < allMoves.Length; i++)
            {
                if (nonSpatial[nonspatialIndex] == null)
                {
                    nonspatialIndex++;
                    foreach (Move m in spatial)
                    {
                        allMoves[i++] = m;
                    }
                }
                else
                {
                    allMoves[i] = nonSpatial[nonspatialIndex++];
                }
            }
            return ValidateTurn(allMoves);
        }


        //This is a copy of the mate detection, but returns a move if exists, TODO Test this.
        //public Turn FindLegalTurn()
        //{
        //    DetermineActiveTLS();
        //    CalcPresent();
        //    //if the opponent can already capture the king, then the player has no legal turns from current position
        //    //if (OpponentCanCaptureKing())
        //    //{
        //    //    return null;
        //    //}
        //    // Get all the moves from the current place 
        //    List<List<Move>> allMoves = new List<List<Move>>();
        //    for (int i = MinTL; i <= MaxTL; i++)
        //    {
        //        List<Move> tlMoves = new List<Move>();
        //        Timeline t = GetTimeline(i);
        //        if (t.ColorPlayable != Color)
        //        {
        //            continue;
        //        }
        //        // the null move marks that the timeline is unmoved. Ie this is the case for
        //        // certain things.(inactive, future timelines etc.)
        //        tlMoves.Add(null);
        //        tlMoves.AddRange(MoveGenerator.GetAllMoves(this, new CoordFive(0, 0, t.TEnd, i, Color)));
        //        allMoves.Add(tlMoves);
        //    }
        //    //do a first pass to get rid of any moves that immediatly put you in check
        //    foreach (List<Move> tlMoves in allMoves)
        //    {
        //        int i = 0;
        //        while (i < tlMoves.Count)
        //        {
        //            Move m = tlMoves[i];
        //            if (m == null)
        //            {
        //                i++;
        //                continue;
        //            }
        //            this.MakeMove(m);
        //            CoordFive loc = new CoordFive(0, 0, m.Origin.T, m.Origin.L, !this.Color);
        //            if (!this.Color)
        //            {
        //                loc.T++;
        //            }
        //            if (MoveGenerator.GetCheckingPieces(this, loc).Count > 0)
        //            {
        //                //System.out.println("removed move");
        //                tlMoves.RemoveAt(i);
        //                i--; //I assume I need this, but maybe not.
        //            }
        //            this.UndoTempMoves();
        //            i++;
        //        }
        //    }
        //    int[] curMove = new int[allMoves.Count];
        //    while (curMove[0] < allMoves[0].Count)
        //    {
        //        Move[] moves = new Move[curMove.Length];
        //        for (int getM = 0; getM < curMove.Length; getM++)
        //        {
        //            moves[getM] = allMoves[getM][curMove[getM]];
        //        }
        //        if (this.ValidatePermutationsReduced(moves))
        //        {
        //            //System.out.println(Arrays.toString(moves));
        //            return new Turn(moves);
        //        }
        //        int cursor = 0;
        //        while (true)
        //        {
        //            curMove[cursor]++;
        //            if (curMove[cursor] >= allMoves[cursor].Count)
        //            {
        //                curMove[cursor] = 0;
        //                cursor++;
        //                if (cursor >= curMove.Length)
        //                {
        //                    return null;
        //                }
        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }
        //    }
        //    return null;
        //}

        public List<Move> FindAllLegalMoves(Timeline timeline) => MoveGenerator.GetAllMoves(this, new CoordFive(0, 0, timeline.TEnd, timeline.Layer, Color));

        /// Find all Legal Turns with just putting them in a list instead of instant returning the first legal turn.
        public List<Turn> FindAllLegalTurn()
        {
            DetermineActiveTLS();
            CalcPresent();
            //if the opponent can already capture the king, then the player has no legal turns from current position
            
            // Get all the moves from the current place 
            List<List<Move?>> allMoves = new List<List<Move?>>();
            for (int i = MinTL; i <= MaxTL; i++)
            {
                List<Move?> tlMoves = new List<Move?>();
                Timeline? t = GetTimeline(i);
                if (t == null || t.ColorPlayable != Color)
                {
                    continue;
                }
                // the null move marks that the timeline is unmoved. Ie this is the case for
                // certain things.(inactive, future timelines etc.)
                tlMoves.Add(null);
                tlMoves.AddRange(MoveGenerator.GetAllMoves(this, new CoordFive(0, 0, t.TEnd, i, Color)));
                allMoves.Add(tlMoves);
            }
            //do a first pass to get rid of any moves that immediatly put you in check
            foreach (List<Move?> tlMoves in allMoves)
            {
                int i = 0;
                while (i < tlMoves.Count)
                {
                    Move? m = tlMoves[i];
                    if (m == null)
                    {
                        i++;
                        continue;
                    }
                    this.MakeMove(m);
                    CoordFive loc = new CoordFive(0, 0, m.Origin.T, m.Origin.L, !this.Color);
                    if (!this.Color)
                    {
                        loc.T++;
                    }
                    this.UndoTempMoves();
                    i++;
                }
            }
            int[] curMove = new int[allMoves.Count];
            List<Turn> allTurns = [];
            while (curMove[0] < allMoves[0].Count)
            {
                Move?[] moves = new Move?[curMove.Length];
                for (int getM = 0; getM < curMove.Length; getM++)
                {
                    moves[getM] = allMoves[getM][curMove[getM]];
                }
                if (true) // this.ValidatePermutationsReduced(moves)
                {
                    //System.out.println(Arrays.toString(moves));
                    allTurns.Add(new Turn(moves!));
                }
                int cursor = 0;
                while (true)
                {
                    curMove[cursor]++;
                    if (curMove[cursor] >= allMoves[cursor].Count)
                    {
                        curMove[cursor] = 0;
                        cursor++;
                        if (cursor >= curMove.Length)
                        {
                            return allTurns;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return allTurns;
        }
        /// <summary>
        /// Adds a timeline, updated min and maxTL, updates active timelines.
        /// </summary>
        /// <param name="b">Board to add as a new timeline</param>
        /// <param name="c">move destination that led to the timeline</param>
        /// <returns></returns>
        private int AddTimeline(Board b, CoordFive c)
        {
            if (c.Color)
            {
                MaxTL++;
                Timeline branch = new Timeline(b, !c.Color, c.T, MaxTL);
                Multiverse.Add(branch);
                return MaxTL;
            }
            else
            {
                MinTL--;
                Timeline branch = new Timeline(b, !c.Color, c.T + 1, MinTL);
                Multiverse.Insert(0, branch);
                return MinTL;
            }
        }

        public static void swap<T>(List<T> array, int index1, int index2)
        {
            T obj = array[index1];
            array[index1] = array[index2];
            array[index2] = obj;
        }

        public override string ToString()
        {
            string temp = "";
            temp += "Turn: " + Color.ToString();
            int tl = MinTL;
            foreach (Timeline t in Multiverse)
            {
                temp += "----------------------TL" + tl.ToString() + "----------------------\n";
                temp += t.ToString();
                tl++;
            }
            return temp;
        }
    }
}
