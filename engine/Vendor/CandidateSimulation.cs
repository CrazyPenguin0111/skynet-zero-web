namespace SkynetZero;

// Reuse a private clone only while the source position remains unchanged.
internal sealed class CandidateSimulation
{
    private readonly GameState source;
    private readonly SelfPlayProfile? profile;
    private MoveGenerator.CurrentThreatProbe? whiteThreats;
    private MoveGenerator.CurrentThreatProbe? blackThreats;
    public GameState State { get; }

    public CandidateSimulation(GameState source, SelfPlayProfile? profile = null)
    {
        this.source = source;
        this.profile = profile;
        State = source.Clone();
    }

    public bool HasCurrentThreats(bool defender)
    {
        if (defender != source.Color)
            return MoveGenerator.HasCurrentThreats(State, defender);
        var probe = defender
            ? whiteThreats ??= new MoveGenerator.CurrentThreatProbe(source, true)
            : blackThreats ??= new MoveGenerator.CurrentThreatProbe(source, false);
        return probe.HasThreats(State);
    }

    public MoveScope Apply(Move move, bool validate = false) => new MoveScope(State, move, profile, validate);

    internal readonly struct MoveScope : IDisposable
    {
        private readonly GameState state;
        private readonly Timeline? origin;
        private readonly Timeline? dest;
        private readonly int originBoards;
        private readonly int destBoards;
        private readonly int minTL, maxTL, minActive, maxActive, present;
        private readonly int turnMoves, turnTLS;
        private readonly bool kingWasTaken, kingWasTakenByColor;
        private readonly DrawHistory? drawHistory;
        private readonly SelfPlayProfile? profile;
        public bool Succeeded { get; }

        internal MoveScope(GameState state, Move move, SelfPlayProfile? profile, bool validate)
        {
            this.state = state;
            this.profile = profile;
            origin = state.GetTimeline(move.Origin.L);
            dest = state.GetTimeline(move.Dest.L);
            originBoards = origin == null ? 0 : origin.WBoards.Count + origin.BBoards.Count;
            destBoards = dest == null ? 0 : dest.WBoards.Count + dest.BBoards.Count;
            minTL = state.MinTL;
            maxTL = state.MaxTL;
            minActive = state.MinActiveTL;
            maxActive = state.MaxActiveTL;
            present = state.Present;
            turnMoves = state.TurnMoves.Count;
            turnTLS = state.TurnTLS.Count;
            kingWasTaken = state.kingWasTaken;
            kingWasTakenByColor = state.kingWasTakenByColor;
            drawHistory = state.DrawHistory;
            Succeeded = false;
            try
            {
                using (profile?.Measure("search-candidate-apply"))
                    Succeeded = state.MakeMove(move, validate);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            using var timing = profile?.Measure("search-candidate-undo");
            while (state.MinTL < minTL)
            {
                state.Multiverse.RemoveAt(0);
                state.MinTL++;
            }
            while (state.MaxTL > maxTL)
            {
                state.Multiverse.RemoveAt(state.Multiverse.Count - 1);
                state.MaxTL--;
            }
            if (origin != null)
                while (origin.WBoards.Count + origin.BBoards.Count > originBoards) origin.UndoMove();
            if (dest != null)
                while (dest.WBoards.Count + dest.BBoards.Count > destBoards) dest.UndoMove();
            state.MinActiveTL = minActive;
            state.MaxActiveTL = maxActive;
            state.Present = present;
            state.kingWasTaken = kingWasTaken;
            state.kingWasTakenByColor = kingWasTakenByColor;
            state.DrawHistory = drawHistory;
            state.TurnMoves.RemoveRange(turnMoves, state.TurnMoves.Count - turnMoves);
            state.TurnTLS.RemoveRange(turnTLS, state.TurnTLS.Count - turnTLS);
        }
    }
}
