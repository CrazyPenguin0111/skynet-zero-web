// Optional real-browser regression checks. Serve the published wwwroot first.
// Usage: node tools/isometric-browser.mjs [http://127.0.0.1:4183]
// Install Playwright, or supply PLAYWRIGHT_MODULE and optionally CHROMIUM_PATH.
import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
const {chromium}=await import(process.env.PLAYWRIGHT_MODULE??'playwright');
const screenshots=process.env.SKYNET_SCREENSHOTS??join(tmpdir(),'skynet-isometric');
await mkdir(screenshots,{recursive:true});
const browser=await chromium.launch({headless:true,...(process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:{})});
const page=await browser.newPage({viewport:{width:1440,height:960}});
const errors=[];page.on('pageerror',error=>errors.push(error.message));
const scroll=()=>page.locator('.multiverse-viewport').evaluate(v=>({x:v.scrollLeft,y:v.scrollTop}));
async function importGame(pgn){
 if(await page.getByRole('button',{name:'Moves & engine',exact:true}).isVisible())await page.getByRole('button',{name:'Moves & engine',exact:true}).click();
 await page.getByRole('button',{name:'Import 5DPGN',exact:true}).first().click();
 await page.getByRole('textbox',{name:'5DPGN game',exact:true}).fill(pgn);
 await page.getByRole('button',{name:'Open analysis board',exact:false}).click();
 await page.getByRole('dialog').waitFor({state:'hidden'});await page.waitForTimeout(500);
}
try {
await page.goto(process.argv[2]??'http://127.0.0.1:4183');
await page.getByRole('gridcell').first().waitFor({timeout:60000});
await page.getByRole('button',{name:'Analysis board',exact:true}).click();
async function setZoom(value){
 await page.getByRole('slider',{name:'Board zoom',exact:true}).evaluate((input,value)=>{input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));},value);
 await page.waitForFunction(value=>Number(document.querySelector('.multiverse-canvas').dataset.zoom)===value/36,value);
 await page.waitForTimeout(100);
}
async function centerBoards(){
 await page.locator('.multiverse-viewport').evaluate(view=>{
  const boxes=[...view.querySelectorAll('.board')].map(b=>b.getBoundingClientRect());
  const left=Math.min(...boxes.map(b=>b.left)),right=Math.max(...boxes.map(b=>b.right));
  const top=Math.min(...boxes.map(b=>b.top)),bottom=Math.max(...boxes.map(b=>b.bottom)),v=view.getBoundingClientRect();
  view.scrollBy({left:(left+right-v.left-v.right)/2,top:(top+bottom-v.top-v.bottom)/2,behavior:'instant'});
 });
 await page.waitForTimeout(150);
}
async function arrowHitsSquare(pathSelector,label,atEnd=true){
 const error=await page.evaluate(({pathSelector,label,atEnd})=>{
  const path=document.querySelector(pathSelector),square=[...document.querySelectorAll('.square')].find(s=>s.getAttribute('aria-label')===label);
  if(!path||!square)throw new Error(`Missing path or square: ${pathSelector}, ${label}`);
  const point=path.getPointAtLength(atEnd?path.getTotalLength():0),screen=new DOMPoint(point.x,point.y).matrixTransform(path.getScreenCTM());
  const r=square.getBoundingClientRect();return Math.hypot(screen.x-r.x-r.width/2,screen.y-r.y-r.height/2);
 },{pathSelector,label,atEnd});
 assert(error<.1,`${pathSelector} missed ${label} by ${error}px`);
}
await importGame('[Board "standard-t0"]\n1. (0T1)Pe2e4 / (0T1)Pe7e5\n2. (0T2)Nb1>>(0T1)b3 /\n*');
const closePanel=page.getByRole('button',{name:'Close panel',exact:true});if(await closePanel.isVisible())await closePanel.click();
const boardCount=await page.locator('.board').count();
for(const [mode,key] of [['history','t'],['parallel','y']]){
 await page.keyboard.press(key);await page.locator(`.view-${mode}`).waitFor();await page.waitForTimeout(600);
 assert.equal(await page.locator('.board').count(),boardCount,'View must retain every board');
 const matrix=await page.locator('.board').first().evaluate(board=>{const m=new DOMMatrix(getComputedStyle(board).transform);return {a:m.a,b:m.b,c:m.c,d:m.d};});
 assert(Math.abs(matrix.b)>.1&&matrix.c===0&&matrix.d===1,'Boards must stand in an isometric perspective');
 assert.equal(matrix.b<0,mode==='history','History and Parallel must use distinct angles');
 for(const zoom of [12,24,72]){
  await setZoom(zoom);await centerBoards();
  for(const flip of [false,true]){
   if(flip)await page.keyboard.press('u');
   await arrowHitsSquare('.played-travel','b3, empty, timeline 0, T1 white');
   await arrowHitsSquare('.played-travel','b1, White knight, timeline 0, T2 white',false);
   if(flip)await page.keyboard.press('u');
  }
  const ends=await page.locator('.present-line').evaluate(band=>{
   const r=band.getBoundingClientRect(),space=band.parentElement.getBoundingClientRect(),m=new DOMMatrix(getComputedStyle(band).transform);
   return [-1,1].map(sign=>{const x=r.x+r.width/2+sign*band.offsetWidth*m.a/2,y=r.y+r.height/2+sign*band.offsetWidth*m.b/2;return x<space.left||x>space.right||y<space.top||y>space.bottom;});
  });assert(ends.every(Boolean),'Isometric present band must extend outside scroll space');
 }
 await setZoom(30);await centerBoards();
 await page.screenshot({path:`${screenshots}/skynet-web-${mode}.png`});
 // Wheel zoom must preserve the projected square under the cursor.
 const target=page.getByRole('gridcell',{name:'b3, empty, timeline 0, T1 white',exact:true});
 await target.scrollIntoViewIfNeeded();await page.waitForTimeout(100);
 const before=await target.boundingBox();const pointer={x:before.x+before.width/2,y:before.y+before.height/2};
 await page.mouse.move(pointer.x,pointer.y);await page.mouse.wheel(0,-90);
 await page.waitForFunction(()=>Number(document.querySelector('.multiverse-canvas').dataset.zoom)>30/36);await page.waitForTimeout(150);
 const after=await target.boundingBox();assert(Math.hypot(after.x+after.width/2-pointer.x,after.y+after.height/2-pointer.y)<2,'Wheel zoom lost its projected anchor');
 await page.keyboard.press(key);await page.locator('.view-normal').waitFor();
}
console.log('PASS both isometric angles, all boards, original travel endpoints, flipping, pointer zoom and unbounded present band at all zooms');

for(const [mode,key] of [['history','t'],['parallel','y']]){
 await importGame('[Board "standard-t0"]\n*');
 if(await closePanel.isVisible())await closePanel.click();
 await setZoom(36);await page.keyboard.press(key);await page.locator(`.view-${mode}`).waitFor();await page.waitForTimeout(600);
 const initialBoardCount=await page.locator('.board').count();
 await page.getByRole('gridcell',{name:'g1, White knight, timeline 0, T1 white',exact:true}).click();
 await page.getByRole('gridcell',{name:'f3, empty, timeline 0, T1 white',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('.button.submit:not(:disabled)'));
 await page.keyboard.press('z');await page.waitForFunction(count=>document.querySelectorAll('.board').length===count,initialBoardCount);
 await page.keyboard.press('u');
 await page.getByRole('gridcell',{name:'e2, White pawn, timeline 0, T1 white',exact:true}).click();
 await page.getByRole('gridcell',{name:'e4, empty, timeline 0, T1 white',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('.button.submit:not(:disabled)'));
 await page.keyboard.press('z');await page.waitForFunction(count=>document.querySelectorAll('.board').length===count,initialBoardCount);await page.keyboard.press('u');
 await page.getByRole('button',{name:'Focus the present',exact:true}).click();await page.waitForTimeout(600);
 const dragStart=await scroll();await page.mouse.move(60,750);await page.mouse.down();await page.mouse.move(150,690,{steps:8});await page.mouse.up();
 const dragEnd=await scroll();assert(Math.abs(dragEnd.x-dragStart.x+90)<2&&Math.abs(dragEnd.y-dragStart.y-60)<2,`${mode}: diagonal drag`);
 await page.keyboard.down('d');await page.keyboard.down('w');await page.waitForTimeout(250);await page.keyboard.up('d');await page.keyboard.up('w');
 const pan=await scroll();assert(pan.x>dragEnd.x+50&&pan.y<dragEnd.y-50,`${mode}: diagonal WASD`);
 await importGame('[Board "custom"]\n[Size "8x8"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]\n1. (0T1)Na6b8 /\n*');
 if(await closePanel.isVisible())await closePanel.click();
 await page.getByRole('button',{name:'Show checks',exact:true}).click();await page.locator('.check-arrow').waitFor({state:'attached'});
 const title=await page.locator('.check-arrow title').textContent();
 const match=title.match(/^Check from (\w\d), empty, timeline (-?\d+), T(\d+) (white|black) to (\w\d), empty, timeline (-?\d+), T(\d+) (white|black)$/);
 assert(match,'Check arrow title');
 for(const [offset,end] of [[1,false],[5,true]]){
  const label=await page.getByRole('gridcell').filter({hasText:''}).evaluateAll((squares,parts)=>squares.find(s=>s.getAttribute('aria-label').startsWith(parts[0]+',')&&s.getAttribute('aria-label').endsWith(`timeline ${parts[1]}, T${parts[2]} ${parts[3]}`)).getAttribute('aria-label'),match.slice(offset,offset+4));
  await arrowHitsSquare('.check-arrow',label,end);
 }
 await centerBoards();await page.screenshot({path:`${screenshots}/skynet-web-${mode}-check.png`});
 await page.keyboard.press('Escape');await page.keyboard.press(key);await page.locator('.view-normal').waitFor();
}
assert.deepEqual(errors,[]);
console.log('PASS real square selection and undo in both orientations, screen-space drag/WASD, projected check arrows, and normal-view restoration');
} catch(error) {
 await page.screenshot({path:join(screenshots,'failure.png')});
 throw error;
} finally {
 await browser.close();
}
