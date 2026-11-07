// World-sound recordings for the Windows build: Sounds/*.ogg beside the game (Rotorcraft_Data/Sounds, or the project's
// Sounds folder in the editor), made by Tools/ambient_recordings.py from CC0 recordings. They are read once at start, made into
// seamless loops and handed to AmbientSynth, which plays each in place of the generated layer it stands in for. The web build
// never reads them and keeps the generated sounds, so its download stays the same.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Rotorcraft.Presentation
{
    /// <summary>One recording as 16-bit samples (interleaved when stereo), ready to play.</summary>
    public sealed class Recording
    {
        public string layer;
        /// <summary>Played as a bout now and then (an owl), not looped the whole time.</summary>
        public bool calls;
        public short[] pcm;
        public int channels, frames, rate;
        /// <summary>Brings it to the level of the generated layer it replaces, at that layer's full strength.</summary>
        public float gain;

        /// <summary>
        /// A loop from samples that run fadeFrames past the loop's length: the overrun is crossfaded into the start (equal power,
        /// as the two stretches are unrelated), so the last sample runs straight on into the first.
        /// </summary>
        public static short[] MakeLoop(float[] x, int channels, int fadeFrames, out int frames)
        {
            var total = x.Length / channels;
            fadeFrames = Math.Max(0, Math.Min(fadeFrames, total / 2));
            frames = total - fadeFrames;
            var pcm = new short[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                double a = 1, b = 0;
                if (f < fadeFrames) { var u = (f + 0.5) / fadeFrames * Math.PI / 2; a = Math.Sin(u); b = Math.Cos(u); }
                for (int c = 0; c < channels; c++)
                {
                    var v = x[f * channels + c] * a + (b > 0 ? x[(frames + f) * channels + c] * b : 0);
                    pcm[f * channels + c] = (short)Math.Max(-32767, Math.Min(32767, Math.Round(v * 32767)));
                }
            }
            return pcm;
        }

        /// <summary>A recording made from samples already in hand (tests, and anything generated).</summary>
        public static Recording From(string layer, float[] x, int channels, int rate, float rms, float fadeS = 0, bool calls = false)
        {
            var pcm = MakeLoop(x, channels, (int)(fadeS * rate), out var frames);
            return new Recording { layer = layer, calls = calls, pcm = pcm, frames = frames, channels = channels, rate = rate, gain = AmbientSynth.TargetRms(layer) / Math.Max(1e-4f, rms) };
        }
    }

    public static class AmbientRecordings
    {
        static readonly Dictionary<string, Recording> ready = new Dictionary<string, Recording>();
        static bool started;

        public static int Count { get { lock (ready) return ready.Count; } }
        /// <summary>The recording standing in for a layer, or null (none, not read yet, or the web build).</summary>
        public static Recording Get(string layer) { lock (ready) return ready.TryGetValue(layer, out var r) ? r : null; }

        public static string Folder => Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Sounds")
            : Path.Combine(Application.dataPath, "Sounds");

        /// <summary>Start reading the recordings, once (Windows and the editor's play mode; never on the web).</summary>
        public static void EnsureLoading()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (started || !Application.isPlaying) return;
            started = true;
            if (!File.Exists(Path.Combine(Folder, "sounds.json"))) return;
            var go = new GameObject("Ambient recordings") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().StartCoroutine(Load(Folder, go));
#endif
        }

        sealed class Runner : MonoBehaviour { }

        [Serializable] class Manifest { public int rate; public Entry[] sounds; }
        [Serializable] class Entry { public string layer, file, kind; public float rms, fade, seconds; }

        static void Discard(UnityEngine.Object o) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }

        /// <summary>Read every recording the list names, one after another. Yields only nulls, so edit-mode tests can run it too.</summary>
        public static IEnumerator Load(string folder, GameObject owner = null)
        {
            Manifest m = null;
            try { m = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(folder, "sounds.json"))); }
            catch (Exception e) { Debug.LogWarning("World-sound recordings not read: " + e.Message); }
            foreach (var s in m?.sounds ?? Array.Empty<Entry>())
            {
                var path = Path.Combine(folder, s.file);
                if (!File.Exists(path)) continue;
                float[] data; int ch, rate;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone) yield return null;
                    if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"{s.file}: {req.error}"); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null) continue;
                    while (clip.loadState == AudioDataLoadState.Loading || clip.loadState == AudioDataLoadState.Unloaded) { if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData(); yield return null; }
                    ch = clip.channels; rate = clip.frequency;
                    data = new float[clip.samples * ch];
                    var ok = clip.loadState == AudioDataLoadState.Loaded && clip.GetData(data, 0);
                    Discard(clip);
                    if (!ok) { Debug.LogWarning($"{s.file}: could not be decoded"); continue; }
                }
                var calls = s.kind == "calls";
                var fade = calls ? 0 : s.fade;
                var job = Task.Run(() => Recording.From(s.layer, data, ch, rate, s.rms, fade, calls));
                while (!job.IsCompleted) yield return null;
                if (job.IsFaulted) { Debug.LogWarning($"{s.file}: {job.Exception?.InnerException?.Message}"); continue; }
                lock (ready) ready[s.layer] = job.Result;
            }
            Debug.Log($"World-sound recordings: {Count} ready from {folder}");
            if (owner != null) Discard(owner);
        }
    }
}
