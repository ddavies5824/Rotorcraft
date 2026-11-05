# Puts the trailer's frames and soundtrack together into video files, with the encoder inside Blender (nothing else to install):
#   blender -b --factory-startup --python Tools\trailer_encode.py -- <trailer folder> <out.mp4> <width> <height> <video kbit/s> <max kbit/s> [audio kbit/s]
# The folder has frames/<part>/f00000.jpg (from Scripts/VR/TrailerRun.cs) and music.wav (from Tools/trailer_music.py).
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1 :]
folder, out, width, height, kbit, maxkbit = args[0], args[1], int(args[2]), int(args[3]), int(args[4]), int(args[5])
audio = int(args[6]) if len(args) > 6 else 192
FPS = 30
PARTS = ["open", "world", "weather", "aircraft", "cockpit", "missions", "career", "vr", "end"]

scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1
scene.render.resolution_x = width
scene.render.resolution_y = height
scene.render.resolution_percentage = 100
editor = scene.sequence_editor_create()
strips = editor.strips

at = 1
for part in PARTS:
    d = os.path.join(folder, "frames", part)
    if not os.path.isdir(d):
        continue
    files = sorted(f for f in os.listdir(d) if f.endswith(".jpg"))
    if not files:
        continue
    strip = strips.new_image(name=part, filepath=os.path.join(d, files[0]), channel=2, frame_start=at, fit_method="FIT")
    for name in files[1:]:
        strip.elements.append(name)
    strip.frame_final_duration = len(files)
    at += len(files)
    print(f"{part}: {len(files)} frames, ends at {at - 1}")
music = os.path.join(folder, "music.wav")
if os.path.exists(music):
    strips.new_sound(name="music", filepath=music, channel=1, frame_start=1)
scene.frame_start = 1
scene.frame_end = at - 1

r = scene.render
r.use_sequencer = True
r.use_compositing = False
s = r.image_settings
if hasattr(s, "media_type"):
    s.media_type = "VIDEO"
s.file_format = "FFMPEG"
s.color_mode = "RGB"
f = r.ffmpeg
f.format = "MPEG4"
f.codec = "H264"
f.constant_rate_factor = "NONE"
f.ffmpeg_preset = "BEST"
f.gopsize = FPS
f.video_bitrate = kbit
f.minrate = 0
f.maxrate = maxkbit
f.buffersize = maxkbit * 2
f.audio_codec = "AAC"
f.audio_bitrate = audio
f.audio_channels = "STEREO"
f.audio_mixrate = 48000
r.filepath = out
bpy.ops.render.render(animation=True, write_still=False)
# Blender puts the file's index last, which an iPhone may refuse to load from a server
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mp4_faststart
mp4_faststart.run(out)
print("encoded", out, os.path.getsize(out) if os.path.exists(out) else "no file", "from frames", scene.frame_start, "to", scene.frame_end)
