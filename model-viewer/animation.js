import {AnimationMixer,LoopOnce,LoopRepeat} from 'three';

function formatTime(seconds){
 const tenths=Math.floor(seconds*10+1e-6);
 return Math.floor(tenths/600)+':'+String(Math.floor(tenths/10)%60).padStart(2,'0')+'.'+tenths%10;
}

export function createPlayback(model,animations,english,draw){
 const clips=animations.filter(clip=>Number.isFinite(clip.duration)&&clip.duration>0&&clip.tracks.length);
 if(!clips.length)return null;
 const mixer=new AnimationMixer(model);
 const bar=document.getElementById('playback');
 const play=bar.querySelector('button'),select=bar.querySelector('select'),seek=bar.querySelector('input[type=range]');
 const loop=bar.querySelector('input[type=checkbox]'),time=bar.querySelector('output');
 const t=(ko,en)=>english?en:ko;
 bar.setAttribute('aria-label',t('애니메이션 재생','Animation playback'));
 select.setAttribute('aria-label',t('애니메이션 클립','Animation clip'));
 seek.setAttribute('aria-label',t('재생 위치','Playback position'));
 bar.querySelector('label span').textContent=t('반복','Loop');
 for(const [index,clip] of clips.entries()){
  const name=typeof clip.name==='string'?clip.name.trim():'';
  select.add(new Option(name||t('클립 ','Clip ')+(index+1),index));
 }
 select.hidden=clips.length===1;
 let action,playing=false,scrubbing=false,lastFrame=null;
 function syncPlay(){play.textContent=playing?t('일시정지','Pause'):t('재생','Play');}
 function syncTime(){
  seek.value=action.time;
  const current=formatTime(action.time),display=current+' / '+formatTime(action.getClip().duration);
  if(time.textContent!==display){time.textContent=display;seek.setAttribute('aria-valuetext',current);}
 }
 function setLoop(){action.setLoop(loop.checked?LoopRepeat:LoopOnce,loop.checked?Infinity:1);}
 function selectClip(){
  if(action)action.stop();
  action=mixer.clipAction(clips[select.selectedIndex]).reset();
  action.clampWhenFinished=true;setLoop();action.play();mixer.update(0);
  seek.max=action.getClip().duration;lastFrame=null;syncTime();draw();
 }
 function toggle(){
  if(!playing&&action.time>=action.getClip().duration){action.reset().play();mixer.update(0);}
  playing=!playing;lastFrame=null;syncPlay();syncTime();draw();
 }
 function seekTo(){
  action.time=Number(seek.value);action.paused=false;mixer.update(0);
  lastFrame=null;syncTime();draw();
 }
 function endScrub(){if(scrubbing){scrubbing=false;lastFrame=null;draw();}}
 function resetClock(){lastFrame=null;if(!document.hidden)draw();}
 play.onclick=toggle;loop.onchange=setLoop;select.onchange=selectClip;seek.oninput=seekTo;
 seek.onpointerdown=event=>{scrubbing=true;seek.setPointerCapture(event.pointerId);};
 seek.onpointerup=seek.onpointercancel=seek.onlostpointercapture=endScrub;
 mixer.addEventListener('finished',()=>{playing=false;syncPlay();});
 document.addEventListener('visibilitychange',resetClock);
 // Skin bounds computed in the bind pose can exclude the animated skeleton.
 model.traverse(node=>{if(node.isSkinnedMesh)node.frustumCulled=false;});
 selectClip();syncPlay();bar.hidden=false;
 return {
  resetClock,
  update(now){
   if(!playing||scrubbing||document.hidden)return;
   if(lastFrame!==null)mixer.update(Math.max(0,(now-lastFrame)/1000));
   lastFrame=now;syncTime();if(playing)draw();
  },
  dispose(){
   playing=false;document.removeEventListener('visibilitychange',resetClock);
   mixer.stopAllAction();mixer.uncacheRoot(model);
  }
 };
}
