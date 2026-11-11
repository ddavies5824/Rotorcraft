// Settings and versioned local saves (port of src/game/save.ts, freeplayStore.ts and the company storage). Files
// live in Application.persistentDataPath as JSON, written atomically (temporary file, then replace) with the
// previous copy kept as .bak; a save that cannot be read is moved aside rather than destroyed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation.UI
{
    public enum GraphicsQuality { High, Balanced, Low }
    public enum CameraPref { Chase, Cockpit }

    public sealed class GameSettings
    {
        public AssistMode assist = AssistMode.Beginner;
        public ControlSettings controls = new ControlSettings();
        /// <summary>Pick the controller layout from the connected device (T.Flight Stick X, gamepad) instead of controls.controller.</summary>
        public bool autoController = true;
        public bool showTrainingAids = true;   // AGL readout, target marker, landing ring
        public bool sound = true;
        public float volume = 0.6f;
        public CameraPref camera = CameraPref.Chase;
        public float textScale = 1;            // 0.9..1.3
        public GraphicsQuality graphics = GraphicsQuality.High;
        public bool fullscreen = true;
        /// <summary>On-screen sticks with the collective on the right and the cyclic on the left (the browser game's "swapped" touch layout).</summary>
        public bool touchSwapped;
        /// <summary>The refresh rate to ask a headset for, in Hz; 0 chooses one the game can hold.</summary>
        public int vrRefreshHz = 0;
        /// <summary>Mechanics the player has already had explained (first-time hints).</summary>
        public List<string> seenHints = new List<string>();

        public GameSettings Clone()
        {
            var s = (GameSettings)MemberwiseClone();
            s.controls = controls.Clone();
            s.seenHints = new List<string>(seenHints);
            return s;
        }
    }

    public enum LoadError { None, Missing, Corrupt, Unavailable }

    public sealed class SaveManager
    {
        readonly string dir;
        public bool Available { get; }

        public SaveManager(string directory = null)
        {
            dir = directory ?? Path.Combine(Application.persistentDataPath, "Saves");
            try { Directory.CreateDirectory(dir); Available = true; }
            catch (Exception e) { Debug.LogWarning($"Saves unavailable: {e.Message}"); Available = false; }
        }

        public string PathOf(string name) => Path.Combine(dir, name + ".json");
        public string Folder => dir;

        bool WriteFile(string name, string json)
        {
            if (!Available) return false;
            try
            {
                var path = PathOf(name);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                WebStorage.Flush();
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"Saving {name} failed: {e.Message}"); return false; }
        }

        string ReadFile(string name)
        {
            var path = PathOf(name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        void Quarantine(string name)
        {
            try
            {
                var path = PathOf(name);
                if (File.Exists(path)) File.Move(path, Path.Combine(dir, $"{name}.corrupt.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json"));
            }
            catch { /* leave it */ }
        }

        // ------------------------------------------------------------------------------------------ career
        public (Career career, LoadError error) LoadCareer()
        {
            if (!Available) return (null, LoadError.Unavailable);
            string raw;
            try { raw = ReadFile("career"); } catch { return (null, LoadError.Unavailable); }
            if (raw == null) return (null, LoadError.Missing);
            try
            {
                var c = SaveData.ValidateCareer(SaveData.Read<Career>(raw));
                if (c == null) { Quarantine("career"); return (null, LoadError.Corrupt); }
                return (c, LoadError.None);
            }
            catch { Quarantine("career"); return (null, LoadError.Corrupt); }
        }

        public bool SaveCareer(Career c) => WriteFile("career", SaveData.Write(c));
        public void DeleteCareer() { try { var p = PathOf("career"); if (File.Exists(p)) { File.Copy(p, p + ".deleted", true); File.Delete(p); } } catch { } }

        public Career ImportCareer(string path)
        {
            try { return SaveData.ValidateCareer(SaveData.Read<Career>(File.ReadAllText(path))); }
            catch { return null; }
        }

        public string ExportCareer(Career c)
        {
            var safe = new string(c.pilotName.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_').ToArray());
            var path = Path.Combine(dir, $"rotorcraft-{safe}-{DateTime.Now:yyyyMMdd-HHmm}.json");
            File.WriteAllText(path, SaveData.Write(c));
            return path;
        }

        // ---------------------------------------------------------------------------------------- settings
        public GameSettings LoadSettings()
        {
            try
            {
                var raw = ReadFile("settings");
                if (raw == null) return new GameSettings();
                var s = SaveData.Read<GameSettings>(raw) ?? new GameSettings();
                // clamp everything a hand-edited file could break
                s.controls = s.controls ?? new ControlSettings();
                s.controls.cyclicSensitivity = Math.Min(1.6, Math.Max(0.4, s.controls.cyclicSensitivity));
                s.controls.pedalSensitivity = Math.Min(1.6, Math.Max(0.4, s.controls.pedalSensitivity));
                s.controls.deadzone = Math.Min(0.3, Math.Max(0, s.controls.deadzone));
                s.controls.controller = s.controls.controller ?? Controllers.PresetGamepad();
                var keys = ControlSettings.DefaultKeys();
                foreach (var k in keys.Keys.ToList()) if (s.controls.keyBindings != null && s.controls.keyBindings.TryGetValue(k, out var v)) keys[k] = v;
                s.controls.keyBindings = keys;
                s.volume = Mathf.Clamp01(s.volume);
                s.textScale = Mathf.Clamp(s.textScale, 0.9f, 1.3f);
                s.seenHints = (s.seenHints ?? new List<string>()).Take(100).ToList();
                return s;
            }
            catch { return new GameSettings(); }
        }

        public void SaveSettings(GameSettings s) => WriteFile("settings", SaveData.Write(s));

        // ---------------------------------------------------------------------------------------- freeplay
        public FreeplayRecord LoadFreeplay()
        {
            try { var raw = ReadFile("freeplay"); return raw == null ? new FreeplayRecord() : SaveData.ValidateFreeplay(SaveData.Read<FreeplayRecord>(raw)); }
            catch { return new FreeplayRecord(); }
        }
        public void SaveFreeplay(FreeplayRecord r) => WriteFile("freeplay", SaveData.Write(r));

        // ----------------------------------------------------------------------------------------- company
        public Company LoadCompany()
        {
            try { var raw = ReadFile("company"); return raw == null ? null : SaveData.ValidateCompany(SaveData.Read<Company>(raw)); }
            catch { Quarantine("company"); return null; }
        }
        public void SaveCompany(Company c)
        {
            if (c != null) WriteFile("company", SaveData.Write(c));
            else try { var p = PathOf("company"); if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
