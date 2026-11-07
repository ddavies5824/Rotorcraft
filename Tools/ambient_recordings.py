# Makes the Windows build's world-sound recordings (Sounds/*.ogg and Sounds/sounds.json) from the CC0 recordings downloaded
# from BigSoundBank. Run with Blender, whose audio module reads and writes OGG Vorbis:
#   set RC_SRC=<folder of downloads> & set RC_OUT=<project>/Sounds
#   blender -b --factory-startup --python Tools/ambient_recordings.py
# Each recording is cut to a stretch with nothing out of place, filtered (wind rumble on the microphone, distant traffic),
# set to a common level and saved with a few seconds over, which the game crossfades into the start so it loops without a seam.
import os, json
import aud, numpy as np

SRC = os.environ["RC_SRC"]; OUT = os.environ["RC_OUT"]
RATE = 48000
FADE = 3.0           # seconds of overlap the game crossfades
TARGET_RMS = 0.1     # common level the files are saved at (the game scales each to the layer it replaces)

# file in the downloads, what it stands in for, the stretch to keep (s), stereo or mono, high-pass Hz, low-pass Hz (0 = none),
# whether it loops (a bed) or plays as a bout now and then (calls), and where it came from.
RECORDINGS = [
    ("forest-2749.ogg", "birds",  5, 98,   2, 150, 0,    "bed",   "Forest #4", "https://bigsoundbank.com/forest-4-s2749.html"),
    ("leaves-0904.ogg", "leaves", 17, 80,  1, 250, 0,    "bed",   "Wind in the Trees", "https://bigsoundbank.com/forest-wind-in-the-trees-s0904.html"),
    ("rain-1019.ogg",   "rain",   4, 77,   2, 100, 0,    "bed",   "Summer Rain on Terrace", "https://bigsoundbank.com/summer-rain-on-terrace-s1019.html"),
    ("town-0608.ogg",   "hum",    0, 103,  1, 60,  0,    "bed",   "Street and Roads", "https://bigsoundbank.com/street-and-roads-s0608.html"),
    ("owl-0937.ogg",    "owls",   0, 75.5, 1, 220, 2400, "calls", "Long-eared owl #2", "https://bigsoundbank.com/long-eared-owl-2-s0937.html"),
    # not yet downloaded (the site refused them): the sea and gulls stay generated until these are added
    ("sea-2565.ogg",    "shore",  10, 103, 2, 60,  0,    "bed",   "Sea of the Beach of Dellec #2", "https://bigsoundbank.com/sea-of-the-beach-of-dellec-2-s2565.html"),
    ("gulls-2573.ogg",  "gulls",  5, 98,   2, 150, 0,    "bed",   "Gulls on the Harbor", "https://bigsoundbank.com/gulls-on-the-harbor-s2573.html"),
]


def band(x, rate, hp, lp):
    """Zero-phase filter in the frequency domain: second-order Butterworth slopes, applied to each channel."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / rate)
    g = np.ones_like(f)
    if hp: g *= 1 / np.sqrt(1 + (hp / np.maximum(f, 1e-3)) ** 4)
    if lp: g *= 1 / np.sqrt(1 + (f / lp) ** 4)
    return np.fft.irfft(np.fft.rfft(x, axis=0) * g[:, None], n=n, axis=0)


def limit(y, ceiling, rate):
    """Hold the peaks under the ceiling (rain drops, a loud bird close by) with a 3 ms look-ahead, so the level can come up without clipping."""
    from numpy.lib.stride_tricks import sliding_window_view
    a = np.abs(y).max(axis=1)
    w = int(0.003 * rate)
    m = sliding_window_view(np.pad(a, (w, w), mode="edge"), 2 * w + 1).max(axis=1)
    g = np.minimum(1, ceiling / np.maximum(m, 1e-9))
    k = w // 2   # smoothing inside the hold window keeps every gain at or under what its peak needs
    g = np.convolve(np.pad(g, (k, k), mode="edge"), np.ones(2 * k + 1) / (2 * k + 1), mode="valid")
    return y * g[:, None]


CREST = 10   # loudest peak at most this many times the average level once limited

os.makedirs(OUT, exist_ok=True)
made = []
for name, layer, a, b, ch, hp, lp, kind, title, url in RECORDINGS:
    path = os.path.join(SRC, name)
    if not os.path.exists(path):
        print(f"skip {name}: not downloaded"); continue
    s = aud.Sound(path)
    rate = int(s.specs[0])
    assert rate == RATE, f"{name}: {rate} Hz"
    x = s.data().astype(np.float64)
    x = x.mean(axis=1, keepdims=True) if ch == 1 else (x if x.shape[1] == 2 else np.repeat(x, 2, axis=1))
    # filter a second either side of the stretch so the filter's edges fall outside it
    lo, hi = int(a * rate), min(len(x), int(b * rate))
    pad0, pad1 = max(0, lo - rate), min(len(x), hi + rate)
    y = band(x[pad0:pad1], rate, hp, lp)[lo - pad0: hi - pad0]
    y -= y.mean(axis=0)
    y /= np.sqrt(np.mean(y ** 2))
    if kind == "bed": y = limit(y, CREST, rate)
    rms = float(np.sqrt(np.mean(y ** 2))); peak = float(np.abs(y).max())
    scale = min(TARGET_RMS / rms, 0.95 / peak)
    y *= scale
    out = os.path.splitext(name)[0].split("-")[0] + ".ogg"
    snd = aud.Sound.buffer(y.astype(np.float32), float(rate))
    snd.write(os.path.join(OUT, out), rate=rate, channels=aud.CHANNELS_STEREO if ch == 2 else aud.CHANNELS_MONO,
              format=aud.FORMAT_FLOAT32, container=aud.CONTAINER_OGG, codec=aud.CODEC_VORBIS, bitrate=160000)
    back = aud.Sound(os.path.join(OUT, out)).data()
    final_rms = float(np.sqrt(np.mean(y ** 2)))
    if kind == "calls":   # how loud the calls themselves are, not the quiet between them
        env = np.sqrt(np.convolve((y ** 2).mean(axis=1), np.ones(rate // 10) / (rate // 10), mode="same"))
        final_rms = float(np.sqrt(np.mean(((y ** 2).mean(axis=1))[env > np.percentile(env, 70)])))
    made.append({"layer": layer, "file": out, "kind": kind, "rms": round(final_rms, 5), "fade": FADE if kind == "bed" else 0,
                 "seconds": round(len(y) / rate, 2), "title": title, "source": url, "licence": "CC0 (public domain)"})
    print(f"{out}: {layer}, {len(y) / rate:.1f} s, {ch} ch, rms {final_rms:.4f} (peak {np.abs(y).max():.3f}, x{scale:.2f}), "
          f"read back {len(back)} of {len(y)} frames, {os.path.getsize(os.path.join(OUT, out)) / 1e6:.2f} MB")

json.dump({"rate": RATE, "sounds": made}, open(os.path.join(OUT, "sounds.json"), "w"), indent=2)
with open(os.path.join(OUT, "CREDITS.txt"), "w") as f:
    f.write("World sounds in the Windows build (the web build makes its own).\n"
            "Recordings by Joseph Sardin, BigSoundBank.com, CC0 (public domain): free to use, no attribution required.\n"
            "Each was cut, filtered and levelled by Tools/ambient_recordings.py.\n\n")
    for m in made: f.write(f"{m['file']}: {m['title']}  {m['source']}\n")
print("wrote", len(made), "sounds")
