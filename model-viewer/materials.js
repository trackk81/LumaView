import * as THREE from 'three';

function textureFile(texture,resolvedNames,embedded){
 if(!texture)return '';
 const reference=texture.userData.externalFileName;
 if(reference)return resolvedNames.get(reference)??reference.split(/[\\/]/).pop();
 const src=texture.image?.currentSrc||texture.image?.src;
 if(src&&!/^(blob:|data:)/i.test(src)){
  try{return decodeURIComponent(new URL(src,location.href).pathname.split('/').pop());}catch{}
 }
 return texture.name||embedded;
}

function opacityImage(image){
 // Three.js samples green for an alphaMap. Use an image's alpha channel when present.
 const canvas=document.createElement('canvas');
 canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;
 const context=canvas.getContext('2d',{willReadFrequently:true});
 context.drawImage(image,0,0);
 const pixels=context.getImageData(0,0,canvas.width,canvas.height);
 const data=pixels.data;
 let i=3;
 while(i<data.length&&data[i]===255)i+=4;
 if(i>=data.length)return image;
 for(i=0;i<data.length;i+=4){
  const alpha=data[i+3];
  data[i]=data[i+1]=data[i+2]=alpha;data[i+3]=255;
 }
 context.putImageData(pixels,0,0);
 return canvas;
}

async function loadImage(file,slot,sampler,gltf){
 const url=URL.createObjectURL(file);
 try{
  const image=new Image();
  image.src=url;await image.decode();
  const texture=new THREE.Texture(slot==='alphaMap'?opacityImage(image):image);
  texture.name=file.name;
  texture.colorSpace=slot==='map'?THREE.SRGBColorSpace:THREE.NoColorSpace;
  texture.flipY=sampler?.flipY??!gltf;
  if(sampler){
   texture.channel=sampler.channel;texture.wrapS=sampler.wrapS;texture.wrapT=sampler.wrapT;
   texture.offset.copy(sampler.offset);texture.repeat.copy(sampler.repeat);texture.center.copy(sampler.center);
   texture.rotation=sampler.rotation;texture.matrixAutoUpdate=sampler.matrixAutoUpdate;texture.matrix.copy(sampler.matrix);
  }
  texture.needsUpdate=true;
  return texture;
 }finally{URL.revokeObjectURL(url);}
}

export function createMaterialPanel(sources,english,gltf,refresh,post,resolvedNames){
 const t=(ko,en)=>english?en:ko;
 const edits=new Map(),rows=new Map();
 const panel=document.createElement('section');
 panel.id='materials';panel.hidden=true;panel.setAttribute('aria-labelledby','material-heading');
 panel.innerHTML=`<header><strong id="material-heading">${t('재질','Materials')}</strong><button type="button" data-close aria-label="${t('닫기','Close')}">×</button></header>
  <label class="material-field">${t('재질 선택','Select material')}<select id="material-list"></select></label>
  <label class="material-check"><input id="material-opaque" type="checkbox">${t('불투명으로 보기','Preview as opaque')}</label>
  <fieldset><legend>${t('텍스처 다시 연결','Relink textures')}</legend></fieldset>
  <label class="material-field" for="material-cutoff">${t('투명도 경계값','Alpha cutoff')} <output id="material-value"></output></label>
  <input id="material-cutoff" type="range" min="0" max="1" step="0.01">
  <p data-notice role="status"></p><p data-error role="alert"></p>
  <button id="material-reset" type="button">${t('원래 재질로 복원','Restore original material')}</button>`;
 document.body.append(panel);
 const get=selector=>panel.querySelector(selector);
 const list=get('#material-list'),opaque=get('#material-opaque'),cutoff=get('#material-cutoff'),reset=get('#material-reset');
 const counters=new Map();
 for(const [i,source] of sources.entries()){
  const name=source.name.trim()||t('재질 ','Material ')+(i+1);
  const count=(counters.get(name)??0)+1;counters.set(name,count);
  list.add(new Option(count===1?name:name+' ('+count+')',String(i)));
 }
 let selected=sources[0];
 function edit(source){
  if(!edits.has(source))edits.set(source,{opaque:false,cutoff:source.alphaTest,slots:{},pending:{},variants:new Map(),error:''});
  return edits.get(source);
 }
 function sync(){
  const state=edits.get(selected);
  opaque.checked=state?.opaque??false;
  cutoff.value=state?.cutoff??selected.alphaTest;cutoff.disabled=opaque.checked;
  get('#material-value').textContent=Number(cutoff.value).toFixed(2);
  reset.disabled=!state;
  get('[data-error]').textContent=state?.error??'';
  for(const [slot,row] of rows){
   const image=selected[slot];
   let name=textureFile(image,resolvedNames,t('내장 이미지','Embedded image'));
   if(!name&&slot==='alphaMap'&&selected.map&&(selected.transparent||selected.alphaTest>0))name=t('색상 맵의 알파','Color map alpha');
   row.textContent=state?.pending[slot]?t('여는 중…','Loading…'):state?.slots[slot]?.name||name||t('없음','None');
   row.title=row.textContent;
  }
 }
 for(const [slot,title] of [['map',t('색상 맵','Color map')],['alphaMap',t('투명도 맵','Opacity map')]]){
  const row=document.createElement('div');row.className='material-slot';row.dataset.slot=slot;
  const label=document.createElement('span'),name=document.createElement('span'),button=document.createElement('button'),file=document.createElement('input');
  label.textContent=title;name.className='material-filename';button.type='button';button.textContent=t('교체…','Replace…');
  button.setAttribute('aria-label',title+' '+t('교체','replacement'));
  file.type='file';file.accept='image/png,image/jpeg,image/webp,image/bmp,image/gif,image/avif';file.hidden=true;
  button.onclick=()=>file.click();
  file.onchange=async()=>{
   const chosen=file.files[0];file.value='';if(!chosen)return;
   const source=selected,state=edit(source),ticket=Symbol();
   state.pending[slot]=ticket;state.error='';sync();
   try{
    const texture=await loadImage(chosen,slot,source[slot]??source.map,gltf);
    if(edits.get(source)!==state||state.pending[slot]!==ticket){texture.dispose();return;}
    const old=state.slots[slot];state.slots[slot]=texture;refresh();old?.dispose();
   }catch{
    if(edits.get(source)===state&&state.pending[slot]===ticket)state.error=t('이미지를 열 수 없습니다. ','Could not load image. ')+chosen.name;
   }finally{
    if(state.pending[slot]===ticket)delete state.pending[slot];
    if(source===selected&&panel.isConnected)sync();
   }
  };
  row.append(label,name,button,file);get('fieldset').append(row);rows.set(slot,name);
 }
 function select(source){const i=sources.indexOf(source);if(i<0)return;selected=source;list.selectedIndex=i;sync();}
 list.onchange=()=>select(sources[list.selectedIndex]);
 opaque.onchange=()=>{edit(selected).opaque=opaque.checked;sync();refresh();};
 cutoff.oninput=()=>{edit(selected).cutoff=Number(cutoff.value);sync();refresh();};
 function release(state){Object.values(state.slots).forEach(tex=>tex.dispose());state.variants.forEach(m=>m.dispose());}
 reset.onclick=()=>{
  const state=edits.get(selected);if(!state)return;
  edits.delete(selected);refresh();release(state);sync();
 };
 function close(){if(!panel.hidden){panel.hidden=true;post('materialPanel:off');}}
 get('[data-close]').onclick=close;
 panel.onkeydown=e=>{e.stopPropagation();if(e.key==='Escape'){e.preventDefault();close();}};
 function preview(source,base){
  const state=edits.get(source);
  if(!state)return base;
  let m=state.variants.get(base);
  if(!m){m=base.clone();state.variants.set(base,m);}else m.copy(base);
  m.onBeforeCompile=base.onBeforeCompile;m.customProgramCacheKey=()=>base.customProgramCacheKey();
  if(state.slots.map)m.map=state.slots.map;
  if(state.slots.alphaMap){
   m.alphaMap=state.slots.alphaMap;m.transparent=true;
   // The new opacity image replaces alpha from the color texture as well.
   m.onBeforeCompile=function(shader,renderer){
    base.onBeforeCompile.call(this,shader,renderer);
    shader.fragmentShader=shader.fragmentShader.replace('#include <map_fragment>',
     'float originalOpacity = diffuseColor.a;\n#include <map_fragment>\ndiffuseColor.a = originalOpacity;');
   };
   m.customProgramCacheKey=()=>base.customProgramCacheKey()+'|relinked-alpha';
  }
  m.alphaTest=state.cutoff;
  if(state.opaque){
   m.alphaMap=null;m.opacity=1;m.transparent=false;m.alphaTest=0;m.alphaHash=false;
   m.alphaToCoverage=false;m.depthWrite=true;m.blending=THREE.NormalBlending;
   if('transmission' in m)m.transmission=0;
   if(m.map&&!m.map.image){m.map=null;m.color?.setHex(0xb4bdc9);}
  }
  m.needsUpdate=true;
  return m;
 }
 sync();
 return {
  preview,select,close,get isOpen(){return !panel.hidden;},
  toggle(){if(panel.hidden){sync();panel.hidden=false;post('materialPanel:on');list.focus();}else close();},
  setTextures(on){get('[data-notice]').textContent=on?'':t('텍스처를 켜면 재질 변경 사항이 보입니다.','Enable textures to see material edits.');},
  sync,dispose(){edits.forEach(release);edits.clear();panel.remove();}
 };
}
