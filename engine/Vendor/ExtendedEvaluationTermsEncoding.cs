// Additional source evaluation terms; GPL-3.0-only. See THIRD_PARTY_NOTICES.md.
namespace SkynetZero;

internal static class ExtendedEvaluationTermsEncoding
{
    internal const int Size = 107;
    internal const string Schema = "evaluation-terms107-transformer-v10";
    internal const string SourceCommit = "3697bf323a1a501428917ad218f7358d681f2904";
    // Upstream's remaining eight slots are fade parameters, not features. At
    // this revision fade1=1 and all other fades=0; chess terms run on one active
    // timeline only. The twelve actual additional inputs retain their identity.
    internal static readonly string[] Names = [
        "mat_a_pawn", "mat_a_brawn", "mat_a_knight", "mat_a_bishop", "mat_a_rook", "mat_a_queen", "mat_a_princess", "mat_a_unicorn",
        "mat_a_dragon", "mat_a_cking", "mat_i_pawn", "mat_i_brawn", "mat_i_knight", "mat_i_bishop", "mat_i_rook", "mat_i_queen",
        "mat_i_princess", "mat_i_unicorn", "mat_i_dragon", "mat_i_cking", "cent_knight", "cent_bishop", "cent_rook", "cent_queen",
        "cent_other", "pawn_adv", "pawn_near", "king_cent", "tl_slots", "tl_slots_heavy", "k_holes", "k_knights",
        "k_open_orth", "k_open_diag", "k_shield", "k_zone_att", "mob_knight", "mob_bishop", "mob_rook", "mob_queen",
        "mob_king", "hanging", "royal_att", "tempo", "active_lines", "checks_5d", "sp_checks", "mandatory",
        "passed", "doubled", "isolated", "king_prox", "undeveloped", "king_prox_5d", "queen_prox", "travel_past",
        "travel_now", "travel_backed", "weak_sq", "open_orth5", "open_diag5", "open_multi5", "king_off", "king_off_new",
        "low_flight", "exiled", "inactive_royal_att", "travel_stm", "tl_slots_threat", "tl_slots_stm", "f7_weak", "f7_open",
        "f7_qreach", "f7_broute", "kn_checks", "sac_line", "sac_close", "sac_now", "sac_now_stm", "sac_open",
        "zone_hot", "weak_hot", "dmax5", "zone_max", "k_def", "k_flight", "tl_lost", "to_move",
        "zone_mand_stm", "zone_mand_opp", "def_hot", "def_board", "travel_hot", "kingless", "kl_swarm", "ch_mat",
        "ch_psqt", "ch_pawn", "ch_piece", "ch_threat", "ch_king", "ch_mob", "chm_pawn", "chm_knight",
        "chm_bishop", "chm_rook", "chm_queen",
    ];
    internal static readonly float[] Weights = [
        484.2f, 150.0f, 845.5f, 1553.5f, 944.9f, 2464.5f, 899.8f, 250.0f,
        150.0f, 195.3f, -83.8f, 74.9f, 219.3f, 630.7f, 316.4f, 1097.3f,
        450.0f, 125.1f, 74.9f, 150.0f, 97.5f, -50.9f, 52.0f, -6.4f,
        142.7f, -28.2f, 302.0f, -123.6f, 2332.7f, -98.2f, -224.7f, 2526.0f,
        -511.5f, 31.5f, 624.7f, 234.0f, 76.0f, 129.6f, 106.9f, 91.3f,
        93.1f, -15.6f, -2076.0f, -106.2f, 142.2f, 0.0f, 0.0f, 278.9f,
        64.0f, -2.4f, -29.8f, 972.7f, -381.6f, 84.5f, -341.1f, 54.9f,
        532.9f, -567.5f, 380.5f, 0.0f, -20.2f, -20.5f, -847.1f, -1536.2f,
        746.0f, -11.8f, -4.4f, 608.4f, 552.0f, -415.3f, 26.4f, 62.9f,
        248.4f, -11.6f, 296.2f, 600.0f, 100.0f, 0.0f, 700.0f, 150.0f,
        460.2f, -74.7f, -197.1f, -430.7f, -15.5f, 226.9f, -92.7f, 372.2f,
        -20.4f, 732.9f, -76.5f, -15.5f, -129.1f, -171.3f, -313.5f, 8.7f,
        16.6f, 0.0f, 0.0f, 12.0f, 47.4f, 15.2f, -740.5f, -1181.9f,
        -1554.7f, -1561.8f, -1312.2f,
    ];
    internal static void Fill(GameState state, bool perspective, float[] output, int offset = 0)
    {
        if (offset < 0 || output.Length - offset < Size) throw new ArgumentException("Expected 107 evaluation terms.");
        EvaluationTermsEncoding.Fill(state, perspective, output, offset);
        Span<int> extra = stackalloc int[12]; StandardChessEvaluation.Fill(state, extra);
        float sign = perspective ? 1 : -1;
        for (int i = 0; i < 12; i++) output[offset + 95 + i] = sign * extra[i] / EvaluationTermsEncoding.Scale;
    }
}
