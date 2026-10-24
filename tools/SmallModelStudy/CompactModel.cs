using System.Numerics;
using System.Text.Json;
using SkynetZero;

// Experimental distilled evaluator. The original linear path remains intact;
// a small MLP approximates the transformer's nonlinear contribution.
internal sealed class CompactModel
{
    public const int Inputs=107,H1=64,H2=32;
    public const int B1=Inputs*H1,W2=B1+H1,B2=W2+H1*H2,W3=B2+H2,B3=W3+H2,Count=B3+1;
    public sealed record Weights(float[] Parameters,float[] Linear,float Bias,float[] Mean,float[] Scale,int Seed);
    public readonly Weights Data;
    private readonly float[] features=new float[Inputs],input=new float[Inputs],h1=new float[H1],h2=new float[H2];
    public CompactModel(Weights weights)=>Data=weights;
    public static CompactModel Load(string path)=>new(JsonSerializer.Deserialize<Weights>(File.ReadAllText(path))!);
    public void Save(string path)=>File.WriteAllText(path,JsonSerializer.Serialize(Data));
    public float Evaluate(GameState state,bool white){ExtendedEvaluationTermsEncoding.Fill(state,white,features);return Forward(features);}
    public float Forward(float[] raw)
    {
        float orientation=raw[MultiverseEvaluation.TempoIndex]>0?-1:1,score=Data.Bias;
        for(int i=0;i<Inputs;i++)score+=Data.Linear[i]*raw[i]*orientation;
        Encode(raw,input);
        score+=Residual(input,h1,h2);
        return MathF.Tanh(Math.Clamp(score*orientation,-20000f/8700f,20000f/8700f));
    }
    public void Encode(float[] raw,float[] output)
    {
        float orientation=raw[MultiverseEvaluation.TempoIndex]>0?-1:1;
        for(int i=0;i<Inputs;i++){
            float x=raw[i]*orientation;
            output[i]=Math.Clamp((MathF.CopySign(MathF.Log(1+MathF.Abs(x)),x)-Data.Mean[i])*Data.Scale[i],-10,10);
        }
    }
    public float Residual(float[] x,float[] a,float[] b)
    {
        var p=Data.Parameters;
        for(int i=0;i<H1;i++)a[i]=Math.Max(0,Dot(p,i*Inputs,x,Inputs)+p[B1+i]);
        for(int i=0;i<H2;i++)b[i]=Math.Max(0,Dot(p,W2+i*H1,a,H1)+p[B2+i]);
        return Dot(p,W3,b,H2)+p[B3];
    }
    private static float Dot(float[] w,int offset,float[] x,int length)
    {
        int i=0;var sum=Vector<float>.Zero;
        for(;i<=length-Vector<float>.Count;i+=Vector<float>.Count)sum+=new Vector<float>(w,offset+i)*new Vector<float>(x,i);
        float result=Vector.Sum(sum);for(;i<length;i++)result+=w[offset+i]*x[i];return result;
    }
}
