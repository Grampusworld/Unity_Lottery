#!/usr/bin/env python3
"""Reproducible local edit of actual Unity PNG takes; no generated gameplay."""
import argparse
import json
import math
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FFMPEG = '/Applications/VideoFusion-macOS.app/Contents/Resources/ffmpeg'
FONT = '/System/Library/Fonts/STHeiti Medium.ttc'
W, H, FPS = 1920, 1080, 60

def run(args, log):
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open('w') as output:
        result = subprocess.run([FFMPEG, '-hide_banner', '-y', *map(str,args)], stdout=output, stderr=output)
    if result.returncode:
        raise RuntimeError(log.read_text()[-5000:])

def caption_png(text, path, y=940):
    image = Image.new('RGBA', (W,H))
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype(FONT, 43)
    box = draw.textbbox((0,0), text, font=font)
    tw = box[2]-box[0]
    x = (W-tw)//2
    draw.rectangle((x-34,y-19,x+tw+34,y+70),fill=(16,23,38,235))
    draw.rectangle((x-34,y-19,x+tw+34,y-15),fill=(227,176,67,255))
    draw.text((x,y-box[1]+4),text,font=font,fill=(250,240,208,255))
    image.save(path)

def lerp_expression(keys, prop, frames):
    # Smoothstep interpolation, expressed in output frame number for zoompan.
    fallback = str(keys[-1][prop])
    for left,right in reversed(list(zip(keys,keys[1:]))):
        a=left['t']*FPS; b=right['t']*FPS
        u=f'min(max((on-{a:.6f})/{max(1,b-a):.6f},0),1)'
        smooth=f'({u})*({u})*(3-2*({u}))'
        value=f'{left[prop]}+({right[prop]-left[prop]})*({smooth})'
        fallback=f'if(lt(on,{b:.6f}),{value},{fallback})'
    return fallback

def source_path(base, text):
    path=Path(text)
    return path if path.is_absolute() else (base/path).resolve()

def encode_part(part, caption, index, base, cache, force=False):
    duration=part['duration']; frames=round(duration*FPS)
    source=part['source']; fps=source.get('fps',60)
    start=round(source.get('start_time',0)*fps)+source.get('start_number',0)
    source_duration=source.get('duration',duration)
    rate=source_duration/duration
    pattern=source_path(base,source['pattern'])
    path=cache/f'{index:02d}.mp4'
    signature=json.dumps([part,caption],ensure_ascii=False,sort_keys=True)
    stamp=path.with_suffix('.json')
    if path.exists() and stamp.exists() and stamp.read_text()==signature and not force:
        print('reuse',path.name,flush=True);return path
    first=Path(str(pattern)%start)
    if not first.exists(): raise FileNotFoundError(first)
    with Image.open(first) as im: sw,sh=im.size
    keys=part.get('camera',{}).get('keyframes',[{'t':0,'zoom':1,'cx':.5,'cy':.5},{'t':duration,'zoom':1,'cx':.5,'cy':.5}])
    z=lerp_expression(keys,'zoom',frames);cx=lerp_expression(keys,'cx',frames);cy=lerp_expression(keys,'cy',frames)
    filters=f"[0:v]trim=duration={source_duration:.8f},setpts=(PTS-STARTPTS)/{rate:.8f},fps={FPS},tpad=stop_mode=clone:stop_duration=1,zoompan=z='{z}':x='max(0,min(iw-iw/zoom,iw*({cx})-iw/zoom/2))':y='max(0,min(ih-ih/zoom,ih*({cy})-ih/zoom/2))':d=1:s={W}x{H}:fps={FPS},setsar=1[v0]"
    args=['-framerate',fps,'-start_number',start,'-i',pattern]
    if caption:
        png=cache/f'caption_{index:02d}.png';caption_png(caption,png,part.get('caption_y',940))
        args+=['-loop','1','-framerate',FPS,'-i',png]
        filters+=';[v0][1:v]overlay=0:0:shortest=1:format=auto[v]'
    else:filters+=';[v0]null[v]'
    args+=['-filter_complex',filters,'-map','[v]','-an','-frames:v',frames,'-r',FPS,'-c:v','h264_videotoolbox','-allow_sw','1','-b:v','18000k','-maxrate','24000k','-bufsize','36000k','-pix_fmt','yuv420p','-tag:v','avc1','-movflags','+faststart',path]
    print('render',index,part.get('label',''),duration,'sec',flush=True)
    run(args,cache/f'part_{index:02d}.log');stamp.write_text(signature)
    return path

def main():
    parser=argparse.ArgumentParser();parser.add_argument('manifest');parser.add_argument('--force',action='store_true');parser.add_argument('--only',type=int);args=parser.parse_args()
    manifest=Path(args.manifest).resolve();base=manifest.parent;data=json.loads(manifest.read_text());cache=base/'edit-cache';cache.mkdir(exist_ok=True)
    paths=[];events=[];cursor=0.;index=0
    for shot in data['shots']:
        parts=shot.get('parts',[shot]);assert abs(sum(p['duration'] for p in parts)-shot['duration'])<1e-6
        for part in parts:
            caption=part.get('caption',shot.get('caption',''))
            if args.only is None or args.only==index:paths.append(encode_part(part,caption,index,base,cache,args.force))
            source=part['source'];takepath=source_path(base,source['pattern']).parent/'take.json'
            if takepath.exists():
                take=json.loads(takepath.read_text());start=source.get('start_time',0);span=source.get('duration',part['duration']);rate=span/part['duration']
                for event in take.get('cues',[]):
                    time=event['frame']/take['fps']
                    if start<=time<start+span:events.append({'time':round(cursor+(time-start)/rate,5),'clip':event['clip']})
            cursor+=part['duration'];index+=1
    if args.only is not None:return
    assert round(cursor*FPS)==1740, cursor
    (base/'sound-events.json').write_text(json.dumps(events,indent=2,ensure_ascii=False))
    listing=cache/'concat.txt';listing.write_text(''.join("file '"+str(p).replace("'","'\\''")+"'\n" for p in paths))
    video=cache/'picture.mp4';run(['-f','concat','-safe','0','-i',listing,'-c','copy','-an','-movflags','+faststart',video],cache/'concat.log')
    music=source_path(base,data['audio']['music']);audios=source_path(base,data['audio']['sfx_folder'])
    args=['-i',video,'-stream_loop','-1','-i',music]
    # The capture log records the actual accepted SFX events, aligned to the edit.
    graph=[f'[1:a]atrim=start={data["audio"].get("music_start",0)}:duration=29,asetpts=PTS-STARTPTS,volume=0.42,afade=t=in:st=0:d=0.08,afade=t=out:st=28.35:d=0.65[music]']
    mixed=['[music]']
    for i,event in enumerate(events):
        audio=audios/(event['clip']+'.ogg');args+=['-i',audio]
        delay=round(event['time']*1000)
        graph.append(f'[{i+2}:a]aformat=sample_rates=48000:channel_layouts=stereo,volume=0.58,adelay={delay}|{delay}[s{i}]');mixed.append(f'[s{i}]')
    graph.append(''.join(mixed)+f'amix=inputs={len(mixed)}:duration=longest:normalize=0,loudnorm=I=-18:TP=-1.5:LRA=9:print_format=json,atrim=duration=29[out]')
    output=source_path(base,data['output']);output.parent.mkdir(parents=True,exist_ok=True)
    args+=['-filter_complex',';'.join(graph),'-map','0:v:0','-map','[out]','-c:v','copy','-c:a','aac','-b:a','256k','-ar','48000','-t','29','-movflags','+faststart',output]
    run(args,cache/'final.log')
    print('DONE',output,flush=True)

if __name__=='__main__':main()
