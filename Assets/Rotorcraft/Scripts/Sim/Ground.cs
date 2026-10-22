// What the simulation needs to know about the world beneath it. In the TypeScript game the sim calls
// groundAt / terrainHeight / platformWake in src/world/world.ts directly; here they sit behind IGround so the
// sim has no dependency on the world code. Rotorcraft.World provides the real terrain; tests use FlatGround.

namespace Rotorcraft.Sim
{
    public enum Surface { Grass, Pad, Deck, Roof, Water, Rock }

    public struct GroundInfo
    {
        public double h;
        public Surface surface;
        public double slope;
        public string siteId;   // null when not on a landing site
        public GroundInfo(double h, Surface surface, double slope, string siteId = null) { this.h = h; this.surface = surface; this.slope = slope; this.siteId = siteId; }
    }

    public interface IGround
    {
        /// <summary>Height of whatever the aircraft would contact at (x,z): water, terrain, a pad, a deck or a roof.</summary>
        GroundInfo GroundAt(double x, double z);
        /// <summary>Terrain height including the roads and the flattened landing sites (no structures).</summary>
        double TerrainHeight(double x, double z);
        /// <summary>Turbulence factor (0..1) in the wake of an offshore platform; (windToX, windToZ) is the unit vector the wind blows towards.</summary>
        double PlatformWake(double x, double y, double z, double windToX, double windToZ);
    }

    /// <summary>Level land at one height everywhere: no water, sites or platforms.</summary>
    public sealed class FlatGround : IGround
    {
        public readonly double height;
        public readonly Surface surface;
        public FlatGround(double height = 0, Surface surface = Surface.Grass) { this.height = height; this.surface = surface; }
        public GroundInfo GroundAt(double x, double z) => new GroundInfo(height, surface, 0);
        public double TerrainHeight(double x, double z) => height;
        public double PlatformWake(double x, double y, double z, double windToX, double windToZ) => 0;
    }

    public static class Ground
    {
        /// <summary>The world the simulation flies over. Set once by the game when the world is loaded.</summary>
        public static IGround Current = new FlatGround();
    }
}
