// Exercise the shipped worker and browser bridge with Node's real worker threads.
// This checks transport/cancellation; it is not a browser UI test.
import assert from 'node:assert/strict';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {Worker as NodeWorker, isMainThread, parentPort, workerData} from 'node:worker_threads';

if (!isMainThread) {
  globalThis.self = globalThis;
  self.postMessage = message => parentPort.postMessage(message);
  const ready = import(workerData.url);
  parentPort.on('message', async data => {
    await ready;
    await self.onmessage({data});
  });
} else {
  const root = path.resolve(process.argv[2] || 'artifacts/publish/wwwroot');
  globalThis.window = {};
  globalThis.document = {
    baseURI: pathToFileURL(root + path.sep).href,
    querySelector(){return null;}
  };
  globalThis.Worker = class {
    constructor(url) {
      this.thread = new NodeWorker(new URL(import.meta.url), {workerData: {url: url.href}});
      this.thread.on('message', data => this.onmessage?.({data}));
      this.thread.on('error', error => this.onerror?.({message: error.message}));
    }
    postMessage(data) { this.thread.postMessage(data); }
    terminate() { this.thread.terminate(); }
  };
  await import(pathToFileURL(path.join(root, 'browser.js')));
  const bridge = window.skynet;
  const send = async request => {
    const reply = JSON.parse(await bridge.request(JSON.stringify(request)));
    assert.equal(reply.error, null, reply.error);
    return reply;
  };
  try {
    const game = await send({command: 'new'});
    assert.equal(game.position.legalMoves.length, 20);
    await send({command: 'move', move: '(0T1)e2e4'});
    const pending = (await send({command: 'state'})).position;
    const rejected = JSON.parse(await bridge.request('{"command":"unknown"}'));
    assert.match(rejected.error, /Unknown engine command/);
    const queued = bridge.request('{"command":"analyze","index":0,"strength":100}');
    const cancellation = assert.rejects(queued, /Search cancelled/);
    bridge.cancel();
    await cancellation;
    const restored = (await send({command: 'import', pgn: pending.pgn})).position;
    assert.deepEqual(restored.boards, pending.boards);
    assert.equal(restored.pendingCount, 1);
    assert.equal(restored.canSubmit, true);
    await send({command: 'submit'});
    const played = await send({command: 'engine', strength: 1});
    assert.equal(played.position.turnCount, 2);
    console.log('PASS shipped worker transport, engine errors, cancellation, restart and pending-game restoration');
    await send({command:'import',pgn:pending.pgn});await send({command:'submit'});
    const beforeServer=(await send({command:'state'})).position;
    window.community={search:async()=>JSON.stringify({...played,evaluation:{...played.evaluation,searchLocation:'server'}})};
    const remote=await send({command:'engine',pgn:beforeServer.pgn,strength:1});
    assert.equal(remote.evaluation.searchLocation,'server');
    assert.deepEqual((await send({command:'state'})).position.boards,played.position.boards);
    window.community.search=async()=>null;
    const local=await send({command:'analyze',pgn:played.position.pgn,index:2,strength:1});
    assert.equal(local.evaluation.searchLocation,null);
    let aborted=false;
    window.community.search=(_,signal)=>new Promise((resolve,reject)=>signal.addEventListener('abort',()=>{
      aborted=true;reject(new DOMException('Search aborted','AbortError'));
    }));
    const native=bridge.request(JSON.stringify({command:'analyze',pgn:played.position.pgn,index:2,strength:100}));
    const canceled=assert.rejects(native,/aborted|cancelled/);bridge.cancel();await canceled;
    assert(aborted,'Stop must abort the native request');
    let finish;
    window.community.search=()=>new Promise(resolve=>{finish=resolve;});
    const stale=bridge.request(JSON.stringify({command:'engine',pgn:beforeServer.pgn,strength:1}));
    const ignored=assert.rejects(stale,/Search cancelled/);bridge.cancel();finish(JSON.stringify(played));await ignored;
    window.community=null;
    assert.equal((await send({command:'state'})).position.turnCount,0,'A late native response must not mutate the restored worker');
    console.log('PASS native result synchronization, browser fallback, native cancellation and stale-response isolation');
  } finally {
    bridge.cancel();
  }
}
