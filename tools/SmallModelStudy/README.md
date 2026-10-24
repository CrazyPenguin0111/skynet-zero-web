# Smaller evaluator / depth-64 experiment

The compact C# evaluator searches substantially deeper on the same time budget,
but this first distilled candidate is **not ready to replace the bundled model**.
Its held-out mean absolute difference from the teacher is 174 cp, and deeper
search still chooses the reported turn-26 travel. No tested position reached
depth 64. The website retains the promoted transformer; maximum strength now
allows iterative deepening up to 64 within its existing 60-second / 65,536-node
limits and displays the depth actually completed.

Measured October 24, 2026 on an Intel Core i9-14900KF using native .NET 10,
single-threaded C# inference and the website's pinned C# search. These are CPU
experiments, not browser WebAssembly timings or an Elo/playing-strength test.
The host also performed browser checks, so absolute throughput is approximate.

## Candidate and training

- Teacher: bundled v10 epoch-1,040 transformer, 74,092 parameters, unchanged
  learned evaluation tables and 107 production features.
- Student: 107 → 64 → 32 → 1 ReLU MLP plus the teacher's frozen linear skip.
  9,025 trainable parameters; 9,133 including the frozen linear weights/bias.
  Fixed normalization statistics are stored separately.
- Canonical side-to-move orientation and signed-log feature encoding match the
  teacher. Only the nonlinear residual is distilled, using its unclipped logit.
  Both evaluators retain the same bounded output and centipawn conversion.
- Source: 126 saved self-play games from run `20261023-011705-981`, sampled
  throughout each game. All 126 parsed successfully. Exact duplicate feature
  vectors were removed across all splits.
- Splits are by whole game: 1,299 training, 428 validation, 446 test positions.
  Game 116,000 is excluded from training and validation. Source filenames,
  hashes and split assignments are in [sources.json](results/sources.json).
- Seed 20261024, Adam, batch 64, gradient-norm limit 5, 160 epochs. Checkpoint
  selection uses validation residual MSE only; epoch 18 was retained. Later
  training loss continued falling while validation did not improve.
- Test teacher disagreement: mean **173.7 cp**, median **112.4 cp**, 95th
  percentile **485.0 cp**. This measures imitation error, not centipawn loss
  against a proven best move. The teacher is not ground truth.

The candidate is [compact-mlp.json](results/compact-mlp.json), with
[training metrics](results/training.json). It is not loaded by the production app.

## Search results

Both models request depth 64 with the same 2,000,000-node ceiling, 65,536-entry
transposition table, one thread, at most 32 actions per complete turn, and one
quiescence turn. Opening-book selection is bypassed. Every returned turn is
replayed through native move legality and submission checks. The benchmark
budgets are larger than the website's node/table budgets to isolate evaluator
cost. Each five-second comparison was repeated in opposite model order.

| Position | Transformer completed depth, 5 s | Compact completed depth, 5 s |
|---|---:|---:|
| Standard turn-zero opening | 3, 3 | 8, 8 |
| Early time-travel branch | 3, 3 | 5, 5 |
| Hanging-queen capture | 6, 6 | 10, 10 |
| Mate in one | 4, 4 | 4, 4 |
| Game 116k, before White 14 | 3, 3 | 9, 9 |
| Game 116k, before White 16 | 3, 3 | 9, 9 |
| Game 116k, before White 24 | 4, 4 | 8, 7 |
| Game 116k, before White 26 | 3, 3 | 5, 5 |

Both models capture the hanging queen and prove the mate-in-one fixture.
The mate search ends on a proven win, not because it reaches the requested
depth. Raw moves, counters, stop reasons and timings are in
[search-5000ms.json](results/search-5000ms.json).

A longer follow-up (one run per model/position) produced:

| Position, 30 s | Transformer depth / nodes | Compact depth / nodes |
|---|---:|---:|
| Opening | 6 / 13,370 | 11 / 583,592 |
| Game 116k, before White 26 | 4 / 17,935 | 6 / 183,270 |

See [search-30000ms.json](results/search-30000ms.json). All four stopped on the
time budget. Both models still select `(1T25)d3(0T25)d2` before White 26, at both
time controls. This experiment therefore does **not** establish a fix for that
reported mistake. At White 24 both models instead choose the spatial bishop
move `(0T24)b5d7` in these runs; no stronger reference establishes its quality.

In the 30-second runs, transformer evaluation (including feature extraction)
uses 91–98% of search time; compact evaluation uses 5–18%. Most compact search
time is consequently outside neural inference. A faster evaluator helps, but
move generation, legality, cloning and search also need work. There is no
evidence here that substituting this MLP automatically improves playing strength.
Before promotion it needs more varied training targets and paired games,
especially tactical and branched positions.

This engine's depth counts one player's complete multiverse turn, potentially
several board actions. Stockfish's reported depth is an iterative-deepening
counter with pruning, reductions and extensions; it does not guarantee an
exhaustive search of every line to that depth. The numbers are not directly
comparable. See the [official Stockfish depth explanation](https://official-stockfish.github.io/docs/stockfish-wiki/Stockfish-FAQ.html#what-is-depth).

## Reproduce

From the repository root, with the .NET 10 SDK and the original saved games:

```sh
dotnet build tools/SmallModelStudy/SmallModelStudy.csproj -c Release
dotnet tools/SmallModelStudy/bin/Release/net10.0/SmallModelStudy.dll prepare artifacts/small-model /path/to/training-runs/20261023-011705-981/playedGames
dotnet tools/SmallModelStudy/bin/Release/net10.0/SmallModelStudy.dll train artifacts/small-model
dotnet tools/SmallModelStudy/bin/Release/net10.0/SmallModelStudy.dll benchmark artifacts/small-model /path/to/PGN_116000.txt 5000
dotnet tools/SmallModelStudy/bin/Release/net10.0/SmallModelStudy.dll benchmark artifacts/small-model /path/to/PGN_116000.txt 30000 opening,game116k-white26 1
```

To benchmark the supplied candidate without retraining, copy
`results/compact-mlp.json` into the chosen output directory first. The scripts
read the saved games and never update the running trainer, source checkpoints
or production model. Dataset files and fresh results under `artifacts/` are
ignored by Git. Results can vary with CPU, JIT warm-up and host load.
