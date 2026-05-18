using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Unity-side bridge for main-thread-only physics APIs. Phase 0 placeholder.
    ///
    /// Phase 4+: sim emits RaycastQuery / SweepQuery events into a thread-safe
    /// queue; this MonoBehaviour drains them in FixedUpdate, runs Physics.Raycast /
    /// CharacterController.Move on the main thread, posts results back via the
    /// CollisionContextRegistry (single-writer = main thread for those fields).
    ///
    /// Phase 5: sim grows its own collision layer + geometry extraction; this
    /// bridge shrinks toward zero responsibilities and eventually deletes.
    public sealed class PhysicsBridge : MonoBehaviour
    {
        void FixedUpdate()
        {
            // No-op until Phase 4.
        }
    }
}
