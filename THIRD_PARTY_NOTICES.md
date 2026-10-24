# Third-party notices

The new web application and combined distribution are licensed under GPL version 3, as supplied in [LICENSE](LICENSE). Individual components retain their original licenses and notices.

## SkynetZero

The C# engine snapshot originates from [GHXX/SkynetZero](https://github.com/GHXX/SkynetZero), including the local working-tree changes identified by [source-manifest.json](engine/source-manifest.json). Original MIT terms are in [SKYNETZERO-LICENSE.txt](engine/SKYNETZERO-LICENSE.txt). The model tensors and learned evaluation table were exported from the user's SkynetZero training checkpoint identified in [provenance.json](engine/Model/provenance.json).

Browser-specific changes are described in the manifest and implemented by `tools/sync-engine.py`. `PortableModel.cs`, the session/notation adapter and the Blazor interface are web application additions. The original TorchSharp binary is not distributed.

## FirePlank 5D chess engine

Portions of the search, evaluation and sacrifice tables are adapted from [FirePlank/5d-chess-engine](https://github.com/FirePlank/5d-chess-engine), GPL-3.0-only. Individual source headers retain revision information. These include `CompleteTurnAlphaBetaSearch.cs`, `CompleteTurnSearchGenerator.cs`, `MultiverseEvaluation.cs`, `StandardChessEvaluation.cs`, `ExtendedEvaluationTermsEncoding.cs`, `SearchHeuristics.cs`, `LocalCaptureSearch.cs`, `SacrificeTable.cs` and `SacrificePreparation.cs` in `engine/Vendor`.

`engine/Resources/fsac_table.bin` is the upstream sacrifice table. `evaluation-tables.bin` is the trained table used with the selected checkpoint. The [original upstream notices](engine/UPSTREAM-NOTICES.md) are retained for context; paths mentioned there refer to the original checkout. In this repository the GPL text is at `LICENSE`, vendored source is at `engine/Vendor`, and resources are at `engine/Resources`.

## Opening repertoire

The supplied SkynetZero book data is compiled into `engine/Resources/openings.json`. This contains factual move sequences indexed as historical positions/actions, source weights and identifying names. It does not include the source documents, illustrations, guide prose or study comments. Sources are **Theory Gang**'s *5D chess opening book v3.0*, **Lawa**'s [f7 sacrifice guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2218187793), and **obama_CAT**'s [London Ng5 study](https://lichess.org/study/lcivLqVK/HNFhAtGQ), as supplied with the original SkynetZero project. Original document hashes and the exact seed-file hashes are recorded in the data. The C# lookup is adapted from SkynetZero's `KnowledgeBooks.TryChoose` under its MIT license.

## Chess piece artwork

The twelve SVG files in `engine/wwwroot/pieces` are the **Cburnett** set by **Colin M. L. Burnett**, licensed under **GPL-2.0-or-later** and used here under GPL version 3. Retrieved from [lichess-org/lila/public/piece/cburnett](https://github.com/lichess-org/lila/tree/master/public/piece/cburnett); Lichess records this exception in its [COPYING.md](https://github.com/lichess-org/lila/blob/master/COPYING.md). The SVGs are unmodified. Fairy-piece letter badges are generated separately by the application.

## .NET and Blazor

The published application includes Microsoft .NET runtime and Blazor WebAssembly dependencies. These retain the [dotnet/runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT), and [ASP.NET Core license](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt). License and notice files from the 10.0.12 packages are bundled in `engine/wwwroot/licenses`. Package versions are recorded in the project file and build output.

## Design reference

The timeline arrangement, broad arrows, checkered canvas and cream controls are inspired by [5D Chess With Multiverse Time Travel](https://www.5dchesswithmultiversetimetravel.com/), by Thunkspace. The application uses its own HTML, CSS, layout and logo; no proprietary game graphics or screenshots are distributed. There is no affiliation or endorsement.
