// Browser-capability tests with a controlled clock and small DOM fixture.
// These exercise the real bridge; they do not claim visual browser coverage.
import assert from 'node:assert/strict';
import path from 'node:path';
import {pathToFileURL} from 'node:url';

const root=path.resolve(process.argv[2]||'artifacts/publish/wwwroot');
const events=new Map(),windowEvents=new Map(),frames=new Map();
let serial=0,time=0,dialog=false;
const viewport={clientWidth:1000,clientHeight:700,scrollWidth:10000,scrollHeight:10000,scrollLeft:2000,scrollTop:2000,
  classList:new Set(),captured:null,
  focus(){document.activeElement=this;},closest(){return null;},
  setPointerCapture(id){this.captured=id;},hasPointerCapture(id){return this.captured===id;},releasePointerCapture(){this.captured=null;},
  scrollBy({left=0,top=0}){this.scrollLeft+=left;this.scrollTop+=top;},
  scrollTo({left,top}){this.scrollLeft=left;this.scrollTop=top;},
  getBoundingClientRect(){return {left:0,top:0};}};
viewport.classList.remove=viewport.classList.delete.bind(viewport.classList);
const chart={getBoundingClientRect(){return {left:100,width:600};}};
const boards=[0,1,2].map(index=>({dataset:{},scrollIntoView(){this.visited=(this.visited||0)+1;}}));
const canvas={dataset:{zoom:'1'},getBoundingClientRect(){return {left:600-viewport.scrollLeft,top:500-viewport.scrollTop};}};
globalThis.window={addEventListener:(type,fn)=>windowEvents.set(type,fn),removeEventListener:type=>windowEvents.delete(type)};
globalThis.document={hidden:false,activeElement:null,
  addEventListener:(type,fn)=>events.set(type,fn),removeEventListener:type=>events.delete(type),
  querySelectorAll:selector=>selector==='.board.playable'?boards:[],
  querySelector:selector=>selector==='[role="dialog"]'?(dialog?{}:null):selector==='.multiverse-viewport'?viewport:selector==='.multiverse-canvas'?canvas:selector==='.evaluation-chart'?chart:null};
globalThis.requestAnimationFrame=fn=>{const id=++serial;frames.set(id,fn);return id;};
globalThis.cancelAnimationFrame=id=>frames.delete(id);
const calls=[];
const target={closest:selector=>selector==='.multiverse-viewport'?viewport:null};
const key=(key,extra={})=>{let prevented=false;events.get('keydown')({key,repeat:false,target,preventDefault(){prevented=true;},...extra});return prevented;};
const release=key=>events.get('keyup')({key});
const tick=async(count=1,ms=1000/60)=>{for(let i=0;i<count;i++){time+=ms;const next=[...frames.values()];frames.clear();for(const fn of next)fn(time);await Promise.resolve();}};
const bind=()=>window.skynet.bindKeyboard({invokeMethodAsync(...args){calls.push(args);return Promise.resolve();}});
await import(pathToFileURL(path.join(root,'browser.js')));
const bridge=window.skynet;bind();

assert.equal(key('f'),true);key('f',{ctrlKey:true});key('a',{isComposing:true});
key('f',{target:{closest:()=>({})}});dialog=true;key('d');dialog=false;
assert.deepEqual(calls,[['OnShortcut','f',false]]);

const motion=async(keys,hz)=>{
  bridge.unbindKeyboard();bind();viewport.scrollLeft=viewport.scrollTop=2000;
  for(const k of keys)key(k);
  await tick(hz,1000/hz);
  return {x:viewport.scrollLeft-2000,y:viewport.scrollTop-2000};
};
const right=await motion(['d'],60),diagonal=await motion(['d','w'],60),highRate=await motion(['d'],120);
assert(right.x>650&&right.y===0,'Holding D must move continuously without repeat events');
assert(diagonal.x>0&&diagonal.y<0,'W+D must move diagonally');
assert(Math.abs(Math.hypot(diagonal.x,diagonal.y)-right.x)<1,'Diagonal speed must equal cardinal speed');
assert(Math.abs(highRate.x-right.x)<15,'Panning must be independent of display refresh rate');
release('d');await tick(60);const stopped=viewport.scrollLeft;await tick(20);
assert.equal(viewport.scrollLeft,stopped,'Motion must stop after key release');
key('s');await tick(5);windowEvents.get('blur')();const blurred=viewport.scrollTop;await tick(20);
assert.equal(viewport.scrollTop,blurred,'Window blur must release held keys');
key('d');await tick(4);dialog=true;await tick(1);const blocked=viewport.scrollLeft;dialog=false;await tick(20);
assert.equal(viewport.scrollLeft,blocked,'Opening a dialog must stop motion');
document.activeElement={closest:()=>({})};key('w');await tick(10);assert.equal(frames.size,0);document.activeElement=null;
console.log('PASS smooth WASD, diagonal normalization, refresh-rate independence, release, blur and input/dialog guards');
const arrows=await motion(['ArrowRight','ArrowUp'],60);
assert(Math.abs(arrows.x-diagonal.x)<1&&Math.abs(arrows.y-diagonal.y)<1,'Arrow keys must share smooth WASD motion');
windowEvents.get('blur')();calls.length=0;
for(const shortcut of ['z','Backspace','Enter','q','e','t','y','u','i','r','Tab'])assert.equal(key(shortcut),true);
assert.deepEqual(calls.map(call=>call[1]),['z','Backspace','Enter','q','e','t','y','u','i','r','Tab']);
assert.equal(key('Tab',{shiftKey:true}),false);
dialog=true;assert.equal(key('Escape'),true);dialog=false;
bridge.cycleBoards();bridge.cycleBoards();bridge.cycleBoards();bridge.cycleBoards();
assert.deepEqual(boards.map(board=>board.visited),[2,1,1]);

const pointer=(type,extra={})=>{
 let prevented=false;events.get(type)({button:0,isPrimary:true,pointerId:7,clientX:400,clientY:300,target,preventDefault(){prevented=true;},...extra});return prevented;
};
viewport.scrollLeft=viewport.scrollTop=1000;
document.activeElement={closest:()=>({})};
assert.equal(pointer('pointerdown'),true,'Background drag must take focus from a slider');
pointer('pointermove',{clientX:340,clientY:390});await tick();
assert.equal(viewport.scrollLeft,1060);assert.equal(viewport.scrollTop,910);
pointer('pointermove',{clientX:320,clientY:380});pointer('pointerup');
assert.equal(viewport.scrollLeft,1080);assert.equal(viewport.scrollTop,920);
assert.equal(viewport.captured,null);assert.equal(viewport.classList.has('dragging'),false);
pointer('pointermove',{clientX:100,clientY:100});await tick();assert.equal(viewport.scrollLeft,1080);
assert.equal(pointer('pointerdown',{target:{closest:selector=>selector==='.multiverse-viewport'?viewport:{}}}),false,'Squares and controls must not start drags');
assert.equal(pointer('pointerdown',{button:2}),false);
pointer('pointerdown');windowEvents.get('blur')();assert.equal(viewport.captured,null);
dialog=true;assert.equal(pointer('pointerdown'),false);dialog=false;
assert.equal(bridge.chartIndex(120,10),0);assert.equal(bridge.chartIndex(400,10),5);assert.equal(bridge.chartIndex(680,10),10);
assert.equal(bridge.chartIndex(0,10),0);assert.equal(bridge.chartIndex(1000,10),10);
console.log('PASS Steam hotkeys, board cycling, background drag, capture cleanup and full-width graph navigation');

const wheel=extra=>{let prevented=false;events.get('wheel')({target,clientX:300,clientY:260,deltaY:120,deltaMode:0,preventDefault(){prevented=true;},...extra});return prevented;};
calls.length=0;assert.equal(wheel({}),true);await tick();
assert.deepEqual(calls,[['OnWheelZoom',-3,300,260]]);
assert.equal(wheel({shiftKey:true}),false);assert.equal(wheel({ctrlKey:true}),false);
assert.equal(wheel({target:{closest:()=>null}}),false);
dialog=true;assert.equal(wheel({}),false);dialog=false;
viewport.scrollLeft=450;viewport.scrollTop=380;
bridge.prepareZoom(300,260);canvas.dataset.zoom='2';bridge.restoreZoom();
assert.equal(600-viewport.scrollLeft+150*2,300);
assert.equal(500-viewport.scrollTop+140*2,260);
bridge.unbindKeyboard();assert.equal(events.size,0);assert.equal(windowEvents.size,0);assert.equal(frames.size,0);
console.log('PASS pointer-centered wheel zoom, canvas-only interception, native modified scrolling and listener cleanup');
