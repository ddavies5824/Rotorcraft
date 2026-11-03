// The flyable aircraft models (the imported .glb of each type), looked up by aircraft id.
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class AircraftLibrary : MonoBehaviour
    {
        public GameObject kestrel, northstar, guardian, nightjar, peregrine;

        public GameObject ModelFor(string id)
        {
            switch (id)
            {
                case "northstar": return northstar;
                case "guardian": return guardian;
                case "nightjar": return nightjar;
                case "peregrine": return peregrine;
                default: return kestrel;
            }
        }
    }
}
