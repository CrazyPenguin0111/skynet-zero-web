namespace SkynetZero.Web;

/// <summary>One catalogue for bot games, online games and rating pool keys.</summary>
// Starting arrangements cross-checked with the notation implementation:
// https://github.com/adri326/5dchess-notation/blob/master/parsers/game.js
public static class VariantCatalog {
    public static readonly VariantInfo[] All = [
        new("standard-t0","Turn Zero","Standard position with a turn-zero history board."),
        new("standard","Standard","The original 5D chess starting position."),
        new("reversed-royalty","Reversed Royalty","Royal queens and non-royal kings."),
        new("standard-princess","Princess","Princesses replace queens; promotions use princess, rook, bishop or knight."),
        new("t1.5","T1.5","Standard, with White forbidden from traveling back to turn 1."),
        new("defended-pawn","Defended Pawn","The queen and queenside knight exchange starting squares.")
    ];
    public static VariantInfo Get(string id) => All.SingleOrDefault(v=>v.Id==id&&v.Available)??throw new InvalidDataException("That variant is not available.");
    public static string StartingPgn(string id) {
        Get(id);
        const string standard="r*nbqk*bnr*/p*p*p*p*p*p*p*p*/8/8/8/8/P*P*P*P*P*P*P*P*/R*NBQK*BNR*";
        string board=id switch {
            "standard-princess"=>"r*nbsk*bnr*/p*p*p*p*p*p*p*p*/8/8/8/8/P*P*P*P*P*P*P*P*/R*NBSK*BNR*",
            "reversed-royalty"=>"r*nbycbnr*/p*p*p*p*p*p*p*p*/8/8/8/8/P*P*P*P*P*P*P*P*/R*NBYCBNR*",
            "defended-pawn"=>"r*qbnk*bnr*/p*p*p*p*p*p*p*p*/8/8/8/8/P*P*P*P*P*P*P*P*/R*QBNK*BNR*",
            _=>standard
        };
        string history=id=="standard-t0"?$"[{board}:0:0:b]\n":"";
        string promotions=id=="standard-princess"?"S,N,R,B":id=="reversed-royalty"?"Q,N,R,B,C":"Q,N,R,B";
        string restriction=id=="t1.5"?"[WhiteTurnOneTravel \"forbidden\"]\n":"";
        return $"[Board \"custom\"]\n[Size \"8x8\"]\n[Color \"white\"]\n[Mode \"5D\"]\n[VariantName \"{All.Single(v=>v.Id==id).Name}\"]\n[Promotions \"{promotions}\"]\n{restriction}{history}[{board}:0:1:w]\n*";
    }
}
