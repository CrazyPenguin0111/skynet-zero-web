// The only JS responsibilities are worker transport and browser capabilities.
window.skynet=(()=>{
 let worker,keyboard,keyUp,wheel,blur,visibility,pointerDown,pointerMove,pointerUp,contextMenu,drag,dragFrame=0,frame=0,lastFrame=0,vx=0,vy=0,zoomAnchor;
 let wheelFrame=0,wheelDelta=0,wheelPoint,zoomBusy=false,serial=0;
 const held=new Set(),pending=new Map();
 const direction=key=>({arrowup:'w',arrowleft:'a',arrowdown:'s',arrowright:'d'}[key]||key);
 const editable=target=>target?.closest?.('input,textarea,select,[contenteditable="true"],[role="textbox"]');
 const blocked=()=>document.hidden||document.querySelector('[role="dialog"]')||editable(document.activeElement);
 // Grow the camera's margins before reaching an edge. Compensate scroll when
 // adding space on the left/top so boards never jump on screen. These values
 // live on the viewport, outside Razor's changing canvas/zoom style attributes.
 const makePanRoom=(viewport,dx=0,dy=0)=>{
  const space=viewport.querySelector?.('.multiverse-space');if(!space)return {x:0,y:0};
  const margin=128,padding=getComputedStyle(space);let x=0,y=0;
  if(viewport.scrollLeft+dx<margin){x=Math.max(viewport.clientWidth*2,margin-dx);viewport.style.setProperty('--camera-left',`${parseFloat(padding.paddingLeft)+x}px`);}
  if(viewport.scrollTop+dy<margin){y=Math.max(viewport.clientHeight*2,margin-dy);viewport.style.setProperty('--camera-top',`${parseFloat(padding.paddingTop)+y}px`);}
  if(viewport.scrollWidth-viewport.clientWidth-viewport.scrollLeft-dx<margin)viewport.style.setProperty('--camera-right',`${parseFloat(padding.paddingRight)+Math.max(viewport.clientWidth*2,margin+dx)}px`);
  if(viewport.scrollHeight-viewport.clientHeight-viewport.scrollTop-dy<margin)viewport.style.setProperty('--camera-bottom',`${parseFloat(padding.paddingBottom)+Math.max(viewport.clientHeight*2,margin+dy)}px`);
  if(x||y)viewport.scrollBy({left:x,top:y,behavior:'instant'});
  return {x,y};
 };
 const panViewport=(viewport,x,y)=>{if(!viewport)return;makePanRoom(viewport,x,y);viewport.scrollBy({left:x,top:y,behavior:'instant'});};
 const focusBoard=board=>{
  const viewport=document.querySelector('.multiverse-viewport');if(!board||!viewport)return;
  const target=board.getBoundingClientRect(),bounds=viewport.getBoundingClientRect();
  // Address the camera directly: scrollIntoView may stop at a transformed,
  // clipped ancestor instead of centering the board in the outer viewport.
  viewport.scrollBy({left:target.left+target.width/2-bounds.left-viewport.clientWidth/2,
   top:target.top+target.height/2-bounds.top-viewport.clientHeight/2,behavior:'smooth'});
 };
 const stopPan=()=>{held.clear();cancelAnimationFrame(frame);frame=0;lastFrame=0;vx=vy=0;};
 const drawDrag=()=>{dragFrame=0;if(!drag)return;const shift=makePanRoom(drag.viewport,drag.left-drag.viewport.scrollLeft,drag.top-drag.viewport.scrollTop);drag.left+=shift.x;drag.top+=shift.y;drag.viewport.scrollTo({left:drag.left,top:drag.top,behavior:'instant'});};
 const stopDrag=()=>{
  cancelAnimationFrame(dragFrame);dragFrame=0;if(!drag)return;
  drawDrag();drag.viewport.classList.remove('dragging');
  if(drag.viewport.hasPointerCapture?.(drag.id))drag.viewport.releasePointerCapture(drag.id);
  drag=null;
 };
 const panFrame=now=>{
  frame=0;if(blocked()){stopPan();return;}
  const viewport=document.querySelector('.multiverse-viewport');if(!viewport){stopPan();return;}
  const dt=lastFrame?Math.min((now-lastFrame)/1000,.05):1/60;lastFrame=now;
  let x=Number(held.has('d'))-Number(held.has('a')),y=Number(held.has('s'))-Number(held.has('w'));
  const length=Math.hypot(x,y);if(length){x/=length;y/=length;}
  const blend=1-Math.exp(-20*dt),speed=750;
  vx+=(x*speed-vx)*blend;vy+=(y*speed-vy)*blend;
  panViewport(viewport,vx*dt,vy*dt);
  if(held.size||Math.hypot(vx,vy)>.5)frame=requestAnimationFrame(panFrame);else stopPan();
 };
 const start=()=>{worker=new Worker(new URL('engine-worker.js',document.baseURI),{type:'module'});worker.onmessage=({data})=>{const task=pending.get(data.id);if(!task)return;pending.delete(data.id);data.error?task.reject(new Error(data.error)):task.resolve(data.result);};worker.onerror=e=>{for(const task of pending.values())task.reject(new Error(e.message||'Engine worker failed to load.'));pending.clear();worker?.terminate();worker=null;};};
 return {
  request(json){if(!worker)start();return new Promise((resolve,reject)=>{const id=++serial;pending.set(id,{resolve,reject});worker.postMessage({id,json});});},
  cancel(){worker?.terminate();worker=null;for(const task of pending.values())task.reject(new Error('Search cancelled.'));pending.clear();},
  save(pgn){try{localStorage.setItem('skynet-zero-game',pgn);}catch{}},
  restore(){try{return localStorage.getItem('skynet-zero-game');}catch{return null;}},
  download(name,text){const url=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);},
  copy(text){return navigator.clipboard.writeText(text);},
  bindKeyboard(dotnet){
   this.unbindKeyboard();
   keyboard=e=>{
    if(e.defaultPrevented||e.ctrlKey||e.metaKey||e.altKey||e.isComposing)return;
    const raw=e.key.toLowerCase();
    // Escape closes the app's current dialog, while text editing and all
    // modified browser shortcuts retain their normal behavior.
    if(raw!=='escape'&&(editable(e.target)||blocked()))return;
    if(!['w','a','s','d','f','enter','z','backspace','q','e','t','y','u','i','r','tab','escape','arrowup','arrowdown','arrowleft','arrowright'].includes(raw))return;
    // Tab still traverses menus and controls. On the board/background it
    // cycles playable boards, as in the Steam game.
    if(raw==='tab'&&(e.shiftKey||(e.target?.closest?.('button,a,input,select,textarea')&&!e.target.closest('.board'))))return;
    e.preventDefault();
    const key=direction(raw);
    if(['w','a','s','d'].includes(key)){held.add(key);if(!frame)frame=requestAnimationFrame(panFrame);return;}
    dotnet.invokeMethodAsync('OnShortcut',e.key,e.repeat).catch(error=>console.error('Keyboard command failed',error));
   };
   keyUp=e=>held.delete(direction(e.key.toLowerCase()));
   const zoomFrame=async()=>{
    wheelFrame=0;if(blocked()){wheelDelta=0;return;}
    if(zoomBusy)return;
    const steps=Math.max(-4,Math.min(4,Math.trunc(wheelDelta/40)));if(!steps)return;
    wheelDelta-=steps*40;zoomBusy=true;
    try{await dotnet.invokeMethodAsync('OnWheelZoom',-steps,wheelPoint.x,wheelPoint.y);}
    catch(error){console.error('Zoom failed',error);}
    finally{zoomBusy=false;if(Math.abs(wheelDelta)>=40)wheelFrame=requestAnimationFrame(zoomFrame);}
   };
   wheel=e=>{
    if(e.defaultPrevented||e.ctrlKey||e.metaKey||e.altKey||e.shiftKey||blocked()||!e.target?.closest?.('.multiverse-viewport'))return;
    if(!e.deltaY)return;
    e.preventDefault();wheelPoint={x:e.clientX,y:e.clientY};
    const amount=e.deltaY*(e.deltaMode===1?16:e.deltaMode===2?400:1);
    if(Math.sign(amount)!==Math.sign(wheelDelta))wheelDelta=0;
    wheelDelta=Math.max(-160,Math.min(160,wheelDelta+amount));
    if(!wheelFrame&&!zoomBusy)wheelFrame=requestAnimationFrame(zoomFrame);
   };
   pointerDown=e=>{
    if(e.button!==0||e.isPrimary===false||document.hidden||document.querySelector('[role="dialog"]')||e.ctrlKey||e.metaKey||e.altKey)return;
    const viewport=e.target?.closest?.('.multiverse-viewport');
    if(!viewport||e.target.closest('button,a,input,textarea,select,.board'))return;
    stopDrag();e.preventDefault();viewport.focus?.({preventScroll:true});drag={id:e.pointerId,x:e.clientX,y:e.clientY,left:viewport.scrollLeft,top:viewport.scrollTop,viewport};
    viewport.setPointerCapture?.(e.pointerId);viewport.classList.add('dragging');
   };
   pointerMove=e=>{
    if(!drag||e.pointerId!==drag.id)return;
    if(blocked()){stopDrag();return;}
    e.preventDefault();
    // Keep a fractional target so slow pointer motion does not accumulate a
    // rounding error every frame when the browser rounds scroll positions.
    drag.left+=drag.x-e.clientX;drag.top+=drag.y-e.clientY;
    drag.x=e.clientX;drag.y=e.clientY;
    if(!dragFrame)dragFrame=requestAnimationFrame(drawDrag);
   };
   pointerUp=e=>{if(drag?.id===e.pointerId)stopDrag();};
   contextMenu=e=>{if(blocked()||!e.target?.closest?.('.multiverse-viewport'))return;e.preventDefault();dotnet.invokeMethodAsync('OnShortcut','Escape',false).catch(console.error);};
   blur=()=>{stopPan();stopDrag();cancelAnimationFrame(wheelFrame);wheelFrame=0;wheelDelta=0;};
   visibility=()=>{if(document.hidden)blur();};
   document.addEventListener('keydown',keyboard);
   document.addEventListener('keyup',keyUp);
   document.addEventListener('wheel',wheel,{passive:false});
   document.addEventListener('visibilitychange',visibility);
   document.addEventListener('pointerdown',pointerDown);
   document.addEventListener('pointermove',pointerMove,{passive:false});
   document.addEventListener('pointerup',pointerUp);
   document.addEventListener('pointercancel',pointerUp);
   document.addEventListener('contextmenu',contextMenu);
   window.addEventListener('blur',blur);
  },
  unbindKeyboard(){
   if(keyboard)document.removeEventListener('keydown',keyboard);
   if(keyUp)document.removeEventListener('keyup',keyUp);
   if(wheel)document.removeEventListener('wheel',wheel);
   if(visibility)document.removeEventListener('visibilitychange',visibility);
   if(blur)window.removeEventListener('blur',blur);
   if(pointerDown)document.removeEventListener('pointerdown',pointerDown);
   if(pointerMove)document.removeEventListener('pointermove',pointerMove);
   if(pointerUp){document.removeEventListener('pointerup',pointerUp);document.removeEventListener('pointercancel',pointerUp);}
   if(contextMenu)document.removeEventListener('contextmenu',contextMenu);
   stopPan();stopDrag();cancelAnimationFrame(wheelFrame);wheelFrame=0;wheelDelta=0;
   keyboard=keyUp=wheel=blur=visibility=null;zoomAnchor=null;
   pointerDown=pointerMove=pointerUp=contextMenu=null;
  },
  prepareZoom(x,y){
   const viewport=document.querySelector('.multiverse-viewport'),canvas=document.querySelector('.multiverse-canvas');
   if(!viewport||!canvas)return;
   const bounds=viewport.getBoundingClientRect(),rect=canvas.getBoundingClientRect(),scale=Number(canvas.dataset.zoom);
   x??=bounds.left+viewport.clientWidth/2;y??=bounds.top+viewport.clientHeight/2;
   zoomAnchor={viewport,x,y,worldX:(x-rect.left)/scale,worldY:(y-rect.top)/scale};
  },
  restoreZoom(){
   const canvas=document.querySelector('.multiverse-canvas'),anchor=zoomAnchor;zoomAnchor=null;
   if(!canvas||!anchor)return;
   const rect=canvas.getBoundingClientRect(),scale=Number(canvas.dataset.zoom);
   anchor.viewport.scrollBy({left:rect.left+anchor.worldX*scale-anchor.x,top:rect.top+anchor.worldY*scale-anchor.y,behavior:'instant'});
  },
  pan(x,y){panViewport(document.querySelector('.multiverse-viewport'),x,y);},
  chartIndex(clientX,turns){
   const chart=document.querySelector('.evaluation-chart');if(!chart)return 0;
   const bounds=chart.getBoundingClientRect(),x=(clientX-bounds.left)/bounds.width*300;
   return Math.max(0,Math.min(turns,Math.round((x-10)/280*turns)));
  },
  focusPresent(){focusBoard(document.querySelector('.board.present'));},
  cycleBoards(){
   const boards=[...document.querySelectorAll('.board.playable')];if(!boards.length)return;
   const active=boards.findIndex(board=>board.dataset.focused==='true');
   for(const board of boards)delete board.dataset.focused;
   const next=boards[(active+1)%boards.length];next.dataset.focused='true';focusBoard(next);
  },
  scrollToEnd(){const list=document.querySelector('.moves-list');if(list)list.scrollTop=list.scrollHeight;}
 };
})();
