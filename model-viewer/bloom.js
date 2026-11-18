import * as THREE from 'three';
import {Pass, FullScreenQuad} from 'three/addons/postprocessing/Pass.js';

const vertexShader = `
 varying vec2 vUv;
 void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
 }
`;

// Clamp before filtering: a single non-finite HDR sample must not spread across
// the viewport via the blur or invalidate the output color conversion.
const finiteColor = `
 float finiteChannel(float value) { return value >= 0.0 ? min(value, 64.0) : 0.0; }
 vec3 finiteRgb(vec3 value) {
  return vec3(finiteChannel(value.r), finiteChannel(value.g), finiteChannel(value.b));
 }
`;
function shader(uniforms, fragmentShader) {
 return new THREE.ShaderMaterial({
  uniforms, vertexShader, fragmentShader, depthTest:false, depthWrite:false,
  blending:THREE.NoBlending, toneMapped:false
 });
}

export class BloomPass extends Pass {
 constructor() {
  super();
  const options={type:THREE.HalfFloatType,depthBuffer:false,stencilBuffer:false};
  this.horizontal=new THREE.WebGLRenderTarget(1,1,options);
  this.vertical=new THREE.WebGLRenderTarget(1,1,options);
  this.threshold=1;
  this.strength=.45;
  this.blur=shader({
   inputTexture:{value:null}, stepUv:{value:new THREE.Vector2()}, threshold:{value:1}, extract:{value:true}
  },`
   uniform sampler2D inputTexture;
   uniform vec2 stepUv;
   uniform float threshold;
   uniform bool extract;
   varying vec2 vUv;
   ${finiteColor}
   vec3 sampleGlow(vec2 uv) {
    vec3 color=finiteRgb(texture2D(inputTexture,uv).rgb);
    return extract ? color*smoothstep(threshold,threshold+0.2,dot(color,vec3(0.2126,0.7152,0.0722))) : color;
   }
   void main() {
    vec3 color=0.227027*sampleGlow(vUv);
    color+=0.316216*(sampleGlow(vUv+stepUv*1.384615)+sampleGlow(vUv-stepUv*1.384615));
    color+=0.070270*(sampleGlow(vUv+stepUv*3.230769)+sampleGlow(vUv-stepUv*3.230769));
    gl_FragColor=vec4(color,1.0);
   }
  `);
  this.merge=shader({baseTexture:{value:null}, glowTexture:{value:null}, strength:{value:1}},`
   uniform sampler2D baseTexture;
   uniform sampler2D glowTexture;
   uniform float strength;
   varying vec2 vUv;
   ${finiteColor}
   void main() {
    vec4 base=texture2D(baseTexture,vUv);
    gl_FragColor=vec4(finiteRgb(base.rgb)+strength*finiteRgb(texture2D(glowTexture,vUv).rgb),base.a);
   }
  `);
  this.quad=new FullScreenQuad(this.blur);
 }
 setSize(width,height) {
  const scale=Math.min(.25,1024/Math.max(1,width,height));
  width=Math.max(1,Math.round(width*scale));
  height=Math.max(1,Math.round(height*scale));
  this.horizontal.setSize(width,height);
  this.vertical.setSize(width,height);
 }
 render(renderer,writeBuffer,readBuffer) {
  const blur=this.blur.uniforms,merge=this.merge.uniforms;
  this.quad.material=this.blur;
  blur.inputTexture.value=readBuffer.texture;
  blur.extract.value=true;
  blur.threshold.value=this.threshold;
  blur.stepUv.value.set(1/this.horizontal.width,0);
  renderer.setRenderTarget(this.horizontal);
  this.quad.render(renderer);
  blur.inputTexture.value=this.horizontal.texture;
  blur.extract.value=false;
  blur.stepUv.value.set(0,1/this.vertical.height);
  renderer.setRenderTarget(this.vertical);
  this.quad.render(renderer);
  this.quad.material=this.merge;
  merge.baseTexture.value=readBuffer.texture;
  merge.glowTexture.value=this.vertical.texture;
  merge.strength.value=this.strength;
  renderer.setRenderTarget(this.renderToScreen?null:writeBuffer);
  this.quad.render(renderer);
 }
 dispose() {
  this.quad.dispose();
  this.blur.dispose();
  this.merge.dispose();
  this.horizontal.dispose();
  this.vertical.dispose();
 }
}
