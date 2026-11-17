import {TransformControls} from 'three/addons/controls/TransformControls.js';


import {SSAOPass} from 'three/addons/postprocessing/SSAOPass.js';

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
let flat=false,lightMode=false,detailsVisible=false;
const flatMaps=new Map(),flatColors=new Map(),userLights=[];
const raycaster=new THREE.Raycaster();
let overlayScene,transform,ssaoPass,ssao=false,selectedLight=-1,aoStrength=1;
let skyDome=false,skyStrength=1,skyEnvironment,skyReflectionTexture,studioEnvironment,ambient,keyLight;
const skyMaterials=new Map(),skyReflectionStrength={value:1};
function error(e){message.textContent=String(e?.message||e);post('error:'+message.textContent);}
window.addEventListener('error',e=>error(e.error||e.message));
window.addEventListener('unhandledrejection',e=>error(e.reason));
function render(){drawPending=false;if(!renderer)return;controls.update();renderScene();}
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
 renderer.render(scene,camera);
 if(ssao&&!flat&&ready&&aoStrength>0){
  if(!ssaoPass){ssaoPass=new SSAOPass(scene,camera,innerWidth,innerHeight,24);ssaoPass.renderToScreen=true;
   ssaoPass.copyMaterial.uniforms.aoStrength={value:1};
   ssaoPass.copyMaterial.fragmentShader=ssaoPass.copyMaterial.fragmentShader.replace("uniform float opacity;","uniform float opacity; uniform float aoStrength;").replace("gl_FragColor = opacity * texel;","gl_FragColor = vec4(mix(vec3(1.0),texel.rgb,aoStrength),1.0);");resizeEffects();}
  ssaoPass.copyMaterial.uniforms.aoStrength.value=aoStrength;ssaoPass.kernelRadius=radius*.18;ssaoPass.minDistance=radius*.002/(camera.far-camera.near);ssaoPass.maxDistance=radius*.6/(camera.far-camera.near);
  const u=ssaoPass.ssaoMaterial.uniforms;u.cameraNear.value=camera.near;u.cameraFar.value=camera.far;u.cameraProjectionMatrix.value.copy(camera.projectionMatrix);u.cameraInverseProjectionMatrix.value.copy(camera.projectionMatrixInverse);
  ssaoPass.render(renderer,null,null);
 }
 renderer.setRenderTarget(null);const autoClear=renderer.autoClear;renderer.autoClear=false;renderer.clearDepth();const near=camera.near,far=camera.far;camera.near=Math.min(near,radius*.0001);camera.far=Math.max(far,camera.position.length()+radius+Math.max(0,...userLights.filter(x=>x.active).map(x=>x.light.position.length())));camera.updateProjectionMatrix();
 renderer.render(overlayScene,camera);camera.near=near;camera.far=far;camera.updateProjectionMatrix();renderer.autoClear=autoClear;
}
function resizeEffects(){if(ssaoPass){ssaoPass.setSize(Math.max(1,Math.ceil(innerWidth)),Math.max(1,Math.ceil(innerHeight)));}}
function draw(){if(!drawPending){drawPending=true;requestAnimationFrame(render);}}
function resize(){camera.aspect=innerWidth/innerHeight;camera.updateProjectionMatrix();renderer.setSize(innerWidth,innerHeight);resizeEffects();draw();}
function fit(axis='iso'){
 if(!model)return;
 const distance=radius/Math.sin(THREE.MathUtils.degToRad(camera.fov/2))/Math.min(1,camera.aspect)*1.12;
 const vector=axis==='front'?new THREE.Vector3(0,0,1):axis==='side'?new THREE.Vector3(1,0,0):axis==='top'?new THREE.Vector3(0,1,.0001):new THREE.Vector3(1,.65,1);
 camera.up.set(0,1,0);camera.position.copy(vector.normalize().multiplyScalar(distance));controls.target.set(0,0,0);controls.minDistance=radius*.01;controls.maxDistance=distance*30;controls.update();draw();
}
function skyMaterial(source){
 // Phong (FBX/MTL) does not inherit scene.environment and cannot sample a PMREM
 // CubeUV map. Keep its original maps/shading and give it a compatible panorama.
 if(!source.isMeshPhongMaterial&&!(source.isMeshStandardMaterial&&source.envMap))return source;
 let material=skyMaterials.get(source);
 if(!material){
  material=source.clone();
  if(source.isMeshPhongMaterial){
   material.envMap=skyReflectionTexture;material.combine=THREE.AddOperation;
   material.onBeforeCompile=(shader,renderer)=>{
    source.onBeforeCompile(shader,renderer);
    shader.uniforms.lumaDomeStrength=skyReflectionStrength;
    shader.fragmentShader='uniform float lumaDomeStrength;\n'+shader.fragmentShader;
    // An additive reflection must respect specular color/map, including black
    // (matte) materials. Multiplying by diffuse would keep black metal black.
    const reflection=THREE.ShaderChunk.envmap_fragment.replace(
     'envColor.xyz * specularStrength * reflectivity',
     'envColor.xyz * specular * specularStrength * reflectivity * lumaDomeStrength');
    shader.fragmentShader=shader.fragmentShader.replace('#include <envmap_fragment>',reflection);
   };
   material.customProgramCacheKey=()=>source.customProgramCacheKey()+'|luma-dome-phong-v1';
  }else{
   // Explicit PBR environment maps otherwise override the dome and its strength.
   material.envMap=null;
  }
  skyMaterials.set(source,material);
 }
 return material;
}
function applySky(){
 if(!ambient)return;
 if(skyDome&&!skyEnvironment){
  const canvas=document.createElement('canvas');canvas.width=128;canvas.height=64;const context=canvas.getContext('2d');
  const gradient=context.createLinearGradient(0,0,0,64);gradient.addColorStop(0,'#edf5ff');gradient.addColorStop(.5,'#e9eef5');gradient.addColorStop(1,'#b9c5d3');context.fillStyle=gradient;context.fillRect(0,0,128,64);
  const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;texture.mapping=THREE.EquirectangularReflectionMapping;
  skyReflectionTexture=texture;
  const generator=new THREE.PMREMGenerator(renderer);skyEnvironment=generator.fromEquirectangular(texture);generator.dispose();
 }
 scene.environment=skyDome?skyEnvironment.texture:studioEnvironment.texture;
 scene.environmentIntensity=skyDome?skyStrength:1;
 skyReflectionStrength.value=skyStrength;
 ambient.intensity=skyDome?2*skyStrength:2;
 ambient.color.set(skyDome?0xedf5ff:0xffffff);ambient.groundColor.set(skyDome?0xb9c5d3:0x505768);
 keyLight.visible=!skyDome;if(model)materials();else draw();
}
window.setSkyStrength=value=>{if(!Number.isFinite(value))return;skyStrength=Math.max(0,Math.min(10,value));applySky();};
function materials(){
 model.traverse(o=>{if(!o.isMesh)return;const source=originals.get(o);const list=Array.isArray(source)?source:[source];
 const next=list.map(m=>{if(flat){const cache=textures?flatMaps:flatColors;let p=cache.get(m);if(!p){p=new THREE.MeshBasicMaterial({color:m.color?.clone()||new THREE.Color(0xb4bdc9),map:textures?m.map:null,vertexColors:m.vertexColors,alphaMap:m.alphaMap,opacity:m.opacity,transparent:m.transparent,alphaTest:m.alphaTest,side:m.side,depthWrite:m.depthWrite,toneMapped:false});cache.set(m,p);}p.wireframe=wire;return p;}if(textures){const p=skyDome?skyMaterial(m):m;p.wireframe=wire;return p;}let p=plain.get(m);if(!p){p=new THREE.MeshStandardMaterial({color:0xb4bdc9,roughness:.65,metalness:0,side:m.side});plain.set(m,p);}p.wireframe=wire;return p;});o.material=Array.isArray(source)?next:next[0];});draw();
}
function capture(){
 renderScene();const c=document.createElement('canvas');c.width=180;c.height=120;const g=c.getContext('2d');g.fillStyle='#202328';g.fillRect(0,0,180,120);const ratio=Math.min(180/renderer.domElement.width,120/renderer.domElement.height);const w=renderer.domElement.width*ratio,h=renderer.domElement.height*ratio;g.drawImage(renderer.domElement,(180-w)/2,(120-h)/2,w,h);post('thumbnail:'+c.toDataURL('image/png').split(',')[1]);
}
window.modelCommand=action=>{
 if(!ready)return;
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
window.modelState=()=>({ready,wire,textures,flat,ssao,ssaoActive:ssao&&!flat&&aoStrength>0,aoStrength,clipNear:camera?.near,clipFar:camera?.far,skyDome,skyStrength,studioDirectionalVisible:keyLight?.visible,selectedLight,gizmoVisible:!!transform?.object,gizmoAxis:transform?.axis,dragging:transform?.dragging,lightMode,detailsVisible,lights:userLights.filter(x=>x.active).map(x=>({id:x.id,color:"#"+x.light.color.getHexString(),strength:x.strength,position:x.light.position.toArray(),screen:x.handle.position.clone().project(camera).toArray()})),grid:grid?.visible,meshes:window.modelStats?.meshes,triangles:window.modelStats?.triangles,camera:camera?.position.toArray(),target:controls?.target.toArray()});
window.addEventListener('keydown',e=>{if(e.key==='Escape'||e.key==='F11'){e.preventDefault();if(lightMode)setLightMode(false);else if(selectedLight>=0)selectLight(-1);else post('exit');}else if(e.key.toLowerCase()==='f')fit();else if(e.key.toLowerCase()==='l'&&!e.repeat){e.preventDefault();setLightMode(!lightMode);}else if(e.key.toLowerCase()==='i'&&!e.repeat)post('toggleDetails');});
window.addEventListener('resize',resize);
function setLightMode(enabled){
 if(!ready)return;if(enabled)selectLight(-1);lightMode=enabled;renderer.domElement.style.cursor=enabled?'crosshair':'';post('lightMode:'+(enabled?'on':'off'));
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
window.setModelDetails=(text,topPixels,visible)=>{
 const panel=document.getElementById('details');panel.textContent=text;panel.style.top=Math.ceil(topPixels/devicePixelRatio)+'px';detailsVisible=visible;panel.hidden=!visible;
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
  if(!gesture){if(blank&&Math.hypot(e.clientX-blank.x,e.clientY-blank.y)<5)selectLight(-1);blank=null;return;}
  const hit=gesture;gesture=null;e.preventDefault();e.stopImmediatePropagation();if(renderer.domElement.hasPointerCapture(e.pointerId))renderer.domElement.releasePointerCapture(e.pointerId);
  if(Math.hypot(e.clientX-hit.x,e.clientY-hit.y)>5)return;
  if(hit.id!==undefined){setLightMode(false);selectLight(hit.id);}
  else if(lightMode)addLight(e.clientX,e.clientY);
 },true);
 renderer.domElement.addEventListener('dblclick',e=>{if(!ready)return;const id=lightHit(e.clientX,e.clientY);if(id!==undefined){e.preventDefault();setLightMode(false);selectLight(id);editSelectedLight();}});
 renderer.domElement.addEventListener('pointercancel',()=>{gesture=null;blank=null;if(transform.dragging)transform.pointerUp({button:0});controls.enabled=true;});
}const manager=new THREE.LoadingManager();
manager.setURLModifier(url=>{
 const resolved=new URL(url,location.href);
 if(!['https://model.lumaview.invalid','https://viewer.lumaview.invalid'].includes(resolved.origin)&&!['data:','blob:'].includes(resolved.protocol))throw new Error('Only local model resources are supported.');
 return url;
});
manager.onError=url=>failed.add(url);
manager.onLoad=()=>{if(ready){draw();setTimeout(capture,100);}};
try{
 renderer=new THREE.WebGLRenderer({antialias:true,alpha:false});renderer.setPixelRatio(Math.min(devicePixelRatio,2));renderer.setSize(innerWidth,innerHeight);renderer.setClearColor(0x202328);renderer.toneMapping=THREE.ACESFilmicToneMapping;document.body.appendChild(renderer.domElement);
 scene=new THREE.Scene();overlayScene=new THREE.Scene();camera=new THREE.PerspectiveCamera(40,innerWidth/innerHeight,.01,10000);
 transform=new TransformControls(camera,renderer.domElement);transform.setMode('translate');transform.setSpace('world');transform.setSize(.85);transform.enabled=false;overlayScene.add(transform.getHelper());
 transform.addEventListener('dragging-changed',e=>{controls.enabled=!e.value;});
 transform.addEventListener('objectChange',()=>{const item=userLights[selectedLight];if(item?.active)item.handle.position.copy(item.light.position);draw();});transform.addEventListener('change',draw);
 controls=new OrbitControls(camera,renderer.domElement);controls.addEventListener('change',draw);controls.enableDamping=false;setupLightPicking();
 const pmrem=new THREE.PMREMGenerator(renderer);const room=new RoomEnvironment();studioEnvironment=pmrem.fromScene(room);scene.environment=studioEnvironment.texture;room.dispose();pmrem.dispose();ambient=new THREE.HemisphereLight(0xffffff,0x505768,2);scene.add(ambient);keyLight=new THREE.DirectionalLight(0xffffff,2.5);keyLight.position.set(4,8,5);scene.add(keyLight);
 window.loadModel=async(url,extension)=>{
  try{
   if(model)throw new Error('Open a new viewer to load another model.');
   if(extension==='glb'||extension==='gltf'){
    const draco=new DRACOLoader(manager).setDecoderPath('./vendor/addons/libs/draco/gltf/');draco.setWorkerLimit(2);
    const ktx=new KTX2Loader(manager).setTranscoderPath('./vendor/addons/libs/basis/').detectSupport(renderer);
    const loader=new GLTFLoader(manager).setDRACOLoader(draco).setKTX2Loader(ktx).setMeshoptDecoder(MeshoptDecoder);
    try{model=(await loader.loadAsync(url)).scene;}finally{draco.dispose();ktx.dispose();}
   }else if(extension==='obj'){
    const response=await fetch(url);if(!response.ok)throw new Error('Model file could not be read.');const text=await response.text();const obj=new OBJLoader(manager);const refs=[...text.matchAll(/^\s*mtllib\s+(.+)$/gm)].map(m=>m[1].trim());
    // MTL paths are relative to the OBJ; textures are relative to their MTL file.
    let creator=null;
    for(const ref of refs){try{const mtlUrl=new URL(ref.replace(/\\/g,'/'),url).href;const mtl=await new MTLLoader(manager).loadAsync(mtlUrl);mtl.preload();if(!creator)creator=mtl;else{Object.assign(creator.materialsInfo,mtl.materialsInfo);Object.assign(creator.materials,mtl.materials);}}catch(e){failed.add(ref);}}
    if(creator)obj.setMaterials(creator);model=obj.parse(text);
   }else if(extension==='fbx'){
    model=await new FBXLoader(manager).loadAsync(url);
   }else if(extension==='stl'){
    const geometry=await new STLLoader(manager).loadAsync(url);model=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:0xb4bdc9,roughness:.65,vertexColors:!!geometry.attributes.color}));
   }else throw new Error('Unsupported 3D format.');
   let meshes=0,triangles=0;const mats=new Set();model.updateMatrixWorld(true);model.traverse(o=>{if(!o.isMesh)return;meshes++;const geometry=o.geometry;if(!geometry.attributes.normal)geometry.computeVertexNormals();triangles+=Math.floor((geometry.index?geometry.index.count:geometry.attributes.position.count)/3)*(o.isInstancedMesh?o.count:1);originals.set(o,o.material);(Array.isArray(o.material)?o.material:[o.material]).forEach(m=>mats.add(m));});
   if(!meshes)throw new Error('No mesh found in this file.');
   const box=new THREE.Box3().setFromObject(model,true);const size=box.getSize(new THREE.Vector3()),center=box.getCenter(new THREE.Vector3());radius=size.length()/2;
   if(!Number.isFinite(radius)||radius<=0)throw new Error('Model bounds are empty or invalid.');
   const wrapper=new THREE.Group();wrapper.add(model);wrapper.position.copy(center).negate();scene.add(wrapper);
   grid=new THREE.GridHelper(radius*3,20,0x697586,0x38414b);grid.position.y=-size.y/2-radius*.002;scene.add(grid);axes=new THREE.AxesHelper(radius*.6);axes.position.y=grid.position.y+.001*radius;scene.add(axes);
   ready=true;window.modelStats={meshes,triangles,materials:mats.size,size:size.toArray()};fit();message.textContent=failed.size?'Some local material/texture files could not be loaded.':'';
   post('loaded:'+JSON.stringify(window.modelStats));setTimeout(capture,200);
  }catch(e){error(e);}
 };
 post('ready');
}catch(e){error(e);}







