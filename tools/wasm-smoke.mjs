// Executes the published browser WebAssembly runtime directly, without a server engine.
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const root=path.resolve(process.argv[2]||'artifacts/publish/wwwroot');
const {dotnet}=await import(pathToFileURL(path.join(root,'_framework/dotnet.js')));
const runtime=await dotnet.withDiagnosticTracing(false).create();
const exports=await runtime.getAssemblyExports('SkynetZero.Web.dll');
const send=request=>{
 const reply=JSON.parse(exports.SkynetZero.Web.EngineBridge.Dispatch(JSON.stringify(request)));
 if(reply.error)throw new Error(reply.error);return reply;
};
const initial=send({command:'new'}).position;
if(initial.legalMoves.length!==20)throw new Error('Wrong initial legal moves');
send({command:'move',move:'(0T1)e2e4'});
send({command:'submit'});
const started=performance.now();
const played=send({command:'engine',strength:1});
if(played.position.turnCount!==2)throw new Error('Engine failed to submit a complete turn');
console.log(JSON.stringify({runtime:'browser-wasm',initialMoves:20,engineTurn:played.position.turns.at(-1).notation,elapsedMs:Math.round(performance.now()-started),depth:played.evaluation.depth,nodes:played.evaluation.nodes}));
const imported=send({command:'import',pgn:played.position.pgn});
if(imported.position.turnCount!==2)throw new Error('WASM PGN round trip failed');
send({command:'import',pgn:'[Board "custom"]\n[Size "8x8"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]'});
const mate=send({command:'analyze',index:0,strength:34}).evaluation;
if(mate.mate!==1||mate.winner!==true)throw new Error('WASM mate proof failed: '+JSON.stringify(mate));
console.log('PASS published WebAssembly legal play, engine response, PGN import/export and M1');
process.exit(0);
