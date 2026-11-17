import * as THREE from 'three';
import {HDRLoader} from 'three/addons/loaders/HDRLoader.js';

const maps=[
 {id:'studio_small_09',ko:'소프트박스 스튜디오',en:'Softbox studio'},
 {id:'studio_small_03',ko:'콘트라스트 스튜디오',en:'Contrast studio'},
 {id:'venice_sunset',ko:'베니스 노을',en:'Venice sunset'},
 {id:'forest_slope',ko:'숲속 자연광',en:'Forest daylight'}
];

export function createEnvironment(renderer,scene,changed,dragging){
 const settings={enabled:false,map:'studio_small_09',intensity:1,rotation:0,background:false,blur:.15,backgroundIntensity:1};
 let texture=null,target=null,token=0,loading=false,disposed=false,error='',customName='',root=null,english=false;
 const generator=new THREE.PMREMGenerator(renderer);generator.compileEquirectangularShader();
 const t=(ko,en)=>english?en:ko;
 function state(){return {...settings,loaded:!!texture,loading,error,customName,width:texture?.image.width??0,height:texture?.image.height??0};}
 function notify(){sync();changed();}
 function configure(value){
  for(const key of ['enabled','background'])if(typeof value[key]==='boolean')settings[key]=value[key];
  for(const [key,min,max] of [['intensity',0,10],['rotation',0,360],['blur',0,1],['backgroundIntensity',0,5]])if(Number.isFinite(value[key]))settings[key]=THREE.MathUtils.clamp(value[key],min,max);
  if(settings.enabled&&!texture&&!loading&&!error)void select(settings.map);
  sync();
 }
 function set(value){configure(value);notify();}
 function validate(buffer){
  if(buffer.byteLength>128*1024*1024)throw new Error(t('HDR 파일은 128MB 이하로 선택하세요.','Choose an HDR file smaller than 128 MB.'));
  const header=new TextDecoder().decode(new Uint8Array(buffer,0,Math.min(buffer.byteLength,8192)));
  const size=header.match(/-Y\s+(\d+)\s+\+X\s+(\d+)/);
  if(!/^#\?(RADIANCE|RGBE)/.test(header)||!size)throw new Error(t('지원되는 Radiance .hdr 파노라마가 아닙니다.','Not a supported Radiance .hdr panorama.'));
  const h=Number(size[1]),w=Number(size[2]);
  if(w<2||h<2||w>Math.min(8192,renderer.capabilities.maxTextureSize)||h>4096||w*h>33554432)throw new Error(t('HDR 해상도는 최대 8192 × 4096입니다.','HDR resolution must be at most 8192 × 4096.'));
 }
 async function load(id,read,name=''){
  const request=++token;loading=true;error='';notify();let next=null,nextTarget=null;
  try{
   const buffer=await read();if(disposed||request!==token)return false;validate(buffer);
   const data=new HDRLoader().parse(buffer);
   next=new THREE.DataTexture(data.data,data.width,data.height,THREE.RGBAFormat,data.type);
   next.colorSpace=THREE.LinearSRGBColorSpace;next.minFilter=next.magFilter=THREE.LinearFilter;next.generateMipmaps=false;next.flipY=true;next.mapping=THREE.EquirectangularReflectionMapping;next.needsUpdate=true;
   nextTarget=generator.fromEquirectangular(next);
   const previous=texture,previousTarget=target;texture=next;target=nextTarget;next=null;nextTarget=null;
   settings.map=id;customName=name;loading=false;notify();previousTarget?.dispose();previous?.dispose();return true;
  }catch(e){if(request===token&&!disposed){loading=false;error=e.message||String(e);notify();}return false;}
  finally{nextTarget?.dispose();next?.dispose();}
 }
 async function select(id){
  const entry=maps.find(m=>m.id===id);if(!entry)return false;
  settings.enabled=true;
  return load(id,async()=>{const response=await fetch('./environments/'+entry.id+'.hdr');if(!response.ok)throw new Error(t('HDR 파일을 읽지 못했습니다.','Could not read HDR file.'));return response.arrayBuffer();});
 }
 async function importFile(file){
  if(!file)return false;
  if(!/\.hdr$/i.test(file.name)||file.size>128*1024*1024){error=t('128MB 이하의 .hdr 파일을 선택하세요.','Choose a .hdr file smaller than 128 MB.');sync();return false;}
  settings.enabled=true;return load('custom',()=>file.arrayBuffer(),file.name);
 }
 function apply(studio){
  const active=settings.enabled&&!!texture;
  scene.environment=active?target.texture:studio;
  scene.environmentIntensity=active?settings.intensity:1;
  scene.environmentRotation.set(0,active?THREE.MathUtils.degToRad(settings.rotation):0,0);
  scene.background=active&&settings.background?texture:new THREE.Color(0x202328);
  scene.backgroundRotation.copy(scene.environmentRotation);scene.backgroundBlurriness=settings.blur;scene.backgroundIntensity=settings.backgroundIntensity;
  return active;
 }
 function mount(parent,en){
  english=en;root=document.createElement('details');root.id='hdr-settings';root.open=true;
  const summary=document.createElement('summary');summary.textContent=t('HDR 스카이돔','HDR sky dome');root.append(summary);
  const enabled=document.createElement('label');enabled.textContent=t('HDR 조명 사용','Enable HDR lighting');const checkbox=document.createElement('input');checkbox.type='checkbox';checkbox.dataset.env='enabled';checkbox.onchange=()=>set({enabled:checkbox.checked});enabled.append(checkbox);root.append(enabled);
  const gallery=document.createElement('div');gallery.className='hdr-gallery';
  for(const entry of maps){const button=document.createElement('button');button.type='button';button.dataset.map=entry.id;button.title=t(entry.ko,entry.en);const img=document.createElement('img');img.src='./environments/'+entry.id+'.jpg';img.alt='';const label=document.createElement('span');label.textContent=t(entry.ko,entry.en);button.append(img,label);button.onclick=()=>void select(entry.id);gallery.append(button);}root.append(gallery);
  const file=document.createElement('input');file.type='file';file.accept='.hdr';file.hidden=true;file.onchange=()=>{void importFile(file.files[0]);file.value='';};
  const open=document.createElement('button');open.type='button';open.textContent=t('내 HDR 파일 열기…','Open HDR file…');open.onclick=()=>file.click();root.append(open,file);
  const status=document.createElement('p');status.id='hdr-status';status.setAttribute('aria-live','polite');root.append(status);
  for(const [key,ko,en,min,max,step] of [['rotation','회전','Rotation',0,360,1],['intensity','조명 밝기','Lighting',0,10,.1],['blur','배경 흐림','Background blur',0,1,.01],['backgroundIntensity','배경 밝기','Background brightness',0,5,.1]]){
   const label=document.createElement('label');label.textContent=t(ko,en);const span=document.createElement('span'),input=document.createElement('input'),output=document.createElement('output');input.type='range';Object.assign(input,{min,max,step});input.dataset.env=key;input.oninput=()=>set({[key]:Number(input.value)});span.append(input,output);label.append(span);root.append(label);
  }
  const label=document.createElement('label');label.textContent=t('HDR 배경 표시','Show HDR background');const bg=document.createElement('input');bg.type='checkbox';bg.dataset.env='background';bg.onchange=()=>set({background:bg.checked});label.append(bg);root.append(label);
  const note=document.createElement('p');note.textContent=t('Shift + 좌우 드래그: 돔 회전. 배경을 숨겨도 조명·반사는 유지됩니다. 플랫/툰 재질에서는 HDR 효과가 제한됩니다.','Shift + drag: rotate dome. Lighting and reflections remain when the background is hidden. HDR effects are limited with flat/toon materials.');root.append(note);
  parent.append(root);sync();
 }
 function sync(){
  if(!root)return;
  for(const input of root.querySelectorAll('[data-env]')){const key=input.dataset.env;input.disabled=(key==='blur'||key==='backgroundIntensity')&&!settings.background;if(input.type==='checkbox')input.checked=settings[key];else{input.value=settings[key];input.nextElementSibling.textContent=key==='rotation'?Math.round(settings[key])+'°':settings[key].toFixed(2);}}
  for(const button of root.querySelectorAll('[data-map]'))button.setAttribute('aria-pressed',String(button.dataset.map===settings.map&&!!texture));
  const entry=maps.find(m=>m.id===settings.map);root.querySelector('#hdr-status').textContent=loading?t('HDR 불러오는 중…','Loading HDR…'):error?t('HDR 오류: ','HDR error: ')+error:texture?(customName||t(entry?.ko??'',entry?.en??''))+' · '+texture.image.width+' × '+texture.image.height:t('맵을 선택하면 HDR 조명이 켜집니다.','Select a map to enable HDR lighting.');
 }
 let gesture=null;
 const canvas=renderer.domElement;
 canvas.addEventListener('pointerdown',e=>{if(!e.shiftKey||e.button!==0||!settings.enabled||!texture)return;gesture={id:e.pointerId,x:e.clientX,angle:settings.rotation};dragging(true);canvas.setPointerCapture(e.pointerId);e.preventDefault();e.stopImmediatePropagation();},true);
 canvas.addEventListener('pointermove',e=>{if(!gesture||gesture.id!==e.pointerId)return;set({rotation:((gesture.angle+(e.clientX-gesture.x)*.5)%360+360)%360});e.preventDefault();e.stopImmediatePropagation();},true);
 const end=e=>{if(!gesture||gesture.id!==e.pointerId)return;gesture=null;dragging(false);if(canvas.hasPointerCapture(e.pointerId))canvas.releasePointerCapture(e.pointerId);e.preventDefault();e.stopImmediatePropagation();};
 canvas.addEventListener('pointerup',end,true);canvas.addEventListener('pointercancel',end,true);canvas.addEventListener('lostpointercapture',()=>{if(gesture){gesture=null;dragging(false);}});
 return {settings,state,configure,set,select,importFile,apply,mount,get texture(){return texture;},dispose(){disposed=true;token++;target?.dispose();texture?.dispose();generator.dispose();}};
}
