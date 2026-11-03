# Search performance

Measured November 2, 2026 on an Intel Core i9-14900KF running Linux, with the existing training process left running. Original v11 weights and evaluation features are unchanged. Depth counts complete multiverse turns, including every required board move. It is the search's completed selective depth, not an exhaustive full-width minimax claim.

The matched harness uses the same PGNs, depth-10 ceiling, 2,000,000-node allowance, 4,096-entry tables and 16-second deadline in native .NET and the published WebAssembly runtime. Opening-book shortcuts are bypassed. Each result is replayed through royal-safety checks and the input position must remain unchanged.

| Position | Browser before: depth / nodes | Browser after: depth / nodes | Node throughput change |
|---|---:|---:|---:|
| Turn Zero start | 5 / 11,165 | 7 / 49,775 | 4.46× |
| After Nf3, Nf6, d4, d5 | 5 / 8,621 | 7 / 39,577 | 4.59× |
| After e4, e5, Nb1 to past b3 | 4 / 4,173 | 4 / 11,309 | 2.71× |

All six browser measurements spent approximately 16 seconds. More nodes do not guarantee the next completed depth, especially with multiple timelines. These are individual samples, not a hardware-independent promise.

The native harness completed depth 10 from Turn Zero in **10.38 seconds**, down from **13.35 seconds**. It searched the identical 162,826 nodes, returned the same score and move, and reduced allocated bytes from 8.22 GB to 5.45 GB (34%). These are cumulative allocations, not peak memory. The four-ply fixture completed depth 10 in 13.98 seconds; the branched fixture reached depth 5 in 16 seconds.

The actual cold native server worker was measured separately, including PGN import and process startup, with the production strength-100 depth ceiling of 64. At the 16-second search deadline it reached **depth 10 / 9 / 5** on these three positions. End-to-end process time was approximately **16.3 seconds**. The server process pool adds a hard limit of budget plus four seconds, including parsing and cleanup. Depth 10 is not guaranteed for arbitrary 5D positions or slower VDS hardware.

Raw results and model checksum: [results.json](results.json). Fixtures: [search-positions.json](../search-positions.json).

Changes responsible for the improvement:

- Release WebAssembly is compiled ahead of time, with trimming and SIMD. Development builds can still use the interpreter.
- Move-ordering snapshots share immutable pages instead of copying a 256 KB array for every generator. Updates and decay preserve the original scores exactly.
- Single-action safety proofs from an immutable inventory are reused. Combined turns are still checked after each move; empty submissions are still checked.
- Repeated same-root analysis keeps its transposition table, history and root-travel evaluation context. Incompatible positions, variants and strength changes invalidate it.
- Maximum strength permits 2,000,000 nodes in 16 seconds. The previous 65,536-node cap could prevent depth 10 even on native hardware. Played-turn comparison shares the remaining time rather than spending another full budget.
- Signed-in VDS users can use native search in bounded child processes; guests and a busy server fall back to the browser. The native worker uses workstation GC and permits a background GC/JIT thread. It does not take the multiplayer write lock.

Reproduce the matched benchmark:

```sh
dotnet run --project tools/SearchBenchmark -c Release
dotnet workload install wasm-tools
dotnet publish engine/SkynetZero.Web.csproj -c Release -p:EngineBenchmark=true -o artifacts/benchmark
node tools/search-benchmark.mjs artifacts/benchmark/wwwroot
```

For a matched interpreter build, add `-p:RunAOTCompilation=false` and publish to a separate directory. Benchmark exports are included only with `EngineBenchmark=true`; normal releases expose no diagnostic search API. Custom fixture files can override `depth`, `nodes`, `milliseconds` and `tableEntries` and can be passed to both harnesses. Fixed-depth/node comparisons are useful for checking algorithmic parity; time-bounded searches can choose different moves after completing different depths.

Validation includes model parity, independently replayed legal opening inventories for all six variants, mate proofs, immutable history snapshots, cache isolation, native authentication/input bounds/concurrency/cancellation, actual Wasm worker cancellation and server-to-browser synchronization, and two-browser community tests with native analysis.
