using UnityEngine;
using DaggerfallWorkshop.Game.Entity;

namespace DaggerfallWorkshop.Sim
{
    /// Periodically scans the scene for DaggerfallEntityBehaviour components
    /// that don't yet have a SimMirror attached, and attaches one.
    ///
    /// Phase 1 approach: low-frequency FindObjectsOfType scan. Cheap at dungeon
    /// scale (tens of enemies); replace with event-driven hookup once we touch
    /// the DFU spawn paths in later phases.
    public sealed class SimRegistrar : MonoBehaviour
    {
        [SerializeField] float scanIntervalSeconds = 0.5f;
        float _nextScanAt;

        void Update()
        {
            if (Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + scanIntervalSeconds;
            ScanAndAttach();
        }

        void ScanAndAttach()
        {
            var entities = FindObjectsOfType<DaggerfallEntityBehaviour>();
            for (int i = 0; i < entities.Length; i++)
            {
                var beh = entities[i];
                if (beh.GetComponent<SimMirror>() == null)
                    beh.gameObject.AddComponent<SimMirror>();
            }
        }
    }
}
