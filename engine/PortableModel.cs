using System.Numerics;
using System.Text.Json;
namespace SkynetZero.Web;

/// <summary>Float32 inference for SkynetZero's v10 term transformer. No retraining or quantization.</summary>
public sealed class PortableModel
{
    public sealed record Tensor(long[] shape, float[] data);
    private readonly Dictionary<string, Tensor> weights;
    private const int Width = 64, Terms = 107;
    private readonly float[] tokens = new float[Terms * Width], qkv = new float[Terms * Width * 3],
        attention = new float[Terms * Width], projected = new float[Terms * Width],
        normalized = new float[Terms * Width], hidden = new float[Terms * 128], scores = new float[Terms],
        features = new float[Terms];
    public PortableModel(Stream stream) => weights = JsonSerializer.Deserialize<Dictionary<string, Tensor>>(stream)
        ?? throw new InvalidDataException("Missing model weights.");
    private float[] W(string name) => weights[name].data;
    public static PortableModel LoadBundled() => new(typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.Model.json")
        ?? throw new InvalidDataException("The bundled SkynetZero checkpoint is missing."));
    public float Evaluate(GameState state, bool perspective)
    {
        ExtendedEvaluationTermsEncoding.Fill(state, perspective, features);
        return Forward(features);
    }
    public float Forward(float[] input)
    {
        if (input.Length != Terms) throw new ArgumentException("The v10 model requires 107 features.");
        float orientation = input[MultiverseEvaluation.TempoIndex] > 0 ? -1 : 1;
        var pw = W("termFeatureProj.weight"); var pb = W("termFeatureProj.bias"); var embedding = W("termEmbedding");
        for (int t=0;t<Terms;t++) {
            float x=input[t]*orientation, n=MathF.CopySign(MathF.Log(1+MathF.Abs(x)),x);
            for(int j=0;j<Width;j++) tokens[t*Width+j]=n*pw[j]+pb[j]+embedding[t*Width+j];
        }
        for(int layer=0;layer<2;layer++) {
            string p=$"encoder.layers.{layer}.";
            Dense(tokens, Width, qkv, Width*3, W(p+"self_attn.in_proj_weight"),W(p+"self_attn.in_proj_bias"));
            for(int head=0;head<4;head++) for(int t=0;t<Terms;t++) {
                int query=t*192+head*16;float max=float.NegativeInfinity;
                for(int s=0;s<Terms;s++) {float score=Dot(qkv,query,qkv,s*192+64+head*16,16)*.25f;scores[s]=score;max=Math.Max(max,score);}
                float sum=0;for(int s=0;s<Terms;s++)sum+=scores[s]=MathF.Exp(scores[s]-max);
                for(int j=0;j<16;j++){float v=0;for(int s=0;s<Terms;s++)v+=scores[s]/sum*qkv[s*192+128+head*16+j];attention[t*64+head*16+j]=v;}
            }
            Dense(attention,64,projected,64,W(p+"self_attn.out_proj.weight"),W(p+"self_attn.out_proj.bias"));
            NormAdd(tokens,projected,normalized,W(p+"norm1.weight"),W(p+"norm1.bias"));
            Dense(normalized,64,hidden,128,W(p+"linear1.weight"),W(p+"linear1.bias"));
            for(int i=0;i<hidden.Length;i++)hidden[i]=Math.Max(0,hidden[i]);
            Dense(hidden,128,projected,64,W(p+"linear2.weight"),W(p+"linear2.bias"));
            NormAdd(normalized,projected,tokens,W(p+"norm2.weight"),W(p+"norm2.bias"));
        }
        var ow=W("termOutput.weight");float result=W("termOutput.bias")[0];
        for(int j=0;j<64;j++){float mean=0;for(int t=0;t<Terms;t++)mean+=tokens[t*64+j];result+=mean/Terms*ow[j];}
        for(int t=0;t<Terms;t++)result+=input[t]*orientation*ow[64+t];
        return MathF.Tanh(Math.Clamp(result*orientation,-20000f/8700f,20000f/8700f));
    }
    private static float Dot(float[] a,int ai,float[] b,int bi,int length)
    {
        int i=0;var sum=Vector<float>.Zero;
        for(;i<=length-Vector<float>.Count;i+=Vector<float>.Count)sum+=new Vector<float>(a,ai+i)*new Vector<float>(b,bi+i);
        float result=Vector.Sum(sum);for(;i<length;i++)result+=a[ai+i]*b[bi+i];return result;
    }
    private static void Dense(float[] x,int input,float[] output,int width,float[] w,float[] b)
    {
        for(int t=0;t<Terms;t++)for(int j=0;j<width;j++) output[t*width+j]=Dot(x,t*input,w,j*input,input)+b[j];
    }
    private static void NormAdd(float[] a,float[] b,float[] output,float[] weight,float[] bias)
    {
        for(int t=0;t<Terms;t++) {
            int o=t*64;float mean=0;for(int j=0;j<64;j++)mean+=a[o+j]+b[o+j];mean/=64;
            float variance=0;for(int j=0;j<64;j++){float v=a[o+j]+b[o+j]-mean;variance+=v*v;}
            float inverse=1/MathF.Sqrt(variance/64+1e-5f);
            for(int j=0;j<64;j++)output[o+j]=(a[o+j]+b[o+j]-mean)*inverse*weight[j]+bias[j];
        }
    }
}
