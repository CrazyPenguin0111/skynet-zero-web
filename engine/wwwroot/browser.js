// The only JS responsibilities are worker transport and browser capabilities.
window.skynet=(()=>{
 let worker,serial=0;const pending=new Map();
 const start=()=>{worker=new Worker(new URL('engine-worker.js',document.baseURI),{type:'module'});worker.onmessage=({data})=>{const task=pending.get(data.id);if(!task)return;pending.delete(data.id);data.error?task.reject(new Error(data.error)):task.resolve(data.result);};worker.onerror=e=>{for(const task of pending.values())task.reject(new Error(e.message||'Engine worker failed to load.'));pending.clear();worker?.terminate();worker=null;};};
 return {
  request(json){if(!worker)start();return new Promise((resolve,reject)=>{const id=++serial;pending.set(id,{resolve,reject});worker.postMessage({id,json});});},
  cancel(){worker?.terminate();worker=null;for(const task of pending.values())task.reject(new Error('Search cancelled.'));pending.clear();},
  save(pgn){try{localStorage.setItem('skynet-zero-game',pgn);}catch{}},
  restore(){try{return localStorage.getItem('skynet-zero-game');}catch{return null;}},
  download(name,text){const url=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);},
  copy(text){return navigator.clipboard.writeText(text);},
  focusPresent(){const p=document.querySelector('.board.frontier.active');if(p)p.scrollIntoView({behavior:'smooth',block:'center',inline:'center'});},
  scrollToEnd(){const list=document.querySelector('.moves-list');if(list)list.scrollTop=list.scrollHeight;}
 };
})();
