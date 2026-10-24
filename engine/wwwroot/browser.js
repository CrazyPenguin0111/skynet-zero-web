// The only JS responsibilities are worker transport and browser capabilities.
window.skynet=(()=>{
 let worker,keyboard,keyUp,wheel,blur,visibility,frame=0,lastFrame=0,vx=0,vy=0,zoomAnchor;
 let wheelFrame=0,wheelDelta=0,wheelPoint,zoomBusy=false,serial=0;
 const held=new Set(),pending=new Map();
 const editable=target=>target?.closest?.('input,textarea,select,[contenteditable="true"],[role="textbox"]');
 const blocked=()=>document.hidden||document.querySelector('[role="dialog"]')||editable(document.activeElement);
 const stopPan=()=>{held.clear();cancelAnimationFrame(frame);frame=0;lastFrame=0;vx=vy=0;};
 const panFrame=now=>{
  frame=0;if(blocked()){stopPan();return;}
  const viewport=document.querySelector('.multiverse-viewport');if(!viewport){stopPan();return;}
  const dt=lastFrame?Math.min((now-lastFrame)/1000,.05):1/60;lastFrame=now;
  let x=Number(held.has('d'))-Number(held.has('a')),y=Number(held.has('s'))-Number(held.has('w'));
  const length=Math.hypot(x,y);if(length){x/=length;y/=length;}
  const blend=1-Math.exp(-20*dt),speed=750;
  vx+=(x*speed-vx)*blend;vy+=(y*speed-vy)*blend;
  viewport.scrollBy({left:vx*dt,top:vy*dt,behavior:'instant'});
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
    if(editable(e.target)||blocked())return;
    if(!['w','a','s','d','f','escape','arrowleft','arrowright'].includes(e.key.toLowerCase()))return;
    e.preventDefault();
    const key=e.key.toLowerCase();
    if(['w','a','s','d'].includes(key)){held.add(key);if(!frame)frame=requestAnimationFrame(panFrame);return;}
    dotnet.invokeMethodAsync('OnShortcut',e.key,e.repeat).catch(error=>console.error('Keyboard command failed',error));
   };
   keyUp=e=>held.delete(e.key.toLowerCase());
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
   blur=()=>{stopPan();cancelAnimationFrame(wheelFrame);wheelFrame=0;wheelDelta=0;};
   visibility=()=>{if(document.hidden)blur();};
   document.addEventListener('keydown',keyboard);
   document.addEventListener('keyup',keyUp);
   document.addEventListener('wheel',wheel,{passive:false});
   document.addEventListener('visibilitychange',visibility);
   window.addEventListener('blur',blur);
  },
  unbindKeyboard(){
   if(keyboard)document.removeEventListener('keydown',keyboard);
   if(keyUp)document.removeEventListener('keyup',keyUp);
   if(wheel)document.removeEventListener('wheel',wheel);
   if(visibility)document.removeEventListener('visibilitychange',visibility);
   if(blur)window.removeEventListener('blur',blur);
   stopPan();cancelAnimationFrame(wheelFrame);wheelFrame=0;wheelDelta=0;
   keyboard=keyUp=wheel=blur=visibility=null;zoomAnchor=null;
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
  pan(x,y){document.querySelector('.multiverse-viewport')?.scrollBy({left:x,top:y,behavior:'instant'});},
  focusPresent(){const p=document.querySelector('.board.present');if(p)p.scrollIntoView({behavior:'smooth',block:'center',inline:'center'});},
  scrollToEnd(){const list=document.querySelector('.moves-list');if(list)list.scrollTop=list.scrollHeight;}
 };
})();
