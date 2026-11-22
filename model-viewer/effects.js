import * as THREE from 'three';
import {EffectComposer} from 'three/addons/postprocessing/EffectComposer.js';
import {RenderPass} from 'three/addons/postprocessing/RenderPass.js';
import {SSAOPass} from 'three/addons/postprocessing/SSAOPass.js';
import {BloomPass} from './bloom.js';
import {LineSegments2} from 'three/addons/lines/LineSegments2.js';
import {LineSegmentsGeometry} from 'three/addons/lines/LineSegmentsGeometry.js';
import {LineMaterial} from 'three/addons/lines/LineMaterial.js';
import {OutlinePass} from 'three/addons/postprocessing/OutlinePass.js';
import {OutputPass} from 'three/addons/postprocessing/OutputPass.js';
import {ShaderPass} from 'three/addons/postprocessing/ShaderPass.js';
import {FXAAShader} from 'three/addons/shaders/FXAAShader.js';

export function createEffects(renderer,scene,camera,changed,environment){
 const defaults={shadows:false,toon:false,outline:false,bloom:false,bloomStrength:.45,bloomThreshold:1,exposure:1,contrast:1,saturation:1,outlineWidth:1.5};
 const settings={...defaults},toonCache=new Map();
 let composer,base,ao,bloom,outline,grade,fxaa,ground,light,model,radius=1;
 const edgeScene=new THREE.Scene();
 const edgeMaterial=new LineMaterial({color:0x17191d,toneMapped:false,alphaToCoverage:false});
 const edgeGeometry=new Map(),edgeMeshes=[];
 const instanceMatrix=new THREE.Matrix4();
 function addHardEdges(){
  if(edgeMeshes.length||!model)return;
  model.traverse(mesh=>{
   if(!mesh.isMesh||mesh.isSkinnedMesh||mesh.morphTargetInfluences?.some(weight=>weight!==0))return;
   let geometry=edgeGeometry.get(mesh.geometry);
   if(!geometry){
    const edges=new THREE.EdgesGeometry(mesh.geometry,30);
    geometry=new LineSegmentsGeometry().fromEdgesGeometry(edges);
    edges.dispose();
    edgeGeometry.set(mesh.geometry,geometry);
   }
   if(!geometry.instanceCount)return;
   const count=mesh.isInstancedMesh?mesh.count:1;
   for(let index=0;index<count;index++){
    const line=new LineSegments2(geometry,edgeMaterial);
    line.matrixAutoUpdate=false;
    edgeScene.add(line);
    edgeMeshes.push({mesh,line,index});
   }
  });
 }
 function renderHardEdges(){
  addHardEdges();
  edgeMaterial.linewidth=settings.outlineWidth*renderer.getPixelRatio();
  for(const {mesh,line,index} of edgeMeshes){
   line.visible=true;
   for(let parent=mesh;parent;parent=parent.parent)if(!parent.visible){line.visible=false;break;}
   line.matrix.copy(mesh.matrixWorld);
   if(mesh.isInstancedMesh){mesh.getMatrixAt(index,instanceMatrix);line.matrix.multiply(instanceMatrix);}
   line.matrixWorldNeedsUpdate=true;
  }
  const autoClear=renderer.autoClear;
  renderer.autoClear=false;
  try{renderer.render(edgeScene,camera);}finally{renderer.autoClear=autoClear;}
 }
 const gradient=new THREE.DataTexture(new Uint8Array([45,45,45,255,120,120,120,255,200,200,200,255,255,255,255,255]),4,1,THREE.RGBAFormat);
 gradient.minFilter=gradient.magFilter=THREE.NearestFilter;gradient.needsUpdate=true;
 function pipeline(){
  if(composer)return;
  composer=new EffectComposer(renderer);
  base=new RenderPass(scene,camera);
  const renderBase=base.render.bind(base);
  base.render=(...args)=>{
   renderBase(...args);
   if(model&&settings.outline)renderHardEdges();
  };
  composer.addPass(base);
  ao=new SSAOPass(scene,camera,innerWidth,innerHeight,24);
  ao.copyMaterial.uniforms.aoStrength={value:1};
  ao.copyMaterial.fragmentShader=ao.copyMaterial.fragmentShader.replace('uniform float opacity;','uniform float opacity; uniform float aoStrength;').replace('gl_FragColor = opacity * texel;','gl_FragColor = vec4(mix(vec3(1.0),texel.rgb,aoStrength),1.0);');
  composer.addPass(ao);
  bloom=new BloomPass();composer.addPass(bloom);
  outline=new OutlinePass(new THREE.Vector2(innerWidth,innerHeight),scene,camera);
  outline.visibleEdgeColor.set(0xffffff);outline.hiddenEdgeColor.set(0x000000);
  outline.overlayMaterial.blending=THREE.NormalBlending;
  outline.overlayMaterial.fragmentShader=outline.overlayMaterial.fragmentShader.replace('gl_FragColor = finalColor;','gl_FragColor = vec4(vec3(0.015), clamp(max(finalColor.r,max(finalColor.g,finalColor.b)),0.0,1.0));');
  composer.addPass(outline);composer.addPass(new OutputPass());
  grade=new ShaderPass({uniforms:{tDiffuse:{value:null},contrast:{value:1},saturation:{value:1}},vertexShader:'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',fragmentShader:'uniform sampler2D tDiffuse; uniform float contrast; uniform float saturation; varying vec2 vUv; void main(){vec4 c=texture2D(tDiffuse,vUv);float y=dot(c.rgb,vec3(.2126,.7152,.0722));c.rgb=mix(vec3(y),c.rgb,saturation);c.rgb=(c.rgb-.5)*contrast+.5;gl_FragColor=vec4(clamp(c.rgb,0.0,1.0),c.a);}'});
  composer.addPass(grade);fxaa=new ShaderPass(FXAAShader);composer.addPass(fxaa);resize();
 }
 function resize(){
  if(!composer)return;
  const p=renderer.getPixelRatio();
  composer.setSize(innerWidth,innerHeight);
  composer.setPixelRatio(p);
  fxaa.uniforms.resolution.value.set(1/(innerWidth*p),1/(innerHeight*p));
 }
 function attach(object,size){
  model=object;radius=size;const box=new THREE.Box3().setFromObject(model,true);
  ground=new THREE.Mesh(new THREE.PlaneGeometry(radius*12,radius*12),new THREE.ShadowMaterial({opacity:.32}));
  ground.rotation.x=-Math.PI/2;ground.position.y=box.min.y-radius*.003;ground.receiveShadow=true;ground.visible=false;scene.add(ground);
  light=new THREE.DirectionalLight();light.castShadow=true;
  light.shadow.mapSize.set(2048,2048);Object.assign(light.shadow.camera,{left:-radius*1.5,right:radius*1.5,top:radius*1.5,bottom:-radius*1.5,near:radius*.01,far:radius*10});
  light.shadow.camera.updateProjectionMatrix();light.shadow.normalBias=radius*.002;light.shadow.bias=-.0001;light.visible=false;scene.add(light);
  model.traverse(o=>{if(o.isMesh){o.castShadow=true;o.receiveShadow=true;}});
 }
 function updateShadowLight(){
  if(!light)return;
  const key=environment.shadowKey;
  if(key){
   light.position.copy(key.direction).applyEuler(scene.environmentRotation).multiplyScalar(radius*Math.sqrt(17));
   light.color.copy(key.color);
   light.intensity=key.intensity*scene.environmentIntensity;
  }else{
   light.position.set(2,3,2).multiplyScalar(radius);
   light.color.set(0xfff3df);light.intensity=2.5;
  }
  light.visible=settings.shadows&&light.intensity>0;
 }
 function material(source,textures){
  let pair=toonCache.get(source);if(!pair){pair=new Map();toonCache.set(source,pair);}
  if(!pair.has(textures))pair.set(textures,new THREE.MeshToonMaterial({
   color:textures?(source.color?.clone()||new THREE.Color(0xb4bdc9)):new THREE.Color(0xb4bdc9),
   map:textures?source.map:null,gradientMap:gradient,normalMap:textures?source.normalMap:null,
   alphaMap:textures?source.alphaMap:null,alphaTest:textures?source.alphaTest:0,
   opacity:textures?source.opacity:1,transparent:textures&&source.transparent,side:source.side,
   vertexColors:textures&&source.vertexColors,depthWrite:!textures||source.depthWrite,
   emissive:(textures&&source.emissive?.clone())||new THREE.Color(0),
   emissiveMap:textures?source.emissiveMap:null,emissiveIntensity:source.emissiveIntensity??1,
   polygonOffset:true,polygonOffsetFactor:1,polygonOffsetUnits:1
  }));
  return pair.get(textures);
 }
 function apply(value){
  for(const key of ['shadows','toon','outline','bloom'])if(typeof value[key]==='boolean')settings[key]=value[key];
  const ranges={bloomStrength:[0,3],bloomThreshold:[0,3],exposure:[.1,3],contrast:[.5,1.5],saturation:[0,2],outlineWidth:[1,4]};
  for(const [key,[min,max]] of Object.entries(ranges))if(Number.isFinite(value[key]))settings[key]=Math.max(min,Math.min(max,value[key]));
  renderer.toneMappingExposure=settings.exposure;renderer.shadowMap.enabled=settings.shadows;renderer.shadowMap.type=THREE.PCFSoftShadowMap;
  if(ground)ground.visible=settings.shadows;
  scene.traverse(o=>{if(o.isMesh){for(const m of Array.isArray(o.material)?o.material:[o.material])m.needsUpdate=true;}});
  sync();changed();
 }
 function render(ssao,flat,strength){
  const useAo=ssao&&!flat&&strength>0;
  const useBloom=settings.bloom;
  const useOutline=!!model&&settings.outline;
  if(!useAo&&!useBloom&&!useOutline&&settings.contrast===1&&settings.saturation===1){
   renderer.setRenderTarget(null);
   renderer.render(scene,camera);
   return;
  }
  pipeline();ao.enabled=useAo;
  if(ao.enabled){ao.kernelRadius=radius*.18;ao.minDistance=radius*.002/(camera.far-camera.near);ao.maxDistance=radius*.6/(camera.far-camera.near);ao.copyMaterial.uniforms.aoStrength.value=strength;
   const u=ao.ssaoMaterial.uniforms;u.cameraNear.value=camera.near;u.cameraFar.value=camera.far;u.cameraProjectionMatrix.value.copy(camera.projectionMatrix);u.cameraInverseProjectionMatrix.value.copy(camera.projectionMatrixInverse);}
  bloom.enabled=useBloom;bloom.strength=settings.bloomStrength;bloom.threshold=settings.bloomThreshold;
  outline.enabled=useOutline;outline.selectedObjects=model?[model]:[];outline.edgeThickness=settings.outlineWidth;outline.edgeStrength=3;
  grade.uniforms.contrast.value=settings.contrast;grade.uniforms.saturation.value=settings.saturation;
  composer.render();
 }
 const panel=document.createElement('section');panel.id='effects';panel.hidden=true;panel.setAttribute('aria-label','Rendering effects');document.body.appendChild(panel);
 let english=false;
 function ui(en=false){
  english=en;const t=(ko,en)=>english?en:ko;
  panel.innerHTML=`<header><strong>${t('GPU 렌더링 효과','GPU rendering effects')}</strong><button id="fx-close" aria-label="Close">×</button></header><p>WebGL · ${t('실시간 조명 / 재질 / 후처리','Real-time lighting / materials / post-processing')}</p><label>${t('프리셋','Preset')}<select id="fx-preset"><option value="custom">${t('직접 설정','Custom')}</option><option value="basic">${t('기본','Basic')}</option><option value="realistic">${t('사실적','Realistic')}</option><option value="toon">${t('툰 / 외곽선','Toon / outline')}</option><option value="toonUntextured">${t('라인 온리','Line Only')}</option><option value="bloom">${t('시네마틱 / 블룸','Cinematic / bloom')}</option></select></label>`;
  environment.mount(panel,en);
  for(const [key,ko,en] of [['shadows','부드러운 그림자 / 바닥','Soft shadows / ground'],['toon','툰 명암','Toon shading'],['outline','외곽선','Outline'],['bloom','블룸 / 빛 번짐','Bloom / glow']]){
   const label=document.createElement('label');label.textContent=t(ko,en);const input=document.createElement('input');input.type='checkbox';input.dataset.key=key;input.addEventListener('change',()=>apply({[key]:input.checked}));label.append(input);panel.append(label);
  }
  for(const [key,ko,en,min,max,step] of [['bloomStrength','블룸 강도','Bloom strength',0,3,.05],['bloomThreshold','발광 임계값','Bloom threshold',0,3,.05],['outlineWidth','외곽선 두께','Outline width',1,4,.1],['exposure','노출','Exposure',.1,3,.05],['contrast','대비','Contrast',.5,1.5,.05],['saturation','채도','Saturation',0,2,.05]]){
   const label=document.createElement('label');label.textContent=t(ko,en);const group=document.createElement('span'),input=document.createElement('input'),output=document.createElement('output');input.type='range';Object.assign(input,{min,max,step});input.dataset.key=key;input.addEventListener('input',()=>apply({[key]:Number(input.value)}));group.append(input,output);label.append(group);panel.append(label);
  }
  const note=document.createElement('p');note.textContent=t('SSAO는 상단 메뉴에서 조절합니다. 플랫 컬러에서는 툰 명암·조명이 적용되지 않습니다. 그림자는 전용 주광원 기준입니다.','SSAO is available in the toolbar. Flat color bypasses toon shading and lighting. Shadows use a dedicated key light.');panel.append(note);
  panel.querySelector('#fx-close').onclick=()=>{visibility(false);renderer.domElement.focus();};
  panel.querySelector('#fx-preset').onchange=e=>preset(e.target.value);
  panel.onkeydown=e=>{e.stopPropagation();if(e.key==='Escape'){visibility(false);renderer.domElement.focus();}};
  sync();
 }
 function sync(){for(const input of panel.querySelectorAll('[data-key]')){const value=settings[input.dataset.key];if(input.type==='checkbox')input.checked=value;else{input.value=value;input.nextElementSibling.textContent=Number(value).toFixed(2);}}const select=panel.querySelector('select');if(select)select.value='custom';}
 function preset(name){
  if(!['basic','realistic','toon','toonUntextured','bloom'].includes(name))return;
  const value={...defaults};if(name==='realistic')Object.assign(value,{shadows:true});
  if(name==='toon'||name==='toonUntextured')Object.assign(value,{toon:true,outline:true});
  if(name==='bloom')Object.assign(value,{shadows:true,bloom:true,bloomStrength:.65,bloomThreshold:.8,exposure:1.1});
  window.setRenderBase?.({ssao:name==='realistic'||name==='bloom',sky:name==='realistic'||name==='bloom',flat:false,textures:name!=='toonUntextured'});apply(value);panel.querySelector('select').value=name;
 }
 function visibility(open){panel.hidden=!open;window.chrome.webview.postMessage('renderPanel:'+(open?'on':'off'));}
 ui();
 return {settings,attach,material,apply,render,resize,preset,updateShadowLight,toggle(en){if(english!==en)ui(en);visibility(panel.hidden);},dispose(){composer?.passes.forEach(p=>p.dispose?.());composer?.dispose();gradient.dispose();toonCache.forEach(pair=>pair.forEach(m=>m.dispose()));ground?.geometry.dispose();ground?.material.dispose();light?.shadow.dispose();edgeGeometry.forEach(geometry=>geometry.dispose());edgeMaterial.dispose();}};
}
