# Source attributions

Portions of the evaluation, sacrifice tables, move ordering, and complete-turn
search are adapted from [FirePlank/5d-chess-engine](https://github.com/FirePlank/5d-chess-engine)
by FirePlank and contributors, licensed under GPL-3.0-only. The source revisions
are recorded in the individual file headers and [engine audit](ENGINE_SOURCE_PARITY.md).
The bundled license is [GPL version 3](resources/evaluation/LICENSE-GPL-3.0.txt).
These adapted portions and the bundled sacrifice table retain that license;
the repository's original MIT license does not relicense upstream material.

Principal adapted files include `MultiverseEvaluation.cs`,
`StandardChessEvaluation.cs`, `ExtendedEvaluationTermsEncoding.cs`,
`SearchHeuristics.cs`, `LocalCaptureSearch.cs`, `SacrificeTable.cs`,
`SacrificePreparation.cs`, `CompleteTurnAlphaBetaSearch.cs`, and
`CompleteTurnSearchGenerator.cs`. Native coordinates, data structures, strict
legality integration, managed-memory handling and the trainable transformer are
SkynetZero adaptations; these are not unmodified upstream engine binaries.

The independent Rust test generators under `tests/AlphaBeta/reference` import the
upstream crate to generate expected results; their pinned revisions are documented
in that directory. `resources/evaluation/fsac_table.bin` is an unmodified upstream
asset with its checksum recorded in the adjacent README.
