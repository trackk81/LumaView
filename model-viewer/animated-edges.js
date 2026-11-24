import {DepthTexture,UnsignedIntType,Vector2} from 'three';

// The outline mask already draws the posed model. Its red and green channels
// hold coverage and occlusion. Store the face normal in the two spare channels.
export function addAnimatedEdges(outline){
 const mask=outline.renderTargetMaskBuffer;
 mask.depthTexture=new DepthTexture(mask.width,mask.height,UnsignedIntType);
 const prepare=outline.prepareMaskMaterial;
 prepare.fragmentShader=prepare.fragmentShader.replace('gl_FragColor = vec4(0.0, depthTest, 1.0, 1.0);',`
  vec3 normal=cross(dFdx(vPosition.xyz),dFdy(vPosition.xyz));
  normal/=max(abs(normal.x)+abs(normal.y)+abs(normal.z),1e-20);
  vec2 oct=normal.z<0.0 ? (1.0-abs(normal.yx))*sign(normal.xy) : normal.xy;
  gl_FragColor=vec4(0.0,depthTest,oct*0.5+0.5);`);
 prepare.needsUpdate=true;

 const overlay=outline.overlayMaterial,uniforms=overlay.uniforms;
 uniforms.poseDepth={value:mask.depthTexture};
 uniforms.edgeStep={value:new Vector2()};
 uniforms.lineRadius={value:1};
 uniforms.nearFar={value:new Vector2()};
 uniforms.pixelFootprint={value:0};
 overlay.fragmentShader=overlay.fragmentShader.replace('void main() {',`
  uniform sampler2D poseDepth;
  uniform vec2 edgeStep,nearFar;
  uniform float pixelFootprint,lineRadius;
  vec3 faceNormal(vec2 packedNormal){
   vec2 p=packedNormal*2.0-1.0;
   vec3 n=vec3(p,1.0-abs(p.x)-abs(p.y));
   n.xy-=sign(n.xy)*max(-n.z,0.0);
   return normalize(n);
  }
  float viewDepth(vec2 uv){
   float d=texture2D(poseDepth,uv).r;
   return nearFar.x*nearFar.y/mix(nearFar.y,nearFar.x,d);
  }
  float edgeAt(vec2 stepUv,vec3 normal,float centerDepth,float footprint){
   vec4 a=texture2D(maskTexture,vUv-stepUv),b=texture2D(maskTexture,vUv+stepUv);
   bool hasA=max(a.r,a.g)<0.5,hasB=max(b.r,b.g)<0.5;
   float crease=max(hasA?1.0-dot(normal,faceNormal(a.ba)):0.0,hasB?1.0-dot(normal,faceNormal(b.ba)):0.0);
   float edge=smoothstep(0.12,0.18,crease);
   if(!hasA||!hasB)return edge;
   // Opposite samples cancel a planar slope and expose overlapping parts.
   float bend=abs(viewDepth(vUv-stepUv)+viewDepth(vUv+stepUv)-2.0*centerDepth);
   return max(edge,smoothstep(2.0*footprint,4.0*footprint,bend));
  }
  float creaseAtRadius(float radius,vec3 normal,float z){
   vec2 stepUv=radius*edgeStep;
   float footprint=z*pixelFootprint*radius;
   return max(edgeAt(vec2(stepUv.x,0.0),normal,z,footprint),edgeAt(vec2(0.0,stepUv.y),normal,z,footprint));
  }
  void main() {`).replace('gl_FragColor = vec4(vec3(0.015), ink);',`
   if(max(maskColor.r,maskColor.g)<0.5){
    vec3 normal=faceNormal(maskColor.ba);
    float z=viewDepth(vUv);
    ink=max(ink,creaseAtRadius(min(lineRadius,1.0),normal,z));
    if(lineRadius>1.0)ink=max(ink,creaseAtRadius(lineRadius,normal,z));
   }
   gl_FragColor=vec4(vec3(0.015),ink);`);
 overlay.needsUpdate=true;
 return (camera,width)=>{
  uniforms.lineRadius.value=Math.max(.75,width*.5);
  uniforms.edgeStep.value.set(1/mask.width,1/mask.height);
  uniforms.nearFar.value.set(camera.near,camera.far);
  uniforms.pixelFootprint.value=2/(camera.projectionMatrix.elements[5]*mask.height);
 };
}
