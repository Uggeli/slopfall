using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Unity-side input capture. Reads keyboard / mouse / gamepad each frame and
    /// enqueues intent events on SimDriver.Inputs. Phase 0 placeholder.
    ///
    /// Phase 4+: define MoveIntent, JumpIntent, AttackIntent, CastIntent, etc.
    /// (each implementing ISimEvent), construct from Unity input here, enqueue
    /// via SimDriver.Instance.Inputs.Enqueue(intent).
    public sealed class InputCapture : MonoBehaviour
    {
        void Update()
        {
            var driver = SimDriver.Instance;
            if (driver == null) return;
            // No-op until Phase 4.
        }
    }
}
