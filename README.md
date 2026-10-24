# SkynetZero Web

[Play SkynetZero](https://crazypenguin0111.github.io/skynet-zero-web/) · [Model provenance](engine/Model/provenance.json) · [Licenses](THIRD_PARTY_NOTICES.md)

Play and analyze 5D chess in a timeline workspace inspired by **5D Chess With Multiverse Time Travel**. The interface, rules, search, model inference and game notation are written in C#. GitHub Pages serves static files; the engine runs on the player's computer as .NET WebAssembly in a dedicated worker. No inference server, account, API key or local engine installation is required.

- Play White, Black or a random side, with a 1–100 strength slider.
- Move across boards and through time, undo pending moves with **Z/Backspace**, then submit a complete multiverse turn with **F/Enter**. Hold **WASD/arrow keys** or drag the background for smooth motion in any direction. **Q/E** or scroll zooms at the pointer; Shift + scroll pans. **Tab** cycles playable boards, **U** flips perspective, **T/Y** toggle history/parallel views, **I** toggles travel animation, and **R** opens a restart prompt. Right-click/Escape deselects. Shortcuts pause in dialogs and text fields.
- Play on a full-window checkered canvas with cream/tan boards, broad purple timeline arrows and a wide side-colored present band passing behind the present boards. Moves, engine settings and analysis use a collapsible panel. Follow persistent white/black piece-travel arrows and labeled branch connectors. Branch origins are reconstructed from played or imported moves; timelines supplied only as an initial custom setup have no recorded parent.
- Click the red **!** at the corner of the board produced by the checking move to advance the present boards by one half-turn and reveal red attack arrows. This is a preview; Escape or Return to position restores the actual game.
- Import a 5DPGN file or paste a game, including explicit custom starting boards.
- Export completed games and unfinished turns as 5DPGN.
- Review played or imported games with a clickable evaluation graph, vertical advantage bar, move badges, per-turn centipawn loss, White/Black ACPL and proven mate-in-N scores. Play another move after rewinding to create an analysis variation. The line selector preserves the original and every variation for the current session; export saves the selected line as 5DPGN.
- Stop a search without losing the game. The last game is saved in local browser storage.

## C# engine and model

`engine/Vendor` contains a snapshot of the actual SkynetZero C# rules, strict royal legality, complete-turn alpha-beta search and evaluation-feature code. This is compiled for the browser; there is no JavaScript chess engine. `App.razor`, `Multiverse.razor` and their C# code implement the interface. The small JavaScript files only provide worker transport, storage, downloads, clipboard and scrolling.

The bundled model is **v10, epoch 1,040**, from run `20261020-030313-588` after 52,000 training games. It was the most recent statistically promoted trained model found on October 23, 2026; the latest run's `best.json` still pointed to epoch zero. Its promotion passed the paired test with 11 favorable, 2 unfavorable and 19 tied unique pairs. This selection does not establish superiority over every checkpoint from every run.

TorchSharp's native runtime cannot run in a browser. `PortableModel.cs` therefore evaluates the original 29 float32 tensors in C#, with the same transformer operations and 107 input features. Weights are neither quantized nor retrained. The checkpoint's learned evaluation tables are bundled too. Parity tests compare 26 outputs against the original TorchSharp model, including both perspectives of five actual positions; maximum observed absolute error was **6.706e-8**. The production feature vectors also match exactly.

Browser adaptations are recorded in [source-manifest.json](engine/source-manifest.json). Search is single-threaded and bounded, optional strategy/tactics book ordering and online sacrifice-table learning are disabled, evaluation tables are held at the bundled snapshot with native root-travel scoping, training-only tensor constructors are omitted, and the adapter preserves 5DPGN promotion restrictions. Original source hashes and bundled file hashes identify the snapshot, including local source changes.

| Slider level | Maximum depth (complete turns) | Node budget | Time budget |
|---|---:|---:|---:|
| 1 | 1 | 96 | 1 second |
| 34 (default) | 2 | 512 | 3 seconds |
| 67 | 6 | 8,192 | 15 seconds |
| 100 | 8 | 65,536 | 60 seconds |

Every intermediate level has its own smoothly increasing node and time budgets. Play and analysis each have a slider. Engine games record the highest played strength in a `SkynetStrength` PGN tag; analysis cannot use a weaker budget. Older engine exports without this tag use maximum analysis. The achieved search depth is shown after each engine move. These are search budgets, not Elo ratings or strict wall-clock guarantees. A complex complete-turn generation or legality check can exceed the time budget. **Stop** terminates the worker and restores the game. If no search depth completes, the UI labels the score as a static evaluation and any playable fallback explicitly.

## Opening repertoire

Engine play now uses the supplied SkynetZero opening books before searching. The frozen repertoire contains **368 source lines**, compiled into **791 choices at 661 exact historical positions**. It includes the opening PDF repertoire, the original-Standard f7 defenses, and the Turn-Zero London Ng5 lines. The compiler replayed and validated all **9,949 plies** with the bundled C# rules. Source documents and seed-file hashes are retained in `engine/Resources/openings.json`.

`OpeningRepertoire.cs` follows native `KnowledgeBooks.TryChoose`: full historical-state keys, variant separation, source weights with duplicate prefixes counted once, side-specific recommendations and strict royal-safety/turn-completion checks. Documented alternatives are sampled by their source weights. Pending turns, checks, branched games and positions outside the repertoire fall back to the normal search. No unapproved learned discoveries are included. This book is separate from the frozen model weights.

A book move is identified as **Opening book** in the engine panel and has no invented centipawn or mate score. Played and imported repertoire moves—including **1. Nf3**—receive the **Book** badge. Position/game analysis still searches opening positions.

To rebuild from the original checkout's supplied books, run:

```sh
dotnet run --project tests/EngineTests.csproj -c Release -- --export-book /path/to/SkynetZero/resources/books engine/Resources/openings.json
```

Review changes and deliberately update the bundle checksum afterward. Normal builds use the committed index and do not need the original checkout.

## Analysis and notation

Positive scores favor White. Engine value units are normalized using the v10 reference active-pawn coefficient: **484.2 engine units = 100 centipawns**. Scores are shown in pawn units (`+1.25`) and move losses in centipawns (`125 cp`). Loss compares the engine's best and played **complete multiverse turns from the same root and completed search depth**, floored at zero. A searched best turn has zero loss even if the next position's independent search changes its evaluation. Comparing a different played turn may require a second search with the selected budget; incomplete comparisons are unscored. Values are clipped to ±1,000 cp for loss calculation. ACPL is the arithmetic mean of scored turns for that side.

Badges include **Book, Best, Brilliant, Excellent, Good, Inaccuracy, Mistake and Blunder**. Best matches the searched turn or a tied alternative. Brilliant additionally requires a spatial piece sacrifice verified by native static exchange, completed depth 3 on both positions, and a preserved evaluation; temporal and multi-board sacrifices are not guessed. Numerical thresholds are 50 cp for Inaccuracy, 100 cp for Mistake and 300 cp for Blunder. These are SkynetZero classifications, not Chess.com's proprietary algorithm.

The graph shades White's advantage white above zero and Black's advantage black below zero. The whole-game analysis button disappears on completion and returns when the game or analysis strength changes.

History view hides the opposite color's boards. Parallel view also rotates the time/timeline layout to place timelines side by side. These are 2D views; the Steam game's 3D camera is not reproduced. The present band spans the padded scroll space at every zoom, and played travel arrows terminate on the original historical destination square. Separate connectors show the new branch.

Only the native search's proven mate distance produces `M1`, `M2`, etc.; large neural scores and incomplete searches do not. Negative mate scores favor Black. Scores with no completed search depth are unscored and cannot receive a numerical mistake/blunder label. Mate transitions receive labels such as “Missed mate” or “Allows mate” and are excluded from numerical ACPL. Terminal checkmate is shown as “Checkmate”. This offers Lichess-style review, but is not Lichess's calibration, accuracy percentage or move classification algorithm. Shallow search can produce noisy loss estimates; use a higher analysis strength for closer review.

The importer accepts the [5DPGN mainline notation](https://github.com/adri326/5dchess-notation), including SAN and explicit coordinates, time travel, castling, en passant, fairy-piece promotion, comments, `w`/`b` turn numbers and even `-0`/`+0` timelines. Built-in starting boards are `standard`, `standard-t0` and `standard-princess`; other named setups need explicit custom 5DFEN. Custom boards support sizes through 8×8 and at most 64 initial timelines. Import is limited to 1 MB and 1,000 complete turns.

Export writes explicit initial 5DFEN boards and mainline moves. Comments, original formatting and sideline variations are not preserved. The `SkynetPartial` extension identifies an unsubmitted final turn; other tools may treat it as a completed turn. Analysis displays submitted turn boundaries; returning to Play restores any pending moves. Export before starting another game if you want to keep multiple games. Analysis results are recomputed and are not embedded in the exported PGN.

## Build and run

Requires the **.NET 10 SDK**. Node.js 24 is used only for integration tests; Python 3 is used for the integrity check and optional local static server. No npm packages, CUDA, TorchSharp, original checkout, or WebAssembly workload are required for the normal build.

```sh
python3 tools/verify-bundle.py
dotnet run --project tests/EngineTests.csproj -c Release
dotnet publish engine/SkynetZero.Web.csproj -c Release -o artifacts/publish
node tools/wasm-smoke.mjs
node tools/worker-smoke.mjs
node tools/browser-controls.mjs
python3 -m http.server 4183 --directory artifacts/publish/wwwroot
```

Open `http://localhost:4183`. Serve the published directory over HTTP; opening `index.html` as a file will not load WebAssembly. Use a modern browser with WebAssembly SIMD and module workers. The first visit downloads the .NET runtime and model/evaluation data; subsequent visits can use the browser cache. Game calculation and analysis stay local after assets load. The app has no analytics or game-upload endpoint.

`tests/Program.cs` covers model/feature parity, strict moves, pending undo, time travel, multi-board turns, import/export, promotion restrictions, mate proof and loss orientation. `tests/UiChecks.cs` renders real Razor components. The Node integration tests execute the **published browser WebAssembly**, including opening-book play, the actual worker bridge and cancellation. A controlled-clock DOM fixture checks continuous diagonal panning, display-rate independence, keyboard release/blur/dialog guards and pointer-centered wheel zoom. They do not substitute for an interactive visual browser check.

## GitHub Pages

This is an independent repository. In **Settings → Pages → Build and deployment**, select **GitHub Actions**. The [workflow](.github/workflows/pages.yml) tests and publishes the static site on pushes to `main`. Pull requests run the same build and tests without deploying. The relative base path supports the `/skynet-zero-web/` project URL; no backend service or Pages-specific response headers are needed.

## Refresh the snapshot

The app is deliberately pinned to the bundled checkpoint. It does not silently fetch a different model.

1. Select a checkpoint using SkynetZero's promotion evidence, including its matching `evaluation-tables.bin`.
2. Run `python3 tools/sync-engine.py /path/to/SkynetZero/SkynetZero`. Review the adapter edits when upstream signatures change.
3. Build the source's `tests/AlphaBeta` project, then run `tools/ExportModel` with `-p:SkynetSource=/path/to/SkynetZero/SkynetZero`. Its three arguments are `move.model`, this repo's `engine/Model` directory and the checkpoint's `evaluation-tables.bin`. This optional export step requires .NET 9, TorchSharp and its matching native LibTorch libraries. The normal website build does not.
4. Copy the matching learned table to `engine/Resources/evaluation-tables.bin`, update `engine/Model/provenance.json` with selection evidence and hashes, and copy it to `engine/wwwroot/model-info.json`.
5. Update `engine/bundle-sha256.json` only after reviewing the intended source/model changes. Run every check above before committing and deploying.

## Attribution

The combined application is distributed under [GPL-3.0](LICENSE), with original component notices retained. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for SkynetZero, FirePlank engine components and Colin M. L. Burnett's chess pieces. The timeline interface is independently implemented. No Steam game artwork is bundled, and this project is not affiliated with Thunkspace.
