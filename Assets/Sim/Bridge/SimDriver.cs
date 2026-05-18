using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Unity-side entry point. Owns the SimulationContext + SimThread.
    /// Add this MonoBehaviour to a bootstrap GameObject and the sim starts in Awake.
    ///
    /// Phase 0: ticks an empty system list at the configured rate and publishes
    /// a heartbeat snapshot every tick. Use the on-screen overlay to verify
    /// the sim thread is alive.
    public sealed class SimDriver : MonoBehaviour
    {
        public static SimDriver Instance { get; private set; }

        [SerializeField] float ticksPerSecond = 10f;
        [SerializeField] int seed = 12345;
        [SerializeField] bool showDebugOverlay = true;

        public SimulationContext Context { get; private set; }
        public InputBus Inputs { get; private set; }
        public SnapshotPublisher Snapshots { get; private set; }
        public EventLog EventLog { get; private set; }

        SimThread _thread;
        TickLoop _loop;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            var events = new EventBus();
            var time = new SimulationTime(1.0 / ticksPerSecond);
            var random = new SimRandom(seed);
            Inputs = new InputBus();
            Snapshots = new SnapshotPublisher();
            Context = new SimulationContext(events, time, random, Inputs);

            _loop = new TickLoop(Context);
            // Phase 3: first real systems. Order matters — TimeSystem emits
            // events EventLog wants to record, so TimeSystem registers first.
            _loop.Register(new TimeSystem());
            EventLog = new EventLog();
            _loop.Register(EventLog);

            _thread = new SimThread(_loop, Context, Snapshots);
            _thread.Start();
            Debug.Log("[SimDriver] sim thread started at " + ticksPerSecond + " Hz (seed=" + seed + ")");

            // Bridge MonoBehaviours — attached to this GameObject so they share its lifetime.
            gameObject.AddComponent<SimRegistrar>();
            gameObject.AddComponent<WorldClockMirror>();
            gameObject.AddComponent<SimInspector>();
            gameObject.AddComponent<SimHUD>();
        }

        void Update()
        {
            if (_thread != null && _thread.LastException != null)
            {
                Debug.LogError("[SimDriver] sim thread crashed: " + _thread.LastException);
                _thread = null;
            }
        }

        void OnDestroy()
        {
            _thread?.Stop();
            if (Instance == this) Instance = null;
        }

        void OnGUI()
        {
            if (!showDebugOverlay) return;
            var snap = Snapshots?.Latest;
            if (snap == null) return;
            var status = _thread != null && _thread.IsRunning ? "running" : "stopped";
            GUI.Label(new Rect(10, 10, 500, 20),
                "sim: " + status + " · tick " + snap.Tick + " · t=" + snap.SimSeconds.ToString("F1") + "s · last=" + (_thread?.LastTickMs ?? 0) + "ms");
        }
    }
}
