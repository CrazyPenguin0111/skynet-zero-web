#!/usr/bin/env python3
"""Snapshot the actual SkynetZero rules/search; omit only training tensor adapters."""
from pathlib import Path
import sys,hashlib,json,re,shutil
src=Path(sys.argv[1]).resolve(); dest=Path(__file__).resolve().parents[1]; vendor=dest/'engine/Vendor';vendor.mkdir(parents=True,exist_ok=True)
names='AlphaBetaSearch CompleteTurnAlphaBetaSearch CompleteTurnSearchGenerator HypercuboidSearch HypercuboidGameAdapter CheckAndMate RoyalLegality LegalTurnContinuation GameLegalActions CandidateSimulation CompleteTurnStateHash SemanticStateHash SearchCycleHistory DrawRules MultiverseEvaluation StandardChessEvaluation StandardChessTables EvaluationTermsEncoding ExtendedEvaluationTermsEncoding SacrificeTable SearchHeuristics LocalCaptureSearch SacrificePreparation PositionHeuristics StrategicHeuristics StaticExchange KingSafety SelfPlayProfile FastTurnWitness FiveDProtocol'.split()
files=[src/(n+'.cs') for n in names]+list((src/'5DChess/5DReWrite').glob('*.cs'))+list((src/'5DChess/FileIO').glob('*.cs'))
manifest={}
for p in files:
 text=p.read_text(encoding='utf-8-sig'); manifest[str(p.relative_to(src))]=hashlib.sha256(p.read_bytes()).hexdigest()
 if p.name=='AlphaBetaSearch.cs':
  text=text[:text.index('internal sealed class AlphaBetaSearch')]+ 'internal static class AlphaBetaSearch { internal const int Mate=1_000_000, EvaluationScale=100_000; }\n'
 if p.name=='CompleteTurnAlphaBetaSearch.cs': text=text.replace('KnowledgeBooks?', 'object?').replace('        var scorer = books?.Scorer(state);\n','').replace('            if (scorer != null) score += 1000 * scorer.Score(next, move, check);\n','')
 if p.name=='HypercuboidGameAdapter.cs':
  text=text.replace('HcBudget? budget = null)\n    {\n        foreach (Timeline line in state.Multiverse)', 'HcBudget? budget = null, List<Move>? threats = null)\n    {\n        foreach (Timeline line in state.Multiverse)')
  text=text.replace('                                return new(new(x, y, line.TEnd, line.Layer, !defender), new(dx, dy, time, layer, !defender));', '                            {\n                                var threat = new Move(new(x, y, line.TEnd, line.Layer, !defender), new(dx, dy, time, layer, !defender));\n                                if (threats == null) return threat;\n                                // The browser preview collects every check using the same\n                                // attack scan; search retains its original early exit.\n                                threats.Add(threat);\n                            }')
  text=text.replace('        return null;\n    }\n\n    private static int AttackSquare', '        return threats?.FirstOrDefault();\n    }\n\n    private static int AttackSquare')
 if p.name=='GameLegalActions.cs':
  text=text.replace('                        if (state.QueenPromotionOnly &&','                        if (!state.PromotionAllowed((promotedPiece-1)%Board.NUMTYPES+1)) continue;\n                        if (state.QueenPromotionOnly &&')
 if p.name=='CompleteTurnStateHash.cs':
  text=text.replace('        if (state.QueenPromotionOnly)', '        if (state.PromotionMask != 0) { writer.Add(0x50524F4D); writer.Add(state.PromotionMask); }\n        if (state.QueenPromotionOnly)')
 if p.name=='GameState.cs':
  text=text.replace('        internal bool QueenPromotionOnly;', '        internal bool QueenPromotionOnly;\n        // Browser adapter: preserve PGN variant promotion restrictions. Zero retains native defaults.\n        internal int PromotionMask;\n        internal bool PromotionAllowed(int type) => PromotionMask == 0 || (PromotionMask & (1 << type)) != 0;')
  text=text.replace('            clone.QueenPromotionOnly = this.QueenPromotionOnly;', '            clone.QueenPromotionOnly = this.QueenPromotionOnly;\n            clone.PromotionMask = this.PromotionMask;')
  text=text.replace('                if (QueenPromotionOnly &&', '                if (!PromotionAllowed(promotedType)) return false;\n                if (QueenPromotionOnly &&')
  text=re.sub(r'^using (?:static )?TorchSharp[^\n]*\n','',text,flags=re.M)
  a=text.index('        public Tensor GameStateTensor('); b=text.index('\n        private void InitVals()',a)
  text=text[:a]+text[b:]
 (vendor/p.name).write_text(text)
# Keep production helper bodies verbatim, without trainer/runtime dependencies.
program=(src/'Program.cs').read_text(encoding='utf-8-sig')
def method(name):
 a=program.index('\tinternal static '+name); start=program.index('{',a); depth=1;i=start+1
 while depth:
  depth+=(program[i]=='{')-(program[i]=='}');i+=1
 return program[a:i]
helpers='namespace SkynetZero;\ninternal static class Program {\ninternal const float MaximumFinitePieceValue=14.5f;\nprivate const double ActiveTimelineCreationPenalty=.1,InactiveTimelineCreationPenalty=.2;\n'+method('int GetCapturedPiece')+'\n'+method('double GetTimelineCreationPenalty(GameState game, GameState successor)')+'\n}\n'
ct=(src/'CompleteTurnCandidates.cs').read_text();a=ct.index('    internal static string ActionKey');b=ct.index(';',a)+1
helpers+='internal static class CompleteTurnCandidates {\n'+ct[a:b]+'\n}\ninternal static class EvaluationEncoding { internal const float ScoreScale=8700f; }\ninternal static class TurnSearch {internal static SelfPlayProfile? Profile; }\n'
books=(src/'KnowledgeBooks.cs').read_text(); helpers+=books[books.index('internal sealed record BookAction'):books.index('internal sealed record LearnedBookEntry')]
q=(src/'QueryStateProvenance.cs').read_text(); helpers+='internal static class QueryStateProvenance {\n'+q[q.index('    internal static string Semantic('):q.index('    internal static string LegalActions(')]+q[q.index('    private static void AppendBoards('):q.index('    private static string HashBytes(')]+'}\n'
(vendor/'BrowserHelpers.cs').write_text('using System.Text;\nusing System.Security.Cryptography;\n'+helpers)
(dest/'engine/source-manifest.json').write_text(json.dumps({'source':'https://github.com/GHXX/SkynetZero','snapshot':'2026-10-23 working tree','files':manifest,'adaptations':['GameState: remove three TorchSharp-only tensor constructors. Preserve PGN promotion restrictions through clone, validation, enumeration and state hashing; zero mask retains native promotion defaults.','BrowserHelpers: extracted capture, timeline penalty, action key, BookAction and semantic serialization helpers; no training entrypoint.','AlphaBetaSearch: retain settings/result contracts and constants; browser uses CompleteTurnAlphaBetaSearch.','CompleteTurnAlphaBetaSearch: omit optional book ordering (the browser supplies no book), retain complete-turn search algorithm and proof semantics.','HypercuboidGameAdapter: optional threat collector exposes every native check for a read-only one-half-turn preview; search still exits on the first threat.']},indent=2)+'\n')
(dest/'engine/Resources').mkdir(exist_ok=True)
shutil.copyfile(src/'resources/evaluation/fsac_table.bin',dest/'engine/Resources/fsac_table.bin')
shutil.copyfile(src/'resources/evaluation/LICENSE-GPL-3.0.txt',dest/'LICENSE')
shutil.copyfile(src.parent/'LICENSE.txt',dest/'engine/SKYNETZERO-LICENSE.txt')
shutil.copyfile(src/'THIRD_PARTY_NOTICES.md',dest/'engine/UPSTREAM-NOTICES.md')
print('Copied',len(files),'engine files')
