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
  let keyHandler,dialogOpen=false;
  globalThis.document = {
    baseURI: pathToFileURL(root + path.sep).href,
    addEventListener(type,fn){if(type==='keydown')keyHandler=fn;},
    removeEventListener(type,fn){if(type==='keydown'&&keyHandler===fn)keyHandler=null;},
    querySelector(selector){return selector==='[role="dialog"]'&&dialogOpen?{}:null;}
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
  const shortcuts=[];
  bridge.bindKeyboard({invokeMethodAsync(...args){shortcuts.push(args);return Promise.resolve();}});
  const press=(key,options={})=>keyHandler({key,repeat:false,preventDefault(){},target:{closest:()=>null},...options});
  press('f');press('W');press('f',{ctrlKey:true});press('a',{isComposing:true});
  press('f',{target:{closest:()=>({})}});dialogOpen=true;press('d');dialogOpen=false;
  assert.deepEqual(shortcuts,[['OnShortcut','f',false],['OnShortcut','W',false]]);
  bridge.unbindKeyboard();assert.equal(keyHandler,null);
  console.log('PASS shipped keyboard bridge, text-field/dialog guards and cleanup');
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
  } finally {
    bridge.cancel();
  }
}
