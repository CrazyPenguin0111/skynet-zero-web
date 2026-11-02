namespace SkynetZero.Server;

public readonly record struct RatingState(double Rating=1500,double Deviation=350,double Volatility=.06);
public readonly record struct RatedResult(RatingState Opponent,double Score);

/// <summary>Glickman's Glicko-2 algorithm, tau=0.5. Opponents use pre-period ratings.</summary>
public static class Glicko2 {
    private const double Scale=173.7178,Tau=.5,Epsilon=.000001;
    public static RatingState Age(RatingState rating,double idleDays) => rating with {
        Deviation=Math.Min(350,Scale*Math.Sqrt(Math.Pow(rating.Deviation/Scale,2)+Math.Max(0,idleDays)*rating.Volatility*rating.Volatility))
    };
    public static RatingState Update(RatingState player,IReadOnlyList<RatedResult> results) {
        double mu=(player.Rating-1500)/Scale,phi=player.Deviation/Scale,sigma=player.Volatility;
        if(results.Count==0)return Age(player,1);
        double varianceInverse=0,sum=0;
        foreach(var r in results){
            if(r.Score is <0 or >1 || !double.IsFinite(r.Score))throw new ArgumentOutOfRangeException(nameof(results));
            double g=1/Math.Sqrt(1+3*Math.Pow(r.Opponent.Deviation/Scale,2)/(Math.PI*Math.PI));
            double e=1/(1+Math.Exp(-g*(mu-(r.Opponent.Rating-1500)/Scale)));
            varianceInverse+=g*g*e*(1-e);sum+=g*(r.Score-e);
        }
        double v=1/Math.Max(varianceInverse,1e-15),delta=v*sum,a=Math.Log(sigma*sigma);
        double F(double x){double exp=Math.Exp(x),baseValue=phi*phi+v+exp;return exp*(delta*delta-phi*phi-v-exp)/(2*baseValue*baseValue)-(x-a)/(Tau*Tau);}
        double A=a,B;
        if(delta*delta>phi*phi+v)B=Math.Log(delta*delta-phi*phi-v);
        else {int k=1;while(F(a-k*Tau)<0&&k<10000)k++;B=a-k*Tau;}
        double fA=F(A),fB=F(B);
        for(int i=0;Math.Abs(B-A)>Epsilon&&i<1000;i++){
            double C=A+(A-B)*fA/(fB-fA),fC=F(C);
            if(fC*fB<=0){A=B;fA=fB;}else fA/=2;
            B=C;fB=fC;
        }
        double nextSigma=Math.Exp(A/2),star=Math.Sqrt(phi*phi+nextSigma*nextSigma),nextPhi=1/Math.Sqrt(1/(star*star)+1/v);
        return new(1500+Scale*(mu+nextPhi*nextPhi*sum),Math.Min(350,Scale*nextPhi),nextSigma);
    }
}
