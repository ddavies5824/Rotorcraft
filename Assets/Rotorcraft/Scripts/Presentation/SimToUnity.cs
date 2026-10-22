// The sim's world is right-handed: x east, y up, z south (north is -z). Unity is left-handed with z forward,
// so north becomes +z: (x, y, z) -> (x, y, -z).
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public static class SimToUnity
    {
        public static Vector3 Position(Vec3 v) => new Vector3((float)v.x, (float)v.y, (float)-v.z);
        public static Vector3 Direction(Vec3 v) => new Vector3((float)v.x, (float)v.y, (float)-v.z);

        /// <summary>The aircraft's attitude as a Unity rotation whose forward is the nose and whose up is the rotor mast.</summary>
        public static Quaternion Attitude(HeliState s)
        {
            var a = SimMath.BodyAxesOf(s.psi, s.theta, s.phi);
            return Quaternion.LookRotation(Direction(a.f), Direction(a.u));
        }
    }
}
