// What the rest of the game knows about VR. The VR code itself lives in its own assembly (Rotorcraft.VR), which is
// only built for Windows, so the web build carries none of it: there Driver stays null and VR is simply absent.
using System;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public interface IVrDriver
    {
        bool Running { get; }
        /// <summary>Starting up, which can take a few seconds.</summary>
        bool Starting { get; }
        /// <summary>Why the last start failed, in words for the player; null if it did not.</summary>
        string Problem { get; }
        void Begin();
        void End();
        /// <summary>Make where the head is now the seat position, looking ahead.</summary>
        void Recentre();
        /// <summary>
        /// Put the player's seat here, looking along view. Level keeps the horizon level whatever the view's
        /// pitch and roll (everywhere but the cockpit, where the seat moves with the aircraft).
        /// </summary>
        void Place(Vector3 eye, Quaternion view, bool level);
        /// <summary>Where a point in the world appears on the UI panel from where the head is now, in the panel's layout units; false if it is not on the panel.</summary>
        bool PanelPoint(Vector3 world, out Vector2 at);
    }

    public static class Vr
    {
        public static IVrDriver Driver;
        /// <summary>This build can do VR.</summary>
        public static bool Available => Driver != null;
        public static bool On => Driver != null && Driver.Running;
        /// <summary>VR started, stopped or failed to start.</summary>
        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();
    }
}
