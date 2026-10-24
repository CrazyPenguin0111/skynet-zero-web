using System.Diagnostics;

namespace SkynetZero;

public sealed class SelfPlayProfile
{
    private readonly Dictionary<string, (double Milliseconds, int Calls)> phases = new();

    public Scope Measure(string phase) => new(this, phase);

    public void WriteTo(TextWriter output)
    {
        foreach (var (phase, value) in phases)
            output.WriteLine(FormattableString.Invariant($"phase,{phase},{value.Calls},{value.Milliseconds:F3}"));
    }

    public readonly struct Scope : IDisposable
    {
        private readonly SelfPlayProfile profile;
        private readonly string phase;
        private readonly long start;

        internal Scope(SelfPlayProfile profile, string phase)
        {
            this.profile = profile;
            this.phase = phase;
            start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            profile.phases.TryGetValue(phase, out var previous);
            profile.phases[phase] = (previous.Milliseconds + Stopwatch.GetElapsedTime(start).TotalMilliseconds, previous.Calls + 1);
        }
    }
}