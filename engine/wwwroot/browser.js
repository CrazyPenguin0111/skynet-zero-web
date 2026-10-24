// The only JS responsibilities are worker transport and browser capabilities.
window.skynet=(()=>{
 let worker,keyboard,serial=0;const pending=new Map();
 const start=()=>{worker=new Worker(new URL('engine-worker.js',document.baseURI),{type:'module'});worker.onmessage=({data})=>{const task=pending.get(data.id);if(!task)return;pending.delete(data.id);data.error?task.reject(new Error(data.error)):task.resolve(data.result);};worker.onerror=e=>{for(const task of pending.values())task.reject(new Error(e.message||'Engine worker failed to load.'));pending.clear();worker?.terminate();worker=null;};};
 return {
  request(json){if(!worker)start();return new Promise((resolve,reject)=>{const id=++serial;pending.set(id,{resolve,reject});worker.postMessage({id,json});});},
  cancel(){worker?.terminate();worker=null;for(const task of pending.values())task.reject(new Error('Search cancelled.'));pending.clear();},
  save(pgn){try{localStorage.setItem('skynet-zero-game',pgn);}catch{}},
  restore(){try{return localStorage.getItem('skynet-zero-game');}catch{return null;}},
  download(name,text){const url=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);},
  copy(text){return navigator.clipboard.writeText(text);},
  bindKeyboard(dotnet){
   if(keyboard)document.removeEventListener('keydown',keyboard);
   keyboard=e=>{
    if(e.defaultPrevented||e.ctrlKey||e.metaKey||e.altKey||e.isComposing)return;
    if(e.target?.closest?.('input,textarea,select,[contenteditable="true"],[role="textbox"]')||document.querySelector('[role="dialog"]'))return;
    if(!['w','a','s','d','f','escape','arrowleft','arrowright'].includes(e.key.toLowerCase()))return;
    e.preventDefault();
    dotnet.invokeMethodAsync('OnShortcut',e.key,e.repeat).catch(error=>console.error('Keyboard command failed',error));
   };
   document.addEventListener('keydown',keyboard);
  },
  unbindKeyboard(){if(keyboard)document.removeEventListener('keydown',keyboard);keyboard=null;},
  pan(x,y){document.querySelector('.multiverse-viewport')?.scrollBy({left:x,top:y,behavior:'instant'});},
  focusPresent(){const p=document.querySelector('.board.present');if(p)p.scrollIntoView({behavior:'smooth',block:'center',inline:'center'});},
  scrollToEnd(){const list=document.querySelector('.moves-list');if(list)list.scrollTop=list.scrollHeight;}
 };
})();
