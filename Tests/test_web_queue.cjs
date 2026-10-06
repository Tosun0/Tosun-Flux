// Run with: node Tests/test_web_queue.cjs
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const crypto=require('node:crypto').webcrypto;

function element(){return {value:'',hidden:false,style:{},dataset:{},options:[],classList:{add(){},remove(){}},addEventListener(){},append(){},remove(){},click(){},setAttribute(){},replaceChildren(){},querySelector:element,querySelectorAll:()=>[],checkValidity:()=>true}}
const elements=new Map(),document={body:element(),createElement:element,querySelectorAll:()=>[],querySelector(selector){if(!elements.has(selector))elements.set(selector,element());return elements.get(selector)}};
class MockRequest{constructor(){this.upload={};this.status=200;this.response=new Blob(['too big'])}open(){}getResponseHeader(){return 'attachment; filename="large.gif"'}send(){queueMicrotask(()=>this.onload())}}
const context=vm.createContext({document,crypto,URL,Blob,File,FormData,XMLHttpRequest:MockRequest,window:{addEventListener(){}},fetch:()=>new Promise(()=>{})});
vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../Source/TosunFluxWeb/static/app.js'),'utf8'),context);
vm.runInContext(`
state.profile={video_extensions:['.mp4'],video_targets:['mp4','gif'],image_targets:['png','gif'],upscale:{webgpu_video_model_url:'video-model'}};
state.clientAi=true;state.maxFiles=20;state.videoLimits={max_batch_bytes:1024};
target.value='gif';resolution.value='scale-2';$('#optimize').value='source';$('#aspect').value='source';$('#fps').value='24';document.querySelector('input[name=fit]:checked').value='fit';
renderFiles=()=>{};refreshTargets=async()=>{};refreshAiOptions=()=>{};
const visits=[];
const realConvertOnServer=convertOnServer;
let releaseFirst;
const gate=new Promise(resolve=>releaseFirst=resolve);
const fixture=name=>({name,size:10,lastModified:1});
convertVideoWithWebGpu=async(file,options,scale)=>{
    visits.push(file.name);if(state.queue.filter(entry=>entry.status==='processing').length!==1)throw new Error('not sequential');
    if(file.name==='first.mp4')await gate;
    if(file.name==='broken.mp4')throw new Error('invalid video');
    if(options.target!=='gif'||scale!==2||options.fps!=='24')throw new Error('settings changed');
    setProgress(55);downloadBlob(new Blob(['video']),file.name+'.gif');
};
convertWithWebGpu=async(file)=>({name:file.name+'.png',size:12});
convertOnServer=async(files,options)=>{visits.push(files[0].name);if(options.fps!=='source'||options.webgpu_scale!==2)throw new Error('image has video FPS');downloadBlob(new Blob(['image']),'image.gif')};
`,context);

(async()=>{
    vm.runInContext(`addFiles([fixture('first.mp4'),fixture('broken.mp4'),fixture('image.png')]);`,context);
    const running=vm.runInContext('convert()',context);
    assert.equal(vm.runInContext('state.busy',context),true);
    assert.equal(vm.runInContext("state.queue[0].status",context),'processing');
    vm.runInContext(`addFiles([fixture('later.mp4'),fixture('later.mp4')]);target.value='mp4';releaseFirst();`,context);
    await running;
    assert.deepEqual(JSON.parse(vm.runInContext('JSON.stringify(visits)',context)),['first.mp4','broken.mp4','image.png.png','later.mp4']);
    assert.deepEqual(JSON.parse(vm.runInContext('JSON.stringify(state.queue.map(entry=>entry.status))',context)),['completed','failed','completed','completed']);
    assert.equal(vm.runInContext('state.busy',context),false);
    assert.equal(vm.runInContext('pendingFiles().length',context),0);
    assert.match(vm.runInContext('state.queue[1].detail',context),/invalid video/);
    assert.equal(vm.runInContext('state.queue.filter(entry=>entry.result).length',context),3);
    vm.runInContext("state.queue[1].status='processing';state.videoLimits.max_batch_bytes=1;",context);
    assert.throws(()=>vm.runInContext("downloadBlob(new Blob(['too big']),'large.gif')",context),/결과 보관 용량/);
    await assert.rejects(Promise.race([vm.runInContext("realConvertOnServer([new File(['source'],'image.png')],{target:'gif'})",context),new Promise((_,reject)=>setTimeout(()=>reject(new Error('XHR queue hung')),2000))]),/결과 보관 용량/);
    vm.runInContext('for(const entry of state.queue)if(entry.result)URL.revokeObjectURL(entry.result.url)',context);
    console.log('PASS: sequential queue, late enqueue, dedupe, isolated failure, fixed settings, image FPS, downloads, and result limit');
})().catch(error=>{console.error(error);process.exitCode=1});
