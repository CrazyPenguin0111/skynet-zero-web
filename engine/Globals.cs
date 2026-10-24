global using static SkynetZero.Globals;
using System.Diagnostics.CodeAnalysis;
namespace SkynetZero;
public static class Globals {
  public static void Assert([DoesNotReturnIf(false)] bool ok, string? message = null) { if (!ok) throw new InvalidOperationException(message ?? "Engine invariant failed"); }
  public static void AssertImplies(bool a, bool b, string? message) => Assert(!a || b, message);
}
