// The real game's screenshot tour (ShotTour) run in the editor: every menu screen, a freeplay shift with the HUD and its
// panels, a career exercise with the walk-round, each aircraft inside and out, weather and effects, life in the world
// and the markings. Explicit: the editor must be started with "-shots <folder>" (bench.ps1 tour), which also gives the
// tour its own saves in that folder and keeps it off the player's cloud account. Nothing opens on the desktop.
using System.Collections;
using NUnit.Framework;
using Rotorcraft.Presentation.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Rotorcraft.Preview.Tests
{
    [Explicit("Takes many minutes; run on purpose with bench.ps1 tour")]
    public sealed class GameTour
    {
        [UnityTest, Timeout(3600000)]
        public IEnumerator Run()
        {
            LogAssert.ignoreFailingMessages = true;
            var folder = ShotTour.ArgFolder();
            Assert.IsNotNull(folder, "start the editor with -shots <folder> (bench.ps1 tour)");
            ShotTour.Done = false;
#if UNITY_EDITOR
            // no screen in batch mode: the game's panel draws into a texture, given before the panel is made (in memory only)
            var ps = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset");
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "tour UI" };
            rt.Create();
            ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = new Color(0, 0, 0, 0);
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Game.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            var t0 = Time.realtimeSinceStartup;
            while (!ShotTour.Done && Time.realtimeSinceStartup - t0 < 3300) yield return null;
#if UNITY_EDITOR
            ps.targetTexture = null; ps.clearColor = false;
#endif
            Assert.IsTrue(ShotTour.Done, "the tour finished");
        }
    }
}
