# The trailer's soundtrack, composed and performed entirely in code, so it belongs to the game and nothing has to be licensed.
# It reads the edit (frames/<part>/timeline.txt, written by Scripts/VR/TrailerRun.cs), so the sections and hits follow the shots.
# Run with Blender's Python, which has numpy:
#   F:\Blender\5.2\python\bin\python.exe -I Tools\trailer_music.py <trailer folder> [fps]
# Writes <folder>/music.wav and music-check.png (waveform and spectrogram, to look at the mix) and prints the level of each section.
import os, struct, sys, wave, zlib

import numpy as np

SR = 48000
BAR = 2.0                     # 120 beats a minute, four beats to the bar
BEAT = BAR / 4
PARTS = ["open", "world", "weather", "aircraft", "cockpit", "missions", "career", "vr", "end"]
rng = np.random.default_rng(2031)

folder = sys.argv[1]
fps = int(sys.argv[2]) if len(sys.argv) > 2 else 30
frames_per_bar = fps * 2

# ------------------------------------------------------------------------------------------- the edit
shots = []     # (name, part, start in bars, length in bars)
at = 0.0
for part in PARTS:
    path = os.path.join(folder, "frames", part, "timeline.txt")
    if not os.path.exists(path):
        continue
    for line in open(path, encoding="utf-8"):
        cols = line.rstrip("\n").split("\t")
        if len(cols) < 3:
            continue
        n = int(cols[1]) / frames_per_bar
        shots.append((cols[2], part, at, n))
        at += n
total_bars = at
if not shots:
    sys.exit("no timeline found")
N = int(total_bars * BAR * SR)
print(f"{len(shots)} shots, {total_bars} bars, {total_bars * BAR:.1f} s")


def shot_named(name):
    for s in shots:
        if s[0] == name:
            return s
    return None


title_bar = (shot_named("title over the lift-off") or shots[0])[2]
final_bar = (shot_named("end card") or shots[-1])[2]
hero_bar = (shot_named("hero") or shots[-1])[2]

# how much of each instrument plays in each stretch of the film
LEVELS = {
    #            pad  drone arp  bass taiko snare tick lead
    "open":     (.70, .55, .00, .00, .00, .00, .00, .70),
    "title":    (.85, .40, .55, .70, .85, .30, .30, .00),
    "world":    (.60, .22, .75, .70, .70, .30, .45, .45),
    "weather":  (.85, .45, .35, .50, .45, .00, .20, .00),
    "aircraft": (.70, .20, .80, .90, 1.0, .70, .60, .90),
    "cockpit":  (.50, .10, .70, .60, .40, .20, .85, .30),
    "missions": (.90, .30, .90, 1.0, 1.0, 1.0, .70, 1.0),
    "career":   (.60, .00, .85, .50, .20, .20, .50, .55),
    "vr":       (.75, .20, .85, .75, .65, .50, .60, .75),
    "hero":     (1.0, .40, 1.0, 1.0, 1.0, 1.0, .80, 1.0),
    "card":     (.00, .00, .00, .00, .00, .00, .00, .00),
}
PAD, DRONE, ARP, BASS, TAIKO, SNARE, TICK, LEAD = range(8)


def section_at(bar):
    if bar >= final_bar:
        return "card"
    if bar >= hero_bar:
        return "hero"
    part = "open"
    for name, p, start, length in shots:
        if start <= bar < start + length:
            part = p
    if part == "open":
        return "title" if bar >= title_bar else "open"
    return part if part in LEVELS else "world"


def level(bar, instrument):
    return LEVELS[section_at(bar)][instrument]


# ---------------------------------------------------------------------------------------- the tools
dry = np.zeros((2, N + SR * 4))
wet = np.zeros((2, N + SR * 4))


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def put(sig, at_seconds, gain=1.0, pan=0.0, send=0.25):
    start = int(at_seconds * SR)
    if start >= dry.shape[1] or start < 0:
        return
    sig = sig[: dry.shape[1] - start]
    l, r = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    dry[0, start : start + len(sig)] += sig * gain * l
    dry[1, start : start + len(sig)] += sig * gain * r
    wet[0, start : start + len(sig)] += sig * gain * l * send
    wet[1, start : start + len(sig)] += sig * gain * r * send


def adsr(n, a, d, s, r):
    env = np.full(n, s, dtype=float)
    na, nd, nr = int(a * SR), int(d * SR), int(r * SR)
    na = min(na, n)
    env[:na] = np.linspace(0, 1, na, endpoint=False)
    nd = min(nd, n - na)
    if nd > 0:
        env[na : na + nd] = np.linspace(1, s, nd, endpoint=False)
    nr = min(nr, n)
    if nr > 0:
        env[-nr:] *= np.linspace(1, 0, nr)
    return env


def band(sig, low, high):
    spec = np.fft.rfft(sig)
    f = np.fft.rfftfreq(len(sig), 1 / SR)
    gain = np.ones_like(f)
    if low > 0:
        gain *= np.clip((f - low * 0.7) / (low * 0.3 + 1e-9), 0, 1)
    if high < SR / 2:
        gain *= np.clip((high * 1.4 - f) / (high * 0.4), 0, 1)
    return np.fft.irfft(spec * gain, len(sig))


def saw(freq, n, harmonics=8, detune=0.0, vibrato=0.0):
    t = np.arange(n) / SR
    phase = 2 * np.pi * freq * (1 + detune) * t + (vibrato * np.sin(2 * np.pi * 4.6 * t + rng.random() * 6) if vibrato else 0)
    out = np.zeros(n)
    for k in range(1, harmonics + 1):
        if freq * k > SR * 0.45:
            break
        out += np.sin(k * phase + rng.random() * 6) / k
    return out


# -------------------------------------------------------------------------------------- the instruments
def pad(notes, seconds):
    n = int(seconds * SR)
    out = np.zeros(n)
    for m in notes:
        for d in (-0.004, 0.0, 0.0045):
            out += saw(midi(m), n, 7, d, 0.05)
    out = band(out, 110, 2600)
    return out / (len(notes) * 3) * adsr(n, 0.5, 0.2, 0.9, 0.6)


def drone(seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    out = saw(midi(38), n, 22, 0.0) * 0.8 + saw(midi(50), n, 20, 0.001) * 0.6 + saw(midi(45), n, 18, -0.001) * 0.5
    out = band(out, 70, 3200) * (1 + 0.05 * np.sin(2 * np.pi * 0.31 * t))
    return out / 3 * adsr(n, 2.0, 0.1, 1.0, 1.5)


def pluck(m, seconds=1.1):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    f = midi(m)
    out = np.zeros(n)
    for k in range(1, 9):
        if f * k > SR * 0.45:
            break
        out += np.sin(2 * np.pi * f * k * t) * np.exp(-t * (2.2 + 1.1 * k)) / k ** 1.15
    attack = np.minimum(1, t / 0.002)
    return out * attack * 0.55


def bass(m, seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    f = midi(m)
    out = np.sin(2 * np.pi * f * t) + 0.45 * np.sin(4 * np.pi * f * t) + 0.18 * np.sin(6 * np.pi * f * t)
    return np.tanh(out * 1.3) * adsr(n, 0.004, 0.09, 0.55, 0.04)


def taiko(size=1.0):
    n = int(1.4 * SR)
    t = np.arange(n) / SR
    freq = (52 + 75 * np.exp(-t * 26)) * (1.25 - 0.25 * size)
    body = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t * 4.2)
    skin = band(rng.normal(0, 1, n), 120, 2400) * np.exp(-t * 34) * 0.55
    return np.tanh((body * 1.5 + skin) * 1.1)


def snare():
    n = int(0.32 * SR)
    t = np.arange(n) / SR
    return band(rng.normal(0, 1, n), 700, 7500) * np.exp(-t * 17) * 0.8 + np.sin(2 * np.pi * 185 * t) * np.exp(-t * 26) * 0.6


def tick():
    n = int(0.05 * SR)
    t = np.arange(n) / SR
    return band(rng.normal(0, 1, n), 6500, 16000) * np.exp(-t * 95)


def whistle(notes):
    """A tin whistle over several notes, with a slide into each, breath, and vibrato that opens up on the long ones. Notes are (beat, midi, beats)."""
    end = max(b + d for b, m, d in notes)
    n = int((end * BEAT + 0.5) * SR)
    pitch = np.zeros(n)
    amp = np.zeros(n)
    for b, m, d in notes:
        a, z = int(b * BEAT * SR), int((b + d) * BEAT * SR)
        pitch[a : z] = m
        length = z - a
        rise, fall = int(0.018 * SR), int(0.05 * SR)
        e = np.ones(length)
        e[:rise] = np.linspace(0.25, 1, rise)
        e[-fall:] = np.linspace(1, 0.45, fall)
        amp[a : z] = e
        vib = np.minimum(1, np.arange(length) / SR / 0.45) * (d >= 1.5)
        pitch[a : z] += 0.16 * vib * np.sin(2 * np.pi * 5.4 * np.arange(length) / SR)
    last = int(end * BEAT * SR)
    pitch[last:] = pitch[last - 1]
    amp[last:] = np.linspace(amp[last - 1], 0, n - last)
    # a little glide between notes
    k = np.ones(int(0.012 * SR)) / int(0.012 * SR)
    pitch = np.convolve(pitch, k, mode="same")
    amp = np.convolve(amp, np.ones(int(0.004 * SR)) / int(0.004 * SR), mode="same")
    phase = 2 * np.pi * np.cumsum(midi(pitch)) / SR
    tone = np.sin(phase) + 0.26 * np.sin(2 * phase) + 0.08 * np.sin(3 * phase)
    breath = band(rng.normal(0, 1, n), 2000, 9000) * 0.05
    return (tone + breath) * amp * 0.5


def boom(seconds=4.0):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    low = np.sin(2 * np.pi * np.cumsum(30 + 62 * np.exp(-t * 7)) / SR) * np.exp(-t * 1.25)
    crash = band(rng.normal(0, 1, n), 300, 9000) * np.exp(-t * 3.4) * 0.35
    return np.tanh((low * 1.6 + crash) * 1.2)


def riser(seconds):
    n = int(seconds * SR)
    u = np.arange(n) / n
    noise = rng.normal(0, 1, n)
    out = np.zeros(n)
    edges = np.geomspace(250, 9000, 12)
    for i in range(11):
        seg = band(noise, edges[i], edges[i + 1])
        out += seg * np.exp(-((u - (i + 1) / 12) ** 2) / 0.012)
    return out * u ** 2 * 0.7


# ------------------------------------------------------------------------------------------- the score
# D minor, the same four chords that the theme sits on: two bars each
CHORDS = [
    ([50, 57, 60, 65, 69], 38, [50, 57, 62, 65, 69, 74, 69, 65]),     # D minor
    ([46, 53, 58, 62, 65], 34, [46, 53, 58, 62, 65, 70, 65, 62]),     # B flat
    ([53, 60, 65, 69, 72], 41, [41, 48, 53, 57, 60, 65, 60, 57]),     # F
    ([48, 55, 60, 64, 67], 36, [48, 55, 60, 64, 67, 72, 67, 64]),     # C
]
# the tune, eight bars long, as (bar, beat, note, beats)
THEME = [
    (0, 0, 69, 1.5), (0, 1.5, 72, .5), (0, 2, 74, 2),
    (1, 0, 72, .5), (1, .5, 69, .5), (1, 1, 67, 1), (1, 2, 69, 2),
    (2, 0, 65, 1), (2, 1, 67, .5), (2, 1.5, 69, .5), (2, 2, 72, 1), (2, 3, 69, 1),
    (3, 0, 67, 1.5), (3, 1.5, 65, .5), (3, 2, 62, 2),
    (4, 0, 74, 1.5), (4, 1.5, 76, .5), (4, 2, 77, 1), (4, 3, 76, .5), (4, 3.5, 74, .5),
    (5, 0, 72, 1), (5, 1, 74, .5), (5, 1.5, 72, .5), (5, 2, 69, 2),
    (6, 0, 67, .5), (6, .5, 69, .5), (6, 1, 72, 1), (6, 2, 74, 1), (6, 3, 72, .5), (6, 3.5, 69, .5),
    (7, 0, 74, 3.5),
]


def seconds(bar):
    return bar * BAR


# the opening: a drone, then the chords, then the tune alone over them
put(drone(seconds(title_bar + 1.5)), 0, 0.17 * LEVELS["open"][DRONE], 0, 0.5)
put(pad(CHORDS[0][0], seconds(4.2)), 0.2, 0.16 * LEVELS["open"][PAD], 0, 0.6)
put(pad(CHORDS[1][0], seconds(title_bar - 4) + 0.2), seconds(4), 0.17 * LEVELS["open"][PAD], 0, 0.6)
opening = [(b * 4 + beat, m, d) for b, beat, m, d in THEME if b < 4]
opening = [(beat, m, d) for beat, m, d in opening if beat < (title_bar - 1.5) * 4]
if opening:
    put(whistle(opening), seconds(1.5), 0.14, -0.1, 0.75)
for b in np.arange(max(0, title_bar - 3), title_bar, 1.0):
    put(taiko(0.8), seconds(b), 0.28 * (b - (title_bar - 4)) / 4, 0, 0.3)
put(riser(seconds(2)), seconds(title_bar - 2), 0.22, 0, 0.4)

# the hit on the title, and every section after it
put(boom(), seconds(title_bar), 0.85, 0, 0.5)
mbar = 0.0
while title_bar + mbar < final_bar:
    bar = title_bar + mbar
    idx = int(mbar // 2) % 4
    notes, root, arp = CHORDS[idx]
    # pad, two bars at a time
    if int(mbar) % 2 == 0 and mbar == int(mbar):
        lv = level(bar, PAD)
        put(pad(notes, BAR * 2 + 0.5), seconds(bar), 0.19 * lv, 0, 0.55)
        lv = level(bar, DRONE)
        if lv > 0:
            put(drone(BAR * 2 + 1.0), seconds(bar), 0.13 * lv, 0, 0.45)
    # eighths: the harp and the bass
    for i in range(8):
        t = bar + i / 8
        if t >= final_bar:
            break
        lv = level(t, ARP)
        if lv > 0:
            pan = np.sin(i * 0.8) * 0.45
            put(pluck(arp[i] + 12), seconds(t), 0.20 * lv * (1.0 if i % 2 == 0 else 0.72), pan, 0.3)
        lv = level(t, BASS)
        if lv > 0 and i % 2 == 0:
            put(bass(root, BEAT * 0.9), seconds(t), 0.26 * lv, 0, 0.03)
        lv = level(t, TICK)
        if lv > 0:
            put(tick(), seconds(t), 0.05 * lv * (1.0 if i % 2 == 1 else 0.6), 0.25 if i % 2 else -0.25, 0.06)
    # drums: a big hit on the one, an answer before the three, and a run into every fourth bar
    lv = level(bar, TAIKO)
    if lv > 0:
        put(taiko(1.0), seconds(bar), 0.46 * lv, 0, 0.32)
        put(taiko(0.75), seconds(bar + 0.375), 0.26 * lv, -0.3, 0.32)
        put(taiko(0.9), seconds(bar + 0.5), 0.36 * lv, 0.2, 0.32)
        if int(mbar) % 4 == 3:
            for j, off in enumerate((0.625, 0.75, 0.8125, 0.875, 0.9375)):
                put(taiko(0.6 + j * 0.07), seconds(bar + off), (0.2 + j * 0.05) * lv, -0.4 + j * 0.2, 0.32)
    lv = level(bar, SNARE)
    if lv > 0:
        put(snare(), seconds(bar + 0.25), 0.17 * lv, 0.08, 0.28)
        put(snare(), seconds(bar + 0.75), 0.17 * lv, -0.08, 0.28)
    mbar += 1.0

# the tune comes round every eight bars wherever the section wants it, an octave up at the end
mbar = 0.0
while title_bar + mbar < final_bar:
    bar = title_bar + mbar
    lv = level(bar, LEAD)
    if lv > 0:
        section_up = 12 if section_at(bar) == "hero" else 0
        notes = [(b * 4 + beat, m + section_up, d) for b, beat, m, d in THEME if title_bar + mbar + b + beat / 4 < final_bar]
        if notes:
            put(whistle(notes), seconds(bar), 0.2 * lv, 0.05, 0.5)
            if section_at(bar) in ("missions", "hero"):
                put(whistle([(a, m - 12, d) for a, m, d in notes]), seconds(bar), 0.09 * lv, -0.2, 0.5)
    mbar += 8.0

# hits on the names of the aircraft, as each one cuts in
for name, part, start, length in shots:
    if part == "aircraft" and not name.startswith("paint"):
        put(boom(1.6), seconds(start), 0.3, 0, 0.45)
    if part == "missions":
        put(taiko(1.1), seconds(start), 0.3, 0, 0.35)

# the build through VR into the last run
vr = [s for s in shots if s[1] == "vr"]
if vr:
    put(riser(seconds(2.5)), seconds(hero_bar - 2.5), 0.28, 0, 0.45)
put(boom(), seconds(hero_bar), 0.7, 0, 0.5)

# the last chord, ringing away under the end card
put(boom(6.0), seconds(final_bar), 0.95, 0, 0.6)
ring = int((total_bars - final_bar) * BAR * SR + SR)
card = np.zeros(ring)
for m in [38, 50, 57, 62, 65, 69, 76]:
    card += saw(midi(m), ring, 7, 0.002, 0.03) / 7
card = band(card, 60, 2800) * adsr(ring, 0.02, 0.4, 0.7, ring / SR * 0.75)
put(card, seconds(final_bar), 0.55, 0, 0.8)
put(pluck(86, 3.0), seconds(final_bar), 0.3, -0.2, 0.9)
put(pluck(81, 3.0), seconds(final_bar) + 0.25, 0.22, 0.2, 0.9)

# --------------------------------------------------------------------------------- reverb on the wet bus
print("reverb")
ir_len = int(2.6 * SR)
tail = np.arange(ir_len) / SR
pre = int(0.024 * SR)
for ch in range(2):
    ir = band(rng.normal(0, 1, ir_len), 180, 6000) * np.exp(-tail * 2.5)
    ir[:pre] = 0
    ir /= np.sqrt(np.sum(ir ** 2))
    size = dry.shape[1] + ir_len
    fast = int(2 ** np.ceil(np.log2(size)))
    wet_out = np.fft.irfft(np.fft.rfft(wet[ch], fast) * np.fft.rfft(ir, fast), fast)[: dry.shape[1]]
    dry[ch] += wet_out * 0.9

# ------------------------------------------------------------------------------- the game's own sound
# Recorded by Scripts/VR/TrailerRun.cs from the game's RotorSynth, one frame at a time in step with the picture.
print("the game's own sound")
SAMPLES_PER_FRAME = SR // fps
game = np.zeros((2, N))
at = 0
for part in PARTS:
    inside = [s for s in shots if s[1] == part]
    if not inside:
        continue
    samples = int(round(sum(s[3] for s in inside) * frames_per_bar)) * SAMPLES_PER_FRAME
    path = os.path.join(folder, "frames", part, "game.pcm")
    if os.path.exists(path):
        raw = np.fromfile(path, dtype="<i2").astype(np.float64).reshape(-1, 2).T / 32767
        if raw.shape[1] != samples:
            print(f"  {part}: expected {samples} samples of game sound, found {raw.shape[1]}")
        n = min(raw.shape[1], samples, N - at)
        game[:, at : at + n] = raw[:, :n]
    else:
        print(f"  {part}: no game sound")
    at += samples

# Against the music, shot by shot: how loud the game is (1 is the usual level) and how far the music drops to let it through.
GAME = {
    "cockpit at dusk": (1.2, 0.8), "overhead on the beach": (2.0, 0.55), "title over the lift-off": (1.2, 1.0),
    "over the forest": (1.1, 0.9), "rain": (1.1, 0.85), "rain from the seat": (1.2, 0.75), "rotor spray": (1.4, 0.65), "rotor dust": (1.4, 0.65),
    "Kestrel": (1.5, 0.65), "Northstar": (1.5, 0.65), "Guardian": (1.5, 0.65), "Nightjar": (1.5, 0.65), "Peregrine": (1.5, 0.65),
    "chin window": (1.2, 0.75), "engine start": (2.0, 0.4), "autorotation": (1.5, 0.55), "autopilot": (1.0, 0.8),
    "winch rescue": (1.5, 0.6), "offshore": (1.3, 0.7), "hospital roof": (1.3, 0.75), "road accident": (1.3, 0.75), "confined landing": (1.4, 0.65),
    "VR head": (1.3, 0.7), "VR hands": (1.2, 0.75), "VR switches": (1.1, 0.8), "hero": (1.1, 1.0),
    "over Kinloch": (1.5, 0.9), "castle": (1.5, 0.9), "lighthouse": (1.6, 0.9), "harbour": (1.5, 0.9), "distillery": (1.4, 0.9),
    "platform": (1.5, 0.9), "search": (1.4, 0.85),
}
# Weather and water the game has no sound for, kept from before as quiet beds: shot, rain, sea
BEDS = {
    "coast at dusk": (0, .25), "fishing boat": (0, .4), "platform": (0, .25), "rain": (.6, 0), "rain from the seat": (.7, 0), "sea fog": (0, .2),
    "rotor spray": (0, .3), "flare at night": (0, .2), "winch rescue": (0, .3), "offshore": (0, .2), "hero": (0, .1),
}
LEN = N
t = np.arange(LEN) / SR
first, second = rng.normal(0, 1, LEN), rng.normal(0, 1, LEN)
rain = band(first, 1800, 13000) * (0.75 + 0.25 * np.sin(2 * np.pi * 0.37 * t))
sea = band(second, 120, 2400) * (0.55 + 0.45 * np.sin(2 * np.pi * 0.11 * t) ** 2)
game_level, music_level, rain_level, sea_level = np.ones(LEN), np.ones(LEN), np.zeros(LEN), np.zeros(LEN)
for name, part, start, length in shots:
    a, z = int(seconds(start) * SR), min(int(seconds(start + length) * SR), LEN)
    game_level[a:z], music_level[a:z] = GAME.get(name, (1.0, 1.0))
    rain_level[a:z], sea_level[a:z] = BEDS.get(name, (0, 0))
# one level for the whole recording, so that the usual level sits a little under the music
heard = (game ** 2).mean(axis=0) > 1e-9
game_rms = np.sqrt(np.mean((game ** 2).mean(axis=0)[heard])) if heard.any() else 1.0
game_scale = 10 ** (-23 / 20) / max(game_rms, 1e-6)
print(f"  game sound {20 * np.log10(max(game_rms, 1e-9)):.1f} dB RMS where it plays, on {100 * heard.mean():.0f}% of the film, raised by {20 * np.log10(game_scale):.1f} dB")
# Rotor noise is broad and buries a tune well before it measures louder than it, so no shot goes far over the music.
GAME_OVER_MUSIC_DB = 3.5
for name, part, start, length in shots:
    a, z = int(seconds(start) * SR), min(int(seconds(start + length) * SR), LEN)
    m = np.sqrt(np.mean((dry[:, a:z] * music_level[a:z]) ** 2))
    g = np.sqrt(np.mean((game[:, a:z] * game_scale * game_level[a:z]) ** 2))
    if g > 1e-5 and m > 1e-5:
        over = 20 * np.log10(g / m) - GAME_OVER_MUSIC_DB
        if over > 0:
            game_level[a:z] *= 10 ** (-over / 20)
ramp = np.ones(int(0.09 * SR)) / int(0.09 * SR)
for curve in (game_level, music_level, rain_level, sea_level):
    curve[:] = np.convolve(curve, ramp, mode="same")
music_alone = dry[:, :N] * music_level
game_alone = game * game_scale * game_level
print("  shot, music, game, game against music (dB)")
for name, part, start, length in shots:
    a, z = int(seconds(start) * SR), min(int(seconds(start + length) * SR), LEN)
    m, g = np.sqrt(np.mean(music_alone[:, a:z] ** 2)), np.sqrt(np.mean(game_alone[:, a:z] ** 2))
    if g > 1e-5:
        print(f"    {name:<26}{20 * np.log10(m + 1e-9):7.1f}{20 * np.log10(g):7.1f}{20 * np.log10(g / (m + 1e-9)):7.1f}")
dry = music_alone + game_alone
dry[0] += rain * rain_level * 0.12 + sea * sea_level * 0.2
dry[1] += np.roll(rain, 480) * rain_level * 0.12 + np.roll(sea, 480) * sea_level * 0.2

# ------------------------------------------------------------------------------------------- finishing
mix = dry[:, :N]
fade = min(int(0.6 * SR), N)
mix[:, -fade:] *= np.linspace(1, 0, fade)
mix[:, :int(0.05 * SR)] *= np.linspace(0, 1, int(0.05 * SR))
# slow levelling, so the quiet sections sit up with the loud ones without losing the shape of the film: half the difference
# from the target, over about half a second
window = int(0.5 * SR)
power = np.convolve((mix ** 2).mean(axis=0), np.ones(window) / window, mode="same")
target = 10 ** (-20 / 20)
level_gain = np.clip((target / np.sqrt(power + 1e-7)) ** 0.5, 0.75, 1.7)
level_gain = np.convolve(level_gain, np.ones(window) / window, mode="same")
level_gain[: int(seconds(2) * SR)] = np.minimum(level_gain[: int(seconds(2) * SR)], 1.0)   # the opening stays quiet
mix = mix * level_gain
peak_before = np.abs(mix).max()
drive = 2.1
mix = np.tanh(mix / peak_before * drive) / np.tanh(drive) * 0.89     # limiting, peaks 1 dB below full

with wave.open(os.path.join(folder, "music.wav"), "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((np.clip(mix.T, -1, 1) * 32767).astype("<i2").tobytes())


def db(x):
    return 20 * np.log10(max(x, 1e-9))


print(f"peak before limiting {peak_before:.2f}, after {np.abs(mix).max():.3f}; whole mix {db(np.sqrt(np.mean(mix ** 2))):.1f} dB RMS")
for name in ["open", "world", "weather", "aircraft", "cockpit", "missions", "career", "vr", "end"]:
    inside = [s for s in shots if s[1] == name]
    if inside:
        a, z = int(seconds(inside[0][2]) * SR), int(seconds(inside[-1][2] + inside[-1][3]) * SR)
        seg = mix[:, a:z]
        print(f"  {name:<10} {seconds(inside[0][2]):6.1f} s  {db(np.sqrt(np.mean(seg ** 2))):6.1f} dB RMS  peak {np.abs(seg).max():.2f}")
print(f"left-right balance {db(np.sqrt(np.mean(mix[0] ** 2))) - db(np.sqrt(np.mean(mix[1] ** 2))):+.2f} dB")

# ------------------------------------------------------------------------------- a picture of it to check
W, HW, HS = 1600, 160, 360
mono = mix.mean(axis=0)
img = np.zeros((HW + HS, W, 3), dtype=np.uint8) + 16
hop = len(mono) // W
for x in range(W):
    seg = mono[x * hop : (x + 1) * hop]
    if len(seg) == 0:
        continue
    pk, rms = np.abs(seg).max(), np.sqrt(np.mean(seg ** 2))
    y0 = HW // 2
    img[int(y0 - pk * y0) : int(y0 + pk * y0) + 1, x] = (70, 130, 180)
    img[int(y0 - rms * y0) : int(y0 + rms * y0) + 1, x] = (160, 220, 250)
win = 4096
window = np.hanning(win)
freqs = np.fft.rfftfreq(win, 1 / SR)
rows = np.geomspace(30, 16000, HS)
for x in range(W):
    a = min(x * hop, len(mono) - win)
    mag = np.abs(np.fft.rfft(mono[a : a + win] * window))
    col = np.interp(rows, freqs, 20 * np.log10(mag + 1e-6))
    v = np.clip((col + 25) / 75, 0, 1)
    img[HW + HS - 1 - np.arange(HS), x, 0] = (v ** 1.2 * 255).astype(np.uint8)
    img[HW + HS - 1 - np.arange(HS), x, 1] = (v ** 2 * 230).astype(np.uint8)
    img[HW + HS - 1 - np.arange(HS), x, 2] = (v ** 3 * 200 + (1 - v) * 30).astype(np.uint8)
# a white line at the start of every part
seen = set()
for name, part, start, length in shots:
    if part not in seen:
        seen.add(part)
        img[:, min(W - 1, int(start / total_bars * W))] = (255, 255, 255)


def png(path, a):
    h, w, _ = a.shape
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


png(os.path.join(folder, "music-check.png"), img)
print("written", os.path.join(folder, "music.wav"))
