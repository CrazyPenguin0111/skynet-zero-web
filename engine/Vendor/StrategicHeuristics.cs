namespace SkynetZero;

// Feature families identified in Kairos's HCE, independently expressed in
// SkynetZero's bounded value units. These are positional preferences, never
// legality filters or mate proofs; upstream fitted weights are not portable.
internal static class StrategicHeuristics
{
    internal readonly record struct Terms(float Pawns, float Centrality, float Shelter,
        float NeighborPressure, float TimelineReserve, float InactiveMaterial, float ExiledRoyals)
    {
        internal float Total => Math.Clamp(Pawns + Centrality + Shelter + NeighborPressure +
            TimelineReserve + InactiveMaterial + ExiledRoyals, -.12f, .12f);
    }

    internal readonly record struct PawnTerms(int Passed, int Doubled, int Isolated, int NearPromotion);

    // A PositionHeuristics instance owns this cache for one search decision. The
    // engine appends new immutable Boards for moves and restores old references
    // on undo; no mutable GameState identity is used as a cache key here.
    internal sealed class Cache
    {
        private readonly record struct PressureKey(Board? Left, Board? Right, int X, int Y, bool White);
        private readonly Dictionary<Board, BoardTerms> boards = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PressureKey, float> pressures = new();
        internal int AnalyzedBoards => boards.Count;
        internal int AnalyzedPressures => pressures.Count;

        internal BoardTerms Info(Board board)
        {
            if (!boards.TryGetValue(board, out var terms)) boards.Add(board, terms = new(board));
            return terms;
        }

        internal float Pressure(Board? left, Board? right, PieceTerms royal)
        {
            var key = new PressureKey(left, right, royal.X, royal.Y, royal.White);
            if (pressures.TryGetValue(key, out float known)) return known;
            float result = 0;
            // Preserve the original accumulation order: left neighbor, then
            // right neighbor, each in square order. Adding two pre-summed board
            // totals would change floating-point rounding across candidates.
            if (left != null) Add(Info(left));
            if (right != null) Add(Info(right));
            pressures.Add(key, result);
            return result;

            void Add(BoardTerms board)
            {
                foreach (PieceTerms attacker in board.Pieces)
                {
                    if (attacker.White == royal.White || attacker.Pawn || attacker.Royal) continue;
                    int distance = Math.Max(Math.Abs(attacker.X - royal.X), Math.Abs(attacker.Y - royal.Y));
                    if (distance <= 2) result += .002f * (3 - distance);
                }
            }
        }
    }

    internal readonly record struct PieceTerms(int X, int Y, bool White, bool Royal, bool Pawn,
        float Centrality, float Shelter, float Material);

    internal sealed class BoardTerms
    {
        internal readonly float Pawns;
        internal readonly PieceTerms[] Pieces;

        internal BoardTerms(Board board)
        {
            PawnPair(board, out var whitePawns, out var blackPawns);
            Pawns = .006f * (whitePawns.Passed - blackPawns.Passed) - .004f * (whitePawns.Doubled - blackPawns.Doubled) -
                .003f * (whitePawns.Isolated - blackPawns.Isolated) + .012f * (whitePawns.NearPromotion - blackPawns.NearPromotion);
            int count = 0;
            foreach (int piece in board.Brd) if (piece != 0) count++;
            Pieces = new PieceTerms[count];
            int next = 0;
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (piece == 0) continue;
                bool white = Board.GetColorBool(piece), royal = Board.IsRoyalPiece(piece), pawn = Pawnish(piece);
                int sign = white ? 1 : -1, x = square % board.Width, y = square / board.Width;
                float centrality = 0, shelter = 0, material = 0;
                if (royal) shelter = sign * StrategicHeuristics.Shelter(board, x, y, white);
                else
                {
                    material = sign * Board.GetPieceValue(piece);
                    if (!pawn)
                    {
                        float center = 1 - (Math.Abs(2 * x - board.Width + 1) + Math.Abs(2 * y - board.Height + 1)) /
                            (float)Math.Max(1, board.Width + board.Height - 2);
                        int type = (Math.Abs(piece) - 1) % Board.NUMTYPES + 1;
                        centrality = sign * center * (type == (int)Board.Piece.WKNIGHT ? .006f : .002f);
                    }
                }
                Pieces[next++] = new(x, y, white, royal, pawn, centrality, shelter, material);
            }
        }
    }

    internal static Terms Evaluate(GameState state, bool perspective, Cache cache)
    {
        if (state.kingWasTaken) return default;
        float pawns = 0, centrality = 0, shelter = 0, inactive = 0;
        Span<float> danger = stackalloc float[2];
        danger.Clear();
        int activeCount = 0, inactiveCount = 0;
        Span<int> activeRoyals = stackalloc int[2], inactiveRoyals = stackalloc int[2];
        activeRoyals.Clear();
        inactiveRoyals.Clear();
        foreach (Timeline line in state.Multiverse)
        {
            BoardTerms board = cache.Info(line.GetPlayableBoard());
            bool active = state.LayerIsActive(line.Layer);
            if (active) { activeCount++; pawns += board.Pawns; }
            else inactiveCount++;
            Board? left = null, right = null;
            bool haveNeighbors = false;
            foreach (PieceTerms piece in board.Pieces)
            {
                int side = piece.White ? 0 : 1;
                if (piece.Royal)
                {
                    if (active)
                    {
                        activeRoyals[side]++;
                        shelter += piece.Shelter;
                        if (!haveNeighbors)
                        {
                            left = state.GetTimeline(line.Layer - 1)?.GetPlayableBoard();
                            right = state.GetTimeline(line.Layer + 1)?.GetPlayableBoard();
                            haveNeighbors = true;
                        }
                        float pressure = cache.Pressure(left, right, piece);
                        danger[side] = Math.Max(danger[side], Math.Min(.025f, pressure));
                    }
                    else inactiveRoyals[side]++;
                }
                else if (!active) inactive += piece.Material;
                else if (!piece.Pawn) centrality += piece.Centrality;
            }
        }
        int whiteCreated = Math.Max(0, state.MaxTL - (state.EvenStart ? 1 : 0));
        int blackCreated = Math.Max(0, -state.MinTL);
        float reserve = .008f * (Math.Clamp(1 + blackCreated - whiteCreated, 0, 2) -
            Math.Clamp(1 + whiteCreated - blackCreated, 0, 2));
        float exiled = .025f * ((activeRoyals[1] == 0 && inactiveRoyals[1] > 0 ? 1 : 0) -
            (activeRoyals[0] == 0 && inactiveRoyals[0] > 0 ? 1 : 0));
        int count = Math.Max(1, activeCount), perspectiveSign = perspective ? 1 : -1;
        return new(perspectiveSign * pawns / count, perspectiveSign * centrality / count,
            perspectiveSign * shelter / count, perspectiveSign * (danger[1] - danger[0]),
            perspectiveSign * reserve, perspectiveSign * .025f * MathF.Tanh(inactive /
                (Math.Max(1, inactiveCount) * Program.MaximumFinitePieceValue)), perspectiveSign * exiled);
    }

    internal static PawnTerms Pawns(Board board, bool white)
    {
        PawnPair(board, out var whiteTerms, out var blackTerms);
        return white ? whiteTerms : blackTerms;
    }

    private static void PawnPair(Board board, out PawnTerms white, out PawnTerms black)
    {
        // Analyze both colors in two board scans. Per-file counts and enemy
        // rank extrema replace pawn lists and repeated pairwise comparisons.
        int length = checked(4 * board.Width);
        Span<int> scratch = board.Width <= 128 ? stackalloc int[length] : new int[length];
        scratch.Clear();
        Span<int> whiteFiles = scratch[..board.Width], blackFiles = scratch.Slice(board.Width, board.Width);
        Span<int> whiteRanks = scratch.Slice(2 * board.Width, board.Width), blackRanks = scratch.Slice(3 * board.Width, board.Width);
        whiteRanks.Fill(board.Height);
        blackRanks.Fill(-1);
        for (int square = 0; square < board.Brd.Length; square++)
        {
            int piece = board.Brd[square];
            if (!Pawnish(piece)) continue;
            int x = square % board.Width, y = square / board.Width;
            if (Board.GetColorBool(piece))
            { whiteFiles[x]++; whiteRanks[x] = Math.Min(whiteRanks[x], y); }
            else
            { blackFiles[x]++; blackRanks[x] = Math.Max(blackRanks[x], y); }
        }
        int whitePassed = 0, blackPassed = 0, whiteIsolated = 0, blackIsolated = 0;
        int whiteNear = 0, blackNear = 0, whiteDoubled = 0, blackDoubled = 0;
        foreach (int count in whiteFiles) whiteDoubled += Math.Max(0, count - 1);
        foreach (int count in blackFiles) blackDoubled += Math.Max(0, count - 1);
        for (int square = 0; square < board.Brd.Length; square++)
        {
            int piece = board.Brd[square];
            if (!Pawnish(piece)) continue;
            bool isWhite = Board.GetColorBool(piece);
            int x = square % board.Width, y = square / board.Width;
            Span<int> files = isWhite ? whiteFiles : blackFiles;
            Span<int> enemyRanks = isWhite ? blackRanks : whiteRanks;
            bool blocked = false;
            for (int file = Math.Max(0, x - 1); file <= Math.Min(board.Width - 1, x + 1); file++)
                blocked |= isWhite ? enemyRanks[file] > y : enemyRanks[file] < y;
            bool isolated = (x == 0 || files[x - 1] == 0) && (x == board.Width - 1 || files[x + 1] == 0);
            int remaining = isWhite ? board.Height - 1 - y : y;
            int near = remaining is 1 or 2 ? 3 - remaining : 0;
            if (isWhite)
            { if (!blocked) whitePassed++; if (isolated) whiteIsolated++; whiteNear += near; }
            else
            { if (!blocked) blackPassed++; if (isolated) blackIsolated++; blackNear += near; }
        }
        white = new(whitePassed, whiteDoubled, whiteIsolated, whiteNear);
        black = new(blackPassed, blackDoubled, blackIsolated, blackNear);
    }

    internal static Terms Evaluate(GameState state, bool perspective)
    {
        if (state.kingWasTaken) return default;
        float pawns = 0, centrality = 0, shelter = 0, inactive = 0;
        Span<float> danger = stackalloc float[2];
        danger.Clear();
        int activeCount = 0, inactiveCount = 0;
        Span<int> activeRoyals = stackalloc int[2], inactiveRoyals = stackalloc int[2];
        activeRoyals.Clear();
        inactiveRoyals.Clear();
        foreach (Timeline line in state.Multiverse)
        {
            Board board = line.GetPlayableBoard();
            bool active = state.LayerIsActive(line.Layer);
            if (active)
            {
                activeCount++;
                PawnPair(board, out var white, out var black);
                pawns += .006f * (white.Passed - black.Passed) - .004f * (white.Doubled - black.Doubled) -
                    .003f * (white.Isolated - black.Isolated) + .012f * (white.NearPromotion - black.NearPromotion);
            }
            else inactiveCount++;
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (piece == 0) continue;
                bool white = Board.GetColorBool(piece);
                int side = white ? 0 : 1, sign = white ? 1 : -1;
                int x = square % board.Width, y = square / board.Width;
                if (Board.IsRoyalPiece(piece))
                {
                    if (active)
                    {
                        activeRoyals[side]++;
                        shelter += sign * Shelter(board, x, y, white);
                        float pressure = 0;
                        // Timeline storage is ordered and contiguous. Only these two
                        // neighbors contributed to the original scan, in this order.
                        for (int offset = -1; offset <= 1; offset += 2)
                        {
                            Timeline? neighbor = state.GetTimeline(line.Layer + offset);
                            if (neighbor == null) continue;
                            Board other = neighbor.GetPlayableBoard();
                            for (int target = 0; target < other.Brd.Length; target++)
                            {
                                int attacker = other.Brd[target];
                                if (attacker == 0 || Board.GetColorBool(attacker) == white ||
                                    Pawnish(attacker) || Board.IsRoyalPiece(attacker)) continue;
                                int distance = Math.Max(Math.Abs(target % other.Width - x), Math.Abs(target / other.Width - y));
                                // Proximity is potential, not a claim that the
                                // piece has a legal cross-timeline attack now.
                                if (distance <= 2) pressure += .002f * (3 - distance);
                            }
                        }
                        danger[side] = Math.Max(danger[side], Math.Min(.025f, pressure));
                    }
                    else inactiveRoyals[side]++;
                }
                else if (!active) inactive += sign * Board.GetPieceValue(piece);
                else if (!Pawnish(piece))
                {
                    float center = 1 - (Math.Abs(2 * x - board.Width + 1) + Math.Abs(2 * y - board.Height + 1)) /
                        (float)Math.Max(1, board.Width + board.Height - 2);
                    int type = (Math.Abs(piece) - 1) % Board.NUMTYPES + 1;
                    centrality += sign * center * (type == (int)Board.Piece.WKNIGHT ? .006f : .002f);
                }
            }
        }
        int whiteCreated = Math.Max(0, state.MaxTL - (state.EvenStart ? 1 : 0));
        int blackCreated = Math.Max(0, -state.MinTL);
        float reserve = .008f * (Math.Clamp(1 + blackCreated - whiteCreated, 0, 2) -
            Math.Clamp(1 + whiteCreated - blackCreated, 0, 2));
        // An exiled royal is strategically vulnerable, but SkynetZero's
        // capture-royal rules do not make it an automatic stalemate loss.
        float exiled = .025f * ((activeRoyals[1] == 0 && inactiveRoyals[1] > 0 ? 1 : 0) -
            (activeRoyals[0] == 0 && inactiveRoyals[0] > 0 ? 1 : 0));
        int count = Math.Max(1, activeCount), perspectiveSign = perspective ? 1 : -1;
        return new(perspectiveSign * pawns / count, perspectiveSign * centrality / count,
            perspectiveSign * shelter / count, perspectiveSign * (danger[1] - danger[0]),
            perspectiveSign * reserve, perspectiveSign * .025f * MathF.Tanh(inactive /
                (Math.Max(1, inactiveCount) * Program.MaximumFinitePieceValue)), perspectiveSign * exiled);
    }

    private static float Shelter(Board board, int x, int y, bool white)
    {
        int forward = y + (white ? 1 : -1), shield = 0, openFiles = 0;
        for (int file = Math.Max(0, x - 1); file <= Math.Min(board.Width - 1, x + 1); file++)
        {
            if (forward >= 0 && forward < board.Height && OwnPawn(board.GetSquare(file, forward), white)) shield++;
            bool pawnOnFile = false;
            for (int rank = 0; rank < board.Height; rank++)
                pawnOnFile |= OwnPawn(board.GetSquare(file, rank), white);
            if (!pawnOnFile) openFiles++;
        }
        return .003f * shield - .002f * openFiles;
    }

    private static bool OwnPawn(int piece, bool white) => Pawnish(piece) && Board.GetColorBool(piece) == white;
    private static bool Pawnish(int piece) => piece != 0 &&
        (Math.Abs(piece) - 1) % Board.NUMTYPES + 1 is (int)Board.Piece.WPAWN or (int)Board.Piece.WBRAWN;
}
