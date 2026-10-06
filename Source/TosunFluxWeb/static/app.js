const $=s=>document.querySelector(s);
const state={files:[],busy:false,serverAi:false,clientAi:false,sessions:new Map(),videoJob:null,videoLimits:null,profile:null,maxFiles:0};
const labels={source:'원본',quality:'고품질',balanced:'균형',small:'작은 용량',custom:'직접 지정',fit:'맞추기',fill:'채우기',stretch:'늘리기','4k':'4K','4k-uhd':'4K UHD',qhd:'QHD',fhd:'FHD',hd:'HD',sd:'SD',png:'PNG',jpg:'JPG',webp:'WEBP',bmp:'BMP',tiff:'TIFF',gif:'GIF',pdf:'PDF',txt:'TXT',md:'Markdown',csv:'CSV',json:'JSON',mp4:'MP4',webm:'WEBM',mov:'MOV',mkv:'MKV',avi:'AVI','png-sequence':'PNG Sequence',mp3:'MP3',wav:'WAV',flac:'FLAC',m4a:'M4A',ogg:'OGG','jpg-sequence':'JPG Sequence'};
// Browser decoders are platform-specific; conversion/output rules come from the app engine.
const clientImageExtensions=new Set(['png','jpg','jpeg','webp']);
const fileInput=$('#fileInput'),dropZone=$('#dropZone'),target=$('#target'),resolution=$('#resolution'),convertButton=$('#convertButton');

fetch('/api/health').then(r=>{if(!r.ok)throw new Error();return r.json()}).then(info=>{
    state.profile=info.profile;state.maxFiles=info.max_files;state.serverAi=info.ai_upscale;
    state.videoLimits=info.webgpu_video;
    state.clientAi=Boolean(state.profile.upscale.webgpu_model_url&&navigator.gpu&&window.ort?.InferenceSession);
    $('#version').textContent='Tosun Flux Web · v'+info.version;
    $('#fileLimit').textContent='최대 '+state.maxFiles+'개';
    applyProfile();refreshAiOptions();renderFiles();
}).catch(()=>{setStatus('앱 엔진의 변환 설정을 불러오지 못했습니다. 새로고침해 주세요.');$('#engineBadge').textContent='서버 연결 실패'});

function choices(select,entries){select.replaceChildren(...entries.map(([value,label])=>new Option(label,value)));select.disabled=false}
function applyProfile(){
    const profile=state.profile;
    choices(resolution,[['source','원본'],...profile.upscale.factors.map(f=>['scale-'+f,f+'x AI 업스케일']),...Object.entries(profile.resolutions).map(([key,size])=>[key,(labels[key]||key)+' · '+size.join('×')]),['custom','직접 지정']]);
    choices($('#optimize'),profile.optimizations.map(key=>[key,key==='source'?'원본 유지':labels[key]||key]));
    choices($('#aspect'),['source',...profile.aspects].map(key=>[key,labels[key]||key]));
    choices($('#fps'),profile.frame_rates.map(key=>[key,labels[key]||key]));
    $('.segmented').replaceChildren(...profile.fit_modes.map((key,index)=>{
        const label=document.createElement('label'),input=document.createElement('input'),span=document.createElement('span');
        input.type='radio';input.name='fit';input.value=key;input.checked=index===0;span.textContent=labels[key]||key;label.append(input,span);return label;
    }));
    for(const input of[$('#width'),$('#height')])input.max=profile.max_output_dimension;
}
fileInput.addEventListener('change',()=>addFiles(fileInput.files));
dropZone.addEventListener('keydown',e=>{if(!state.busy&&(e.key==='Enter'||e.key===' '))fileInput.click()});
for(const event of['dragenter','dragover'])dropZone.addEventListener(event,e=>{e.preventDefault();if(!state.busy)dropZone.classList.add('dragging')});
for(const event of['dragleave','drop'])dropZone.addEventListener(event,e=>{e.preventDefault();dropZone.classList.remove('dragging')});
dropZone.addEventListener('drop',e=>addFiles(e.dataTransfer.files));
$('#clearButton').addEventListener('click',()=>{state.files=[];renderFiles();refreshTargets()});
resolution.addEventListener('change',updateResolution);
target.addEventListener('change',()=>{$('#fpsWrap').hidden=!(state.files.length&&state.files.every(isVideo)&&state.profile.video_targets.includes(target.value));refreshAiOptions()});
convertButton.addEventListener('click',convert);

function addFiles(items){
    if(state.busy||!state.profile)return;
    const known=new Set(state.files.map(file=>file.name+':'+file.size+':'+file.lastModified));
    for(const file of items){const key=file.name+':'+file.size+':'+file.lastModified;if(!known.has(key)&&state.files.length<state.maxFiles){state.files.push(file);known.add(key)}}
    fileInput.value='';renderFiles();refreshTargets();
}
function renderFiles(){
    $('#fileList').replaceChildren(...state.files.map((file,index)=>{
        const item=document.createElement('li'),name=document.createElement('span'),size=document.createElement('span'),remove=document.createElement('button');
        name.className='file-name';name.textContent=file.name;size.className='file-size';size.textContent=formatBytes(file.size);
        remove.className='remove';remove.type='button';remove.textContent='×';remove.disabled=state.busy;remove.setAttribute('aria-label',file.name+' 삭제');
        remove.onclick=()=>{state.files.splice(index,1);renderFiles();refreshTargets()};item.append(name,size,remove);return item;
    }));
    const total=state.files.reduce((sum,file)=>sum+file.size,0);
    $('#fileSummary').textContent=state.files.length?state.files.length+'개 · '+formatBytes(total):'파일이 없습니다';
    $('#clearButton').disabled=state.busy||!state.files.length;
    convertButton.disabled=state.busy||!state.profile||!state.files.length||!target.value;
    fileInput.disabled=state.busy||!state.profile;
    for(const input of document.querySelectorAll('.settings-panel select,.settings-panel input'))input.disabled=state.busy||!state.profile;
    target.disabled=state.busy||!target.options.length;
    updateResolution();
}
async function refreshTargets(){
    target.replaceChildren();target.disabled=true;
    if(!state.files.length){renderFiles();refreshAiOptions();return}
    try{
        const response=await fetch('/api/targets',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(state.files.map(f=>f.name))});
        if(!response.ok)throw new Error();
        const payload=await response.json();
        for(const value of payload.targets)target.add(new Option(labels[value]||value,value));
        setStatus(payload.targets.length?'설정을 선택하고 변환을 시작하세요.':'함께 변환할 수 있는 공통 출력 형식이 없습니다.');
    }catch{setStatus('출력 형식을 확인하지 못했습니다.')}
    renderFiles();target.dispatchEvent(new Event('change'));
}
function isVideo(file){return state.profile.video_extensions.includes('.'+file.name.split('.').pop().toLowerCase())}
function canClientVideo(){return state.clientAi&&state.profile.upscale.webgpu_video_model_url&&state.files.length>0&&state.profile.video_targets.includes(target.value)&&state.files.every(isVideo)}
function canClientUpscale(){return canClientVideo()||(state.clientAi&&state.files.length>0&&state.profile.image_targets.includes(target.value)&&state.files.every(file=>clientImageExtensions.has(file.name.split('.').pop().toLowerCase())))}
function refreshAiOptions(){
    if(!state.profile)return;
    $('#engineBadge').textContent=state.clientAi?'WebGPU · 앱 엔진 저장':state.serverAi?'서버 AI 엔진':'일반 변환 모드';
    for(const factor of state.profile.upscale.factors){
        const option=resolution.querySelector('option[value="scale-'+factor+'"]'),usable=state.serverAi||canClientUpscale()||(state.clientAi&&!state.files.length);
        option.disabled=!usable;option.textContent=usable?factor+'x AI 업스케일':factor+'x AI 업스케일 · 현재 형식 미지원';
    }
    if(resolution.selectedOptions[0]?.disabled)resolution.value='source';
    updateResolution();
}
function updateResolution(){const custom=resolution.value==='custom',scale=resolution.value.startsWith('scale-');$('#customSize').hidden=!custom;$('#aspect').disabled=state.busy||!state.profile||custom||scale}
async function convert(){
    if(state.busy||!state.profile||!state.files.length||!target.value)return;
    const custom=resolution.value==='custom',scale=resolution.value.startsWith('scale-')?Number(resolution.value.slice(6)):1;
    if(custom&&(!$('#width').checkValidity()||!$('#height').checkValidity()||!$('#width').value||!$('#height').value)){setStatus('직접 해상도는 가로·세로 2~'+state.profile.max_output_dimension+' 범위로 입력하세요.');return}
    const files=state.files.slice(),options={target:target.value,optimize:$('#optimize').value,resolution:custom||scale!==1?'source':resolution.value,aspect:custom||scale!==1?'source':$('#aspect').value,fit:document.querySelector('input[name=fit]:checked').value,fps:$('#fpsWrap').hidden?'source':$('#fps').value,scale_factor:scale};
    if(custom){options.width=$('#width').value;options.height=$('#height').value}
    const local=scale!==1&&canClientUpscale();
    for(const link of $('#downloads').querySelectorAll('a'))URL.revokeObjectURL(link.href);
    $('#downloads').replaceChildren();
    state.busy=true;renderFiles();convertButton.textContent=local?'업스케일 중…':'변환 중…';setProgress(0);
    try{
        if(local&&canClientVideo())await convertVideoWithWebGpu(files,options,scale);
        else if(local){
            const upscaled=await convertWithWebGpu(files,scale);
            await convertOnServer(upscaled,{...options,scale_factor:1,fps:'source',webgpu_scale:scale});
        }else await convertOnServer(files,options);
    }catch(error){setStatus(error.message||'변환에 실패했습니다.');setProgress(0)}
    finally{finish()}
}
async function convertWithWebGpu(files,scale){
    $('#progressBar').classList.add('indeterminate');
    setStatus('WebGPU 엔진과 Real-ESRGAN 모델을 준비하고 있습니다. 첫 실행은 약 32MB를 다운로드합니다.');
    await webGpuSession();$('#progressBar').classList.remove('indeterminate');
    const results=[];
    for(let index=0;index<files.length;index++){
        const file=files[index];setStatus(file.name+' · '+scale+'x WebGPU 업스케일 중');
        const blob=await upscaleImage(file,scale,amount=>setProgress((index+amount)/files.length*90));
        results.push(new File([blob],file.name.replace(/\.[^.]+$/,'')+'.png',{type:'image/png'}));
    }
    return results;
}
async function webGpuSession(video=false){
    const url=video?state.profile.upscale.webgpu_video_model_url:state.profile.upscale.webgpu_model_url;
    if(state.sessions.has(url))return state.sessions.get(url);
    // Only keep the active model in GPU memory.
    for(const session of state.sessions.values())await session.release();
    state.sessions.clear();
    ort.env.wasm.numThreads=1;ort.env.wasm.wasmPaths='https://cdn.jsdelivr.net/npm/onnxruntime-web@1.30.0/dist/';
    const session=await ort.InferenceSession.create(url,{executionProviders:['webgpu'],graphOptimizationLevel:'all'});
    state.sessions.set(url,session);return session;
}
async function upscaleImage(file,scale,onProgress,video=false){
    const bitmap=await createImageBitmap(file);
    try{
        const nativeScale=state.profile.upscale.native_scale,limit=state.profile.max_output_dimension;
        if(bitmap.width*scale>limit||bitmap.height*scale>limit)throw new Error(file.name+': 결과 한 변은 '+limit+'픽셀을 넘을 수 없습니다.');
        if(bitmap.width*nativeScale>limit||bitmap.height*nativeScale>limit)throw new Error(file.name+': WebGPU 중간 이미지가 브라우저 크기 제한을 넘습니다.');
        const source=document.createElement('canvas'),output=document.createElement('canvas'),tileCanvas=document.createElement('canvas');
        source.width=bitmap.width;source.height=bitmap.height;source.getContext('2d',{willReadFrequently:true}).drawImage(bitmap,0,0);
        // Keep the native output; the app engine performs the same 2x Lanczos reduction.
        output.width=bitmap.width*nativeScale;output.height=bitmap.height*nativeScale;
        const sourceContext=source.getContext('2d',{willReadFrequently:true}),outputContext=output.getContext('2d'),session=await webGpuSession(video),tileSize=64,padding=8,columns=Math.ceil(bitmap.width/tileSize),rows=Math.ceil(bitmap.height/tileSize),total=columns*rows;
        let completed=0;
        for(let y=0;y<bitmap.height;y+=tileSize){for(let x=0;x<bitmap.width;x+=tileSize){
            const coreWidth=Math.min(tileSize,bitmap.width-x),coreHeight=Math.min(tileSize,bitmap.height-y),left=Math.max(0,x-padding),top=Math.max(0,y-padding),right=Math.min(bitmap.width,x+coreWidth+padding),bottom=Math.min(bitmap.height,y+coreHeight+padding),width=right-left,height=bottom-top,image=sourceContext.getImageData(left,top,width,height),pixels=width*height,inputData=new Float32Array(pixels*3);
            for(let pixel=0;pixel<pixels;pixel++){inputData[pixel]=image.data[pixel*4]/255;inputData[pixels+pixel]=image.data[pixel*4+1]/255;inputData[pixels*2+pixel]=image.data[pixel*4+2]/255}
            const input=new ort.Tensor('float32',inputData,[1,3,height,width]);
            const results=await session.run({[session.inputNames[0]]:input}),result=results[session.outputNames[0]],nativeWidth=width*nativeScale,nativeHeight=height*nativeScale,rgba=new Uint8ClampedArray(nativeWidth*nativeHeight*4),nativePixels=nativeWidth*nativeHeight;
            if(result.type!=='float32'||result.dims[2]!==nativeHeight||result.dims[3]!==nativeWidth)throw new Error('WebGPU 모델 출력이 앱의 업스케일 규격과 다릅니다.');
            for(let pixel=0;pixel<nativePixels;pixel++){rgba[pixel*4]=channel(result.data[pixel]);rgba[pixel*4+1]=channel(result.data[nativePixels+pixel]);rgba[pixel*4+2]=channel(result.data[nativePixels*2+pixel]);rgba[pixel*4+3]=255}
            tileCanvas.width=nativeWidth;tileCanvas.height=nativeHeight;tileCanvas.getContext('2d').putImageData(new ImageData(rgba,nativeWidth,nativeHeight),0,0);
            outputContext.drawImage(tileCanvas,(x-left)*nativeScale,(y-top)*nativeScale,coreWidth*nativeScale,coreHeight*nativeScale,x*nativeScale,y*nativeScale,coreWidth*nativeScale,coreHeight*nativeScale);
            input.dispose?.();result.dispose?.();completed++;onProgress(completed/total);await new Promise(requestAnimationFrame);
        }}
        outputContext.globalCompositeOperation='destination-in';outputContext.drawImage(bitmap,0,0,output.width,output.height);
        return await new Promise((resolve,reject)=>output.toBlob(blob=>blob?resolve(blob):reject(new Error('출력 이미지를 만들지 못했습니다.')),'image/png'));
    }finally{bitmap.close()}
}
function channel(value){return Math.max(0,Math.min(255,Math.round(value*255)))}
function downloadBlob(blob,name){
    const link=document.createElement('a');link.className='text-button';
    link.href=URL.createObjectURL(blob);link.download=name;link.textContent=name+' 다운로드';$('#downloads').append(link);link.click();
}
async function checkedResponse(response){
    if(response.ok)return response;
    let message='서버 요청을 처리하지 못했습니다.';
    try{const payload=await response.json();if(typeof payload.detail==='string')message=payload.detail}catch{}
    throw new Error(message);
}
async function convertVideoWithWebGpu(files,options,scale){
    if(files.reduce((bytes,file)=>bytes+file.size,0)>state.videoLimits.max_batch_bytes)throw new Error('이번 배치의 전체 영상 용량 제한은 '+formatBytes(state.videoLimits.max_batch_bytes)+'입니다. 파일을 나눠 주세요.');
    let downloadedBytes=0;
    setStatus('영상용 WebGPU 모델을 준비합니다. 첫 실행은 약 1.2MB를 다운로드합니다.');
    $('#progressBar').classList.add('indeterminate');await webGpuSession(true);$('#progressBar').classList.remove('indeterminate');
    for(let fileIndex=0;fileIndex<files.length;fileIndex++){
        const file=files[fileIndex],data=new FormData();data.append('file',file,file.name);
        for(const[key,value]of Object.entries({target:options.target,optimize:options.optimize,fps:options.fps,scale}))data.append(key,String(value));
        setStatus(file.name+' · 원본 영상 업로드 및 프레임 준비 중');
        const job=await (await checkedResponse(await fetch('/api/webgpu/video',{method:'POST',body:data}))).json();
        state.videoJob=job.id;
        const base='/api/webgpu/video/'+job.id;
        try{
            for(let index=0;;index++){
                const response=await checkedResponse(await fetch(base+'/frames/'+index));
                if(response.status===204)break;
                const source=await response.blob();
                setStatus(file.name+' · '+scale+'x WebGPU · 프레임 '+(index+1)+' / 약 '+job.frames+' · '+job.fps+' FPS');
                const result=await upscaleImage(source,scale,amount=>setProgress((fileIndex+Math.min(.95,(index+amount)/job.frames*.95))/files.length*100),true);
                const frame=new FormData();frame.append('file',result,'frame.png');
                await checkedResponse(await fetch(base+'/frames/'+index,{method:'PUT',body:frame}));
            }
            setStatus(file.name+' · 오디오 결합 및 최종 저장 중');$('#progressBar').classList.add('indeterminate');
            const response=await checkedResponse(await fetch(base+'/finish',{method:'POST'})),blob=await response.blob();
            downloadedBytes+=blob.size;
            if(downloadedBytes>state.videoLimits.max_batch_bytes)throw new Error('배치 결과 용량 제한을 넘었습니다. 이미 완료된 파일은 아래 링크에 남아 있습니다. 나머지 파일은 나눠 변환해 주세요.');
            downloadBlob(blob,downloadName(response.headers.get('Content-Disposition'))||file.name+'.'+options.target);
            setProgress((fileIndex+1)/files.length*100);$('#progressBar').classList.remove('indeterminate');
        }finally{
            await fetch(base,{method:'DELETE'}).catch(()=>{});state.videoJob=null;
        }
    }
    setStatus(files.length+'개 영상 저장 완료. 자동 다운로드가 막혔다면 아래 결과 링크를 눌러 주세요.');
}
window.addEventListener('pagehide',()=>{if(state.videoJob)fetch('/api/webgpu/video/'+state.videoJob,{method:'DELETE',keepalive:true}).catch(()=>{})});
function convertOnServer(files,options){
    const data=new FormData();files.forEach(file=>data.append('files',file,file.name));
    for(const[key,value]of Object.entries(options))data.append(key,String(value));
    return new Promise((resolve,reject)=>{
        const request=new XMLHttpRequest(),local=options.webgpu_scale!==undefined;
        request.open('POST','/api/convert');request.responseType='blob';
        request.upload.onprogress=e=>{if(e.lengthComputable){setProgress(local?90+e.loaded/e.total*10:e.loaded/e.total*100);setStatus((local?'확대 결과를 앱 엔진에 전송':'업로드')+' 중 · '+Math.round(e.loaded/e.total*100)+'%')}};
        request.upload.onload=()=>{$('#progressBar').classList.add('indeterminate');setStatus(local?'앱과 동일한 엔진으로 압축·저장 중입니다.':'변환 중입니다. 파일 크기에 따라 시간이 걸릴 수 있습니다.')};
        request.onload=async()=>{
            if(request.status>=200&&request.status<300){
                const name=downloadName(request.getResponseHeader('Content-Disposition'))||'Tosun-Flux.'+options.target;
                downloadBlob(request.response,name);setProgress(100);setStatus(name+' 다운로드를 시작했습니다.');resolve();
            }else{
                const text=await request.response.text();let message=text;
                try{message=JSON.parse(text).detail}catch{}
                reject(new Error(typeof message==='string'?message:'변환 요청을 처리하지 못했습니다.'));
            }
        };
        request.onerror=()=>reject(new Error('서버에 연결하지 못했습니다.'));
        request.send(data);
    });
}
function finish(){state.busy=false;$('#progressBar').classList.remove('indeterminate');convertButton.textContent='변환 시작';renderFiles();refreshAiOptions()}
function setStatus(text){$('#statusText').textContent=text}
function setProgress(value){$('#progressBar').style.width=value+'%'}
function formatBytes(bytes){if(!bytes)return'0 B';const units=['B','KB','MB','GB'],i=Math.min(Math.floor(Math.log(bytes)/Math.log(1024)),3);return(bytes/1024**i).toFixed(i?1:0)+' '+units[i]}
function downloadName(header){if(!header)return null;const utf=header.match(/filename\*=UTF-8''([^;]+)/i);if(utf)return decodeURIComponent(utf[1]);const plain=header.match(/filename="?([^";]+)"?/i);return plain?.[1]||null}
