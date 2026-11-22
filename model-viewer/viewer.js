import {TransformControls} from 'three/addons/controls/TransformControls.js';


import {createEffects} from './effects.js';
import {createEnvironment} from './environment.js';
import {createPlayback} from './animation.js';
import {createMaterialPanel} from './materials.js';

import * as THREE from 'three';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {OBJLoader} from 'three/addons/loaders/OBJLoader.js';
import {MTLLoader} from 'three/addons/loaders/MTLLoader.js';
import {STLLoader} from 'three/addons/loaders/STLLoader.js';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
import {DRACOLoader} from 'three/addons/loaders/DRACOLoader.js';
import {KTX2Loader} from 'three/addons/loaders/KTX2Loader.js';
import {MeshoptDecoder} from 'three/addons/libs/meshopt_decoder.module.js';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';
const post=text=>window.chrome.webview.postMessage(text);
const message=document.getElementById('message');
let renderer,scene,camera,controls,model,grid,axes,radius=1,wire=false,textures=true,originals=new Map(),plain=new Map(),ready=false,drawPending=false;
const failed=new Set();
const resolvedTextures=new Map();
window.chrome.webview.addEventListener('message',({data})=>{
 if(typeof data?.textureReference==='string'&&typeof data.textureFile==='string'){
  resolvedTextures.set(data.textureReference,data.textureFile);
  if(materialPanel?.isOpen)materialPanel.sync();
 }
});
const fbxTexturePrefix='dollview-fbx-texture:';
let englishMessages=false;
function showMissingFiles(){
 if(!ready||!failed.size)return;
 const names=[...failed].map(url=>{
  let path=url;
  try{
   const parsed=new URL(url,location.href);
   if(parsed.protocol==='data:'||parsed.protocol==='blob:')return englishMessages?'embedded image':'내장 이미지';
   path=decodeURIComponent(parsed.hostname==='textures.lumaview.invalid'?parsed.search.slice(1):parsed.pathname);
  }catch{}
  return path.split(/[\\/]/).pop();
 });
 message.textContent=(englishMessages?'Missing original material or texture files: ':'원본에서 찾을 수 없는 재질·텍스처 파일: ')+[...new Set(names)].join(', ');
}
let flat=false,lightMode=false,detailsVisible=false;
const flatMaterials=new Map(),userLights=[];
const raycaster=new THREE.Raycaster();
let effects,environment,playback,materialPanel;
let overlayScene,transform,ssao=false,selectedLight=-1,aoStrength=1;
let skyDome=false,skyStrength=1,studioEnvironment,ambient,keyLight;
const skyMaterials=new Map(),skyLightingStrength={value:1};
function error(e){message.textContent=String(e?.message||e);post('error:'+message.textContent);}
window.addEventListener('error',e=>error(e.error||e.message));
window.addEventListener('unhandledrejection',e=>error(e.reason));
let viewportWidth=0,viewportHeight=0;
function render(now){
 drawPending=false;
 if(!renderer||renderer.getContext().isContextLost())return;
 const width=innerWidth,height=innerHeight;
 if(width<2||height<2)return;
 // Keep post-processing buffers bounded on large or high-DPI screens.
 const ratio=Math.min(devicePixelRatio,2,Math.sqrt(4194304/(width*height)));
 if(width!==viewportWidth||height!==viewportHeight||ratio!==renderer.getPixelRatio()){
  viewportWidth=width;viewportHeight=height;
  camera.aspect=width/height;camera.updateProjectionMatrix();
  if(ratio!==renderer.getPixelRatio())renderer.setPixelRatio(ratio);
  renderer.setSize(width,height);
  resizeEffects();
 }
 playback?.update(now);controls.update();renderScene();
}
function updateCameraDepth(){
 if(!model)return;
 // Tight, view-dependent depth range instead of a million-to-one fixed ratio.
 const direction=camera.getWorldDirection(new THREE.Vector3());
 const centerDepth=camera.position.clone().negate().dot(direction);
 const near=Math.max(radius*.0001,(centerDepth-radius*1.1)*.75);
 const far=Math.max(near+radius*2,centerDepth+radius*4);
 if(Math.abs(camera.near-near)>radius*1e-7||Math.abs(camera.far-far)>radius*1e-7){camera.near=near;camera.far=far;camera.updateProjectionMatrix();}
 const projectedRadius=radius*innerHeight/(2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2))*Math.max(radius*.01,centerDepth));
 aoStrength=THREE.MathUtils.smoothstep(projectedRadius,16,70);
}
function renderScene(){
 updateCameraDepth();
 renderer.setRenderTarget(null);
 renderer.setViewport(0,0,viewportWidth,viewportHeight);
 renderer.setScissorTest(false);
 effects.render(ssao,flat,aoStrength);
 renderer.setRenderTarget(null);const autoClear=renderer.autoClear;renderer.autoClear=false;renderer.clearDepth();const near=camera.near,far=camera.far;camera.near=Math.min(near,radius*.0001);camera.far=Math.max(far,camera.position.length()+radius+Math.max(0,...userLights.filter(x=>x.active).map(x=>x.light.position.length())));camera.updateProjectionMatrix();
 renderer.render(overlayScene,camera);camera.near=near;camera.far=far;camera.updateProjectionMatrix();renderer.autoClear=autoClear;
}
function resizeEffects(){effects?.resize();}
function draw(){if(!drawPending){drawPending=true;requestAnimationFrame(render);}}
function resize(){draw();}
function fit(axis='iso'){
 if(!model)return;
 const distance=radius/Math.sin(THREE.MathUtils.degToRad(camera.fov/2))/Math.min(1,camera.aspect)*1.12;
 const vector=axis==='front'?new THREE.Vector3(0,0,1):axis==='side'?new THREE.Vector3(1,0,0):axis==='top'?new THREE.Vector3(0,1,.0001):new THREE.Vector3(1,.65,1);
 camera.up.set(0,1,0);camera.position.copy(vector.normalize().multiplyScalar(distance));controls.target.set(0,0,0);controls.minDistance=radius*.01;controls.maxDistance=distance*30;controls.update();draw();
}
function skyMaterial(source){
 const legacy=source.isMeshPhongMaterial||source.isMeshLambertMaterial;
 if(!legacy&&!(source.isMeshStandardMaterial&&source.envMap))return source;
 let material=skyMaterials.get(source);
 if(!material){
  material=source.clone();
  if(legacy){
   // FBX and MTL materials need explicit image-based lighting. Use the same
   // filtered HDR and rotation as PBR without replacing authored material maps.
   material.onBeforeCompile=(shader,renderer)=>{
    source.onBeforeCompile(shader,renderer);
    shader.uniforms.domeStrength=skyLightingStrength;
    shader.fragmentShader=shader.fragmentShader
     .replace('#include <common>',`#include <common>
      #include <cube_uv_reflection_fragment>
      uniform float domeStrength;`)
     .replace('#include <lights_fragment_end>',`
      vec3 domeNormal = inverseTransformDirection(geometryNormal, viewMatrix);
      irradiance += PI * textureCubeUV(envMap, envMapRotation * domeNormal, 1.0).rgb * domeStrength;
      #include <lights_fragment_end>`)
     .replace('#include <envmap_fragment>',source.isMeshPhongMaterial?`
      vec3 reflected = inverseTransformDirection(reflect(-geometryViewDir, geometryNormal), viewMatrix);
      vec3 domeReflection = textureCubeUV(envMap, envMapRotation * reflected, 0.0).rgb;
      outgoingLight += domeReflection * specular * specularStrength * reflectivity * domeStrength;`:'');
   };
   material.customProgramCacheKey=()=>source.customProgramCacheKey()+'|dollview-dome-v2';
  }else{
   // Explicit PBR environment maps otherwise override the dome and its strength.
   material.envMap=null;
  }
  skyMaterials.set(source,material);
 }
 if(legacy){
  if(material.envMap!==scene.environment){material.envMap=scene.environment;material.needsUpdate=true;}
  material.envMapRotation.copy(scene.environmentRotation);
 }
 return material;
}
function applySky(){
 if(!ambient)return;
 environment.configure({enabled:skyDome,intensity:skyStrength});
 const active=environment.apply(studioEnvironment.texture);
 skyLightingStrength.value=skyStrength;
 effects?.updateShadowLight();
 ambient.intensity=active ? .25*skyStrength : (effects?.settings.shadows ? .6 : 2);
 ambient.color.set(0xffffff);ambient.groundColor.set(0x505768);
 keyLight.visible=!active&&!effects?.settings.shadows;if(model)materials();else draw();
}
window.setEnvironment=value=>environment.set(value);
window.selectEnvironment=id=>environment.select(id);
window.importEnvironment=file=>environment.importFile(file);

window.setSkyStrength=value=>{if(!Number.isFinite(value))return;skyStrength=Math.max(0,Math.min(10,value));applySky();};
function viewportMaterial(source){
 if(!flat&&effects?.settings.toon)return effects.material(source,textures);
 let base=source;
 if(!textures){
  if(!plain.has(source))plain.set(source,new THREE.MeshStandardMaterial({color:0xb4bdc9,roughness:.65,metalness:0,side:source.side}));
  base=plain.get(source);
 }
 if(flat){
  if(!flatMaterials.has(base))flatMaterials.set(base,new THREE.MeshBasicMaterial({
   color:base.color??0xb4bdc9,map:base.map,vertexColors:base.vertexColors,alphaMap:base.alphaMap,
   opacity:base.opacity,transparent:base.transparent,alphaTest:base.alphaTest,side:base.side,depthWrite:base.depthWrite,toneMapped:false
  }));
  return flatMaterials.get(base);
 }
 return textures&&skyDome&&environment.texture?skyMaterial(base):base;
}
function materials(){
 const active=new Map();
 function resolve(source){
  if(!active.has(source)){
   const base=viewportMaterial(source);
   const material=textures&&materialPanel?materialPanel.preview(source,base):base;
   material.wireframe=wire;
   active.set(source,material);
  }
  return active.get(source);
 }
 for(const [mesh,source] of originals)mesh.material=Array.isArray(source)?source.map(resolve):resolve(source);
 materialPanel?.setTextures(textures);
 draw();
}
function capture(){
 if(renderer.getContext().isContextLost()||!viewportWidth)return;
 renderScene();const c=document.createElement('canvas');c.width=180;c.height=120;const g=c.getContext('2d');g.fillStyle='#202328';g.fillRect(0,0,180,120);const ratio=Math.min(180/renderer.domElement.width,120/renderer.domElement.height);const w=renderer.domElement.width*ratio,h=renderer.domElement.height*ratio;g.drawImage(renderer.domElement,(180-w)/2,(120-h)/2,w,h);post('thumbnail:'+c.toDataURL('image/png').split(',')[1]);
}
window.setRenderBase=value=>{ssao=value.ssao;skyDome=value.sky;flat=value.flat;if(typeof value.textures==='boolean')textures=value.textures;applySky();post('renderBase:'+JSON.stringify({ssao,sky:skyDome,flat,textures}));};
window.setRenderEffects=value=>effects.apply(value);
window.setRenderPreset=name=>effects.preset(name);
window.toggleRenderEffects=en=>{materialPanel?.close();effects.toggle(en);};
window.modelCommand=action=>{
 if(!ready)return;
 if(action==='materials'){
  effects.close();
  if(lightMode)setLightMode(false);
  if(selectedLight>=0)selectLight(-1);
  materialPanel.toggle();
 }
 if(['iso','front','side','top'].includes(action))fit(action);
 if(action==='sky'){skyDome=!skyDome;applySky();}
 if(action==='ssao'){ssao=!ssao;draw();}
 if(action==='editLight')editSelectedLight();
 if(action==='clearSelection')selectLight(-1);
 if(action==='flat'){flat=!flat;materials();}
 if(action==='light')setLightMode(!lightMode);
 if(action==='cancelLight')setLightMode(false);
 if(action==='wire'){wire=!wire;materials();}
 if(action==='textures'){textures=!textures;materials();}
 if(action==='grid'){grid.visible=!grid.visible;axes.visible=grid.visible;draw();}
};
window.modelState=()=>({environment:environment?.state(),effects:{...effects?.settings},ready,wire,textures,flat,ssao,ssaoActive:ssao&&!flat&&aoStrength>0,aoStrength,clipNear:camera?.near,clipFar:camera?.far,skyDome,skyStrength,studioDirectionalVisible:keyLight?.visible,selectedLight,gizmoVisible:!!transform?.object,gizmoAxis:transform?.axis,dragging:transform?.dragging,lightMode,detailsVisible,lights:userLights.filter(x=>x.active).map(x=>({id:x.id,color:"#"+x.light.color.getHexString(),strength:x.strength,position:x.light.position.toArray(),screen:x.handle.position.clone().project(camera).toArray()})),grid:grid?.visible,meshes:window.modelStats?.meshes,triangles:window.modelStats?.triangles,camera:camera?.position.toArray(),target:controls?.target.toArray()});
let shortcuts={
 details:{ctrl:false,alt:false,shift:false,key:'Tab',code:'Tab'},
 fit:{ctrl:false,alt:false,shift:false,key:'f',code:'KeyF'},
 fullscreen:{ctrl:false,alt:true,shift:false,key:'Enter',code:'Enter'}
};
window.setShortcuts=map=>{if(map&&typeof map==='object')shortcuts={...shortcuts,...map};};
function matchShortcut(spec,e){
 if(!spec)return false;
 if(!!e.ctrlKey!==!!spec.ctrl||!!e.altKey!==!!spec.alt||!!e.shiftKey!==!!spec.shift)return false;
 if(spec.code&&e.code===spec.code)return true;
 if(spec.key&&e.key&&e.key.toLowerCase()===spec.key.toLowerCase())return true;
 return false;
}
window.addEventListener('keydown',e=>{
 if(matchShortcut(shortcuts.fullscreen,e)){
  e.preventDefault();
  e.stopPropagation();
  if(!e.repeat)post('toggleFullscreen');
 }
},true);
window.addEventListener('keydown',e=>{if(e.target.closest?.('#effects,#materials'))return;if(e.key==='Escape'){e.preventDefault();if(materialPanel?.isOpen)materialPanel.close();else if(lightMode)setLightMode(false);else if(selectedLight>=0)selectLight(-1);else post('exit');}else if(matchShortcut(shortcuts.details,e)){e.preventDefault();if(!e.repeat)post('toggleDetails');}else if(matchShortcut(shortcuts.fit,e)){e.preventDefault();fit();}else if(e.key.toLowerCase()==='l'&&!e.repeat&&!e.ctrlKey&&!e.altKey){e.preventDefault();setLightMode(!lightMode);}});
window.addEventListener('resize',resize);
function setLightMode(enabled){
 if(!ready)return;if(enabled){materialPanel?.close();selectLight(-1);}lightMode=enabled;renderer.domElement.style.cursor=enabled?'crosshair':'';post('lightMode:'+(enabled?'on':'off'));
}
function setRay(x,y){const rect=renderer.domElement.getBoundingClientRect();camera.updateMatrixWorld();scene.updateMatrixWorld(true);overlayScene.updateMatrixWorld(true);raycaster.setFromCamera(new THREE.Vector2((x-rect.left)/rect.width*2-1,-(y-rect.top)/rect.height*2+1),camera);}
function lightHit(x,y){setRay(x,y);return raycaster.intersectObjects(userLights.filter(l=>l.active).map(l=>l.handle),false)[0]?.object.userData.lightId;}
function addLight(x,y){
 setRay(x,y);if(raycaster.intersectObject(model,true).length)return false;
 const normal=camera.getWorldDirection(new THREE.Vector3());
 const plane=new THREE.Plane().setFromNormalAndCoplanarPoint(normal,controls.target.clone().addScaledVector(normal,-radius*.8));
 const point=raycaster.ray.intersectPlane(plane,new THREE.Vector3());if(!point)return false;
 const light=new THREE.PointLight(0xffffff,radius*radius*6,0,2);light.position.copy(point);scene.add(light);
 const icon=document.createElement('canvas');icon.width=64;icon.height=64;const g=icon.getContext('2d');g.strokeStyle='#ffffff';g.lineWidth=3;g.fillStyle='#202328';g.beginPath();g.arc(32,32,13,0,Math.PI*2);g.fill();g.stroke();for(let n=0;n<8;n++){const a=n*Math.PI/4;g.beginPath();g.moveTo(32+20*Math.cos(a),32+20*Math.sin(a));g.lineTo(32+28*Math.cos(a),32+28*Math.sin(a));g.stroke();}
 const texture=new THREE.CanvasTexture(icon);const handle=new THREE.Sprite(new THREE.SpriteMaterial({map:texture,depthTest:false,depthWrite:false,toneMapped:false}));handle.position.copy(point);handle.renderOrder=100;handle.scale.setScalar(radius*.2);const id=userLights.length;handle.userData.lightId=id;overlayScene.add(handle);userLights.push({id,light,handle,strength:1,active:true});setLightMode(false);selectLight(id);draw();return true;
}
window.updateModelLight=(id,color,strength)=>{
 const item=userLights[id];if(!item?.active||!/^#[0-9a-f]{6}$/i.test(color)||!Number.isFinite(strength))return;
 item.strength=Math.max(0,Math.min(100,strength));item.light.color.set(color);item.light.intensity=radius*radius*6*item.strength;item.handle.material.color.set(color);draw();
};
window.removeModelLight=id=>{const item=userLights[id];if(!item?.active)return;if(selectedLight===id)selectLight(-1);scene.remove(item.light);overlayScene.remove(item.handle);item.handle.material.map.dispose();item.handle.material.dispose();item.active=false;draw();};
window.setModelDetails=(text,topPixels,visible,playbackInset=0)=>{
 const panel=document.getElementById('details');panel.textContent=text;panel.style.top=Math.ceil(topPixels/devicePixelRatio)+'px';detailsVisible=visible;panel.hidden=!visible;
 document.documentElement.style.setProperty('--playback-inset',Math.ceil(playbackInset/devicePixelRatio)+'px');
 document.documentElement.style.setProperty('--viewer-top',Math.ceil(topPixels/devicePixelRatio)+'px');
};
function selectLight(id){
 if(transform.dragging){transform.reset();transform.pointerUp({button:0});}
 transform.detach();transform.enabled=false;selectedLight=-1;controls.enabled=true;
 if(userLights[id]?.active){selectedLight=id;transform.enabled=true;transform.attach(userLights[id].light);}
 post('lightSelected:'+selectedLight);draw();
}
function editSelectedLight(){const item=userLights[selectedLight];if(item?.active)post('editLight:'+JSON.stringify({id:item.id,color:'#'+item.light.color.getHexString(),strength:item.strength}));}
function setupLightPicking(){
 let gesture=null,blank=null;
 renderer.domElement.addEventListener('pointerdown',e=>{
  if(!ready||e.button!==0)return;
  setRay(e.clientX,e.clientY);
  if(selectedLight>=0&&!lightMode){const rect=renderer.domElement.getBoundingClientRect();transform.pointerHover({x:(e.clientX-rect.left)/rect.width*2-1,y:-(e.clientY-rect.top)/rect.height*2+1,button:0});if(transform.axis!==null)return;}
  const id=lightHit(e.clientX,e.clientY);
  if(id===undefined&&!lightMode){blank={x:e.clientX,y:e.clientY};return;}
  gesture={id,x:e.clientX,y:e.clientY};e.preventDefault();e.stopImmediatePropagation();try{renderer.domElement.setPointerCapture(e.pointerId);}catch{}
 },true);
 renderer.domElement.addEventListener('pointerup',e=>{
  if(!gesture){
   if(blank&&!e.shiftKey&&Math.hypot(e.clientX-blank.x,e.clientY-blank.y)<5){
    selectLight(-1);
    if(materialPanel?.isOpen){
     setRay(e.clientX,e.clientY);
     const hit=raycaster.intersectObject(model,true).find(hit=>originals.has(hit.object));
     if(hit){const source=originals.get(hit.object);materialPanel.select(Array.isArray(source)?source[hit.face.materialIndex]:source);}
    }
   }
   blank=null;return;
  }
  const hit=gesture;gesture=null;e.preventDefault();e.stopImmediatePropagation();if(renderer.domElement.hasPointerCapture(e.pointerId))renderer.domElement.releasePointerCapture(e.pointerId);
  if(Math.hypot(e.clientX-hit.x,e.clientY-hit.y)>5)return;
  if(hit.id!==undefined){setLightMode(false);selectLight(hit.id);}
  else if(lightMode)addLight(e.clientX,e.clientY);
 },true);
 renderer.domElement.addEventListener('dblclick',e=>{if(!ready)return;const id=lightHit(e.clientX,e.clientY);if(id!==undefined){e.preventDefault();setLightMode(false);selectLight(id);editSelectedLight();}});
 renderer.domElement.addEventListener('pointercancel',()=>{gesture=null;blank=null;if(transform.dragging)transform.pointerUp({button:0});controls.enabled=true;});
}const manager=new THREE.LoadingManager();
manager.setURLModifier(url=>{
 if(url.startsWith(fbxTexturePrefix))return 'https://textures.lumaview.invalid/?'+encodeURIComponent(url.slice(fbxTexturePrefix.length));
 const resolved=new URL(url,location.href);
 if(!['https://model.lumaview.invalid','https://viewer.lumaview.invalid'].includes(resolved.origin)&&!['data:','blob:'].includes(resolved.protocol))throw new Error('Only local model resources are supported.');
 return url;
});
manager.onError=url=>{failed.add(url);showMissingFiles();};
manager.onLoad=()=>{if(ready){draw();setTimeout(capture,100);}};
try{
 renderer=new THREE.WebGLRenderer({antialias:true,alpha:false});renderer.setPixelRatio(Math.min(devicePixelRatio,2));renderer.setSize(innerWidth,innerHeight);renderer.setClearColor(0x202328);renderer.toneMapping=THREE.ACESFilmicToneMapping;document.body.appendChild(renderer.domElement);
 renderer.domElement.addEventListener('webglcontextrestored',()=>{viewportWidth=0;playback?.resetClock();draw();});
 scene=new THREE.Scene();scene.background=new THREE.Color(0x202328);overlayScene=new THREE.Scene();camera=new THREE.PerspectiveCamera(40,innerWidth/innerHeight,.01,10000);
 environment=createEnvironment(renderer,scene,()=>{skyDome=environment.settings.enabled;skyStrength=environment.settings.intensity;applySky();},value=>{controls.enabled=!value&&!transform?.dragging;});
 transform=new TransformControls(camera,renderer.domElement);transform.setMode('translate');transform.setSpace('world');transform.setSize(.85);transform.enabled=false;overlayScene.add(transform.getHelper());
 transform.addEventListener('dragging-changed',e=>{controls.enabled=!e.value;});
 transform.addEventListener('objectChange',()=>{const item=userLights[selectedLight];if(item?.active)item.handle.position.copy(item.light.position);draw();});transform.addEventListener('change',draw);
 controls=new OrbitControls(camera,renderer.domElement);controls.addEventListener('change',draw);controls.enableDamping=false;setupLightPicking();
 const pmrem=new THREE.PMREMGenerator(renderer);const room=new RoomEnvironment();studioEnvironment=pmrem.fromScene(room);scene.environment=studioEnvironment.texture;room.dispose();pmrem.dispose();ambient=new THREE.HemisphereLight(0xffffff,0x505768,2);scene.add(ambient);keyLight=new THREE.DirectionalLight(0xffffff,2.5);keyLight.position.set(4,8,5);scene.add(keyLight);
 effects=createEffects(renderer,scene,camera,()=>{if(model)applySky();else draw();},environment);
 window.addEventListener('pagehide',()=>{playback?.dispose();materialPanel?.dispose();effects.dispose();environment.dispose();skyMaterials.forEach(m=>m.dispose());flatMaterials.forEach(m=>m.dispose());plain.forEach(m=>m.dispose());studioEnvironment.dispose();},{once:true});
 window.loadModel=async(url,extension,english=false)=>{
  try{
   englishMessages=english;
   if(model)throw new Error('Open a new viewer to load another model.');
   let clips=[];
   if(extension==='glb'||extension==='gltf'){
    const draco=new DRACOLoader(manager).setDecoderPath('./vendor/addons/libs/draco/gltf/');draco.setWorkerLimit(2);
    const ktx=new KTX2Loader(manager).setTranscoderPath('./vendor/addons/libs/basis/').detectSupport(renderer);
    const loader=new GLTFLoader(manager).setDRACOLoader(draco).setKTX2Loader(ktx).setMeshoptDecoder(MeshoptDecoder);
    try{const gltf=await loader.loadAsync(url);model=gltf.scene;clips=gltf.animations;}finally{draco.dispose();ktx.dispose();}
   }else if(extension==='obj'){
    const response=await fetch(url);if(!response.ok)throw new Error('Model file could not be read.');const text=await response.text();const obj=new OBJLoader(manager);const refs=[...text.matchAll(/^\s*mtllib\s+(.+)$/gm)].map(m=>m[1].trim());
    // MTL paths are relative to the OBJ; textures are relative to their MTL file.
    let creator=null;
    for(const ref of refs){try{const mtlUrl=new URL(ref.replace(/\\/g,'/'),url).href;const mtl=await new MTLLoader(manager).loadAsync(mtlUrl);mtl.preload();if(!creator)creator=mtl;else{Object.assign(creator.materialsInfo,mtl.materialsInfo);Object.assign(creator.materials,mtl.materials);}}catch(e){failed.add(ref);}}
    if(creator)obj.setMaterials(creator);model=obj.parse(text);
   }else if(extension==='fbx'){
    model=await new FBXLoader(manager).setResourcePath(fbxTexturePrefix).loadAsync(url);
    clips=model.animations;
   }else if(extension==='stl'){
    const geometry=await new STLLoader(manager).loadAsync(url);model=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:0xb4bdc9,roughness:.65,vertexColors:!!geometry.attributes.color}));
   }else throw new Error('Unsupported 3D format.');
   let meshes=0,triangles=0;const mats=new Set();model.updateMatrixWorld(true);model.traverse(o=>{if(!o.isMesh)return;meshes++;const geometry=o.geometry;if(!geometry.attributes.normal)geometry.computeVertexNormals();triangles+=Math.floor((geometry.index?geometry.index.count:geometry.attributes.position.count)/3)*(o.isInstancedMesh?o.count:1);originals.set(o,o.material);(Array.isArray(o.material)?o.material:[o.material]).forEach(m=>mats.add(m));});
   if(!meshes)throw new Error('No mesh found in this file.');
   materialPanel=createMaterialPanel([...mats],english,extension==='gltf'||extension==='glb',materials,post,resolvedTextures);
   playback=createPlayback(model,clips,english,draw);
   const box=new THREE.Box3().setFromObject(model,true);const size=box.getSize(new THREE.Vector3()),center=box.getCenter(new THREE.Vector3());radius=size.length()/2;
   if(!Number.isFinite(radius)||radius<=0)throw new Error('Model bounds are empty or invalid.');
   const wrapper=new THREE.Group();wrapper.add(model);wrapper.position.copy(center).negate();scene.add(wrapper);
   grid=new THREE.GridHelper(radius*3,20,0x697586,0x38414b);grid.position.y=-size.y/2-radius*.002;scene.add(grid);axes=new THREE.AxesHelper(radius*.6);axes.position.y=grid.position.y+.001*radius;scene.add(axes);
   scene.updateMatrixWorld(true);effects.attach(model,radius);
   ready=true;window.modelStats={meshes,triangles,materials:mats.size,size:size.toArray()};applySky();fit();message.textContent='';showMissingFiles();
   post('loaded:'+JSON.stringify(window.modelStats));setTimeout(capture,200);
  }catch(e){error(e);}
 };
 post('ready');
}catch(e){error(e);}







