// Browser-only bridge. Rules, search, model inference and PGN handling are C#.
import { dotnet } from './_framework/dotnet.js';
let dispatch;
const ready=(async()=>{const runtime=await dotnet.withDiagnosticTracing(false).create();const exports=await runtime.getAssemblyExports('SkynetZero.Web.dll');dispatch=exports.SkynetZero.Web.EngineBridge.Dispatch;})();
self.onmessage=async({data})=>{try{await ready;self.postMessage({id:data.id,result:dispatch(data.json)});}catch(e){self.postMessage({id:data.id,error:String(e?.message||e)});}};
