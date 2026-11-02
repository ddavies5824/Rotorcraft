// Times the pieces of the world build one at a time (single thread), to see what a browser has to chew through.
using System;
using System.Diagnostics;
using System.Reflection;
using Rotorcraft.Presentation;
using Rotorcraft.World;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rotorcraft.EditorTools
{
    public static class WebLoadProbe
    {
        public static void Run()
        {
            var T = typeof(KinlochTerrain);
            var sw = Stopwatch.StartNew();
            void Lap(string what) { Debug.Log($"PROBE {what}: {sw.Elapsed.TotalSeconds:F2} s"); sw.Restart(); }
            Rotorcraft.Sim.Ground.Current = new WorldGround(); Lap("ground");
            T.GetMethod("WarmUp", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); Lap("warm-up");
            var tiles = (System.Collections.IList)T.GetMethod("Plan", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); Lap($"plan ({tiles.Count} tiles)");
            var tileType = T.GetNestedType("Tile", BindingFlags.NonPublic);
            var compute = T.GetMethod("Compute", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var (res, alpha) in new[] { (257, 256), (129, 64), (1025, 512) })
            {
                var t = Activator.CreateInstance(tileType, true);
                tileType.GetField("x0").SetValue(t, -2048); tileType.GetField("z0").SetValue(t, -2048);
                tileType.GetField("size").SetValue(t, 2048); tileType.GetField("res").SetValue(t, res); tileType.GetField("alphaRes").SetValue(t, alpha);
                compute.Invoke(null, new[] { t }); Lap($"one core tile at {res}/{alpha}");
            }
            for (int i = 0; i < 200000; i++) WorldMap.TerrainHeight(-3000 + i * 0.01, -2000); Lap("200k TerrainHeight");
            for (int i = 0; i < 20000; i++) FieldLayout.FieldAt(-3000 + i * 0.1, -2000); Lap("20k FieldAt");
        }
    }
}
