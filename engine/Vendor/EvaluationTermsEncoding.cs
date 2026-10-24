namespace SkynetZero;

// The complete 95-term upstream evaluator is the sole model input. There are
// no board tensors, duplicated side vectors, precomputed totals or score bypasses.
internal static class EvaluationTermsEncoding
{
    internal const int Tokens = MultiverseEvaluation.FeatureCount, Features = 1, Size = Tokens;
    internal const float Scale = 32f, ScoreScale = 8700f;
    internal const string Schema = "evaluation-terms95-transformer-v9";
    internal static readonly string[] Names = (string[])MultiverseEvaluation.FeatureNames.Clone();

    internal static void Fill(GameState state, bool perspective, float[] output, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (offset < 0 || output.Length - offset < Size)
            throw new ArgumentException($"Evaluation-term input requires {Size} floats.", nameof(output));
        // Every channel can acquire a learned coefficient, including the two
        // check terms whose shipped linear coefficients happen to be zero.
        Span<int> terms = stackalloc int[Size];
        MultiverseEvaluation.FillFeatures(state, terms);
        float orientation = perspective ? 1 : -1;
        for (int i = 0; i < Size; i++) output[offset + i] = orientation * terms[i] / Scale;
    }

    internal static void Fill(GameState state, Move move, int originalMin, int originalMax,
        float[] output, int offset = 0) => Fill(state, move.Origin.Color, output, offset);
}
