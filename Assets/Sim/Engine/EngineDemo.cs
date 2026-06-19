using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DaggerfallWorkshop.Sim.Engine.Demo
{
    // A self-contained proof of the CQRS loop (mirrors docs/Architechturesample.cs):
    // a System reads positions read-only and emits move intents; a Registry owns the
    // positions and applies the intents. Run via `Sim.Host --enginedemo`.

    public struct PositionComponent
    {
        public int EntityId;
        public float X, Y;
    }

    public struct MoveEntityEvent : IEvent
    {
        public int EntityId;
        public float DeltaX, DeltaY;
    }

    public sealed class MovementRegistry : Registry
    {
        readonly Dictionary<int, PositionComponent> _storage = new Dictionary<int, PositionComponent>();
        public IReadOnlyDictionary<int, PositionComponent> Components => _storage;

        public MovementRegistry(EventBus events) : base(events)
        {
            _storage[101] = new PositionComponent { EntityId = 101, X = 10f, Y = 0f };
            _storage[102] = new PositionComponent { EntityId = 102, X = 95f, Y = 0f };
        }

        public override void Update(long tick)
        {
            // WRITE phase: sole writer of own storage; in-place is safe (no reader runs now).
            foreach (ref readonly var ev in Events.GetEvents<MoveEntityEvent>())
            {
                if (_storage.TryGetValue(ev.EntityId, out var p))
                {
                    p.X += ev.DeltaX; p.Y += ev.DeltaY;
                    _storage[ev.EntityId] = p;
                    Console.WriteLine($"[Registry] {ev.EntityId} -> X {p.X:F1}");
                }
            }
        }
    }

    public sealed class PatrolSystem : SimSystem
    {
        readonly MovementRegistry _movement;
        public PatrolSystem(EventBus events, MovementRegistry movement) : base(events) => _movement = movement;

        public override void Update(long tick)
        {
            // READ+EMIT phase: read-only over the registry → parallel over entities.
            Parallel.ForEach(_movement.Components, kv =>
            {
                if (kv.Value.X < 100f)
                    Events.Publish(new MoveEntityEvent { EntityId = kv.Key, DeltaX = 5f });
                else
                    Console.WriteLine($"[T{Environment.CurrentManagedThreadId} Patrol] {kv.Key} at boundary");
            });
        }
    }

    public static class EngineDemo
    {
        public static int Run()
        {
            var bus = new EventBus();
            var movement = new MovementRegistry(bus);
            var engine = new SimEngine(bus, new Registry[] { movement }, new SimSystem[] { new PatrolSystem(bus, movement) });

            for (int t = 1; t <= 3; t++)
            {
                Console.WriteLine($"\n--- TICK {t} ---");
                engine.Step();
            }
            Console.WriteLine("\nengine demo ok");
            return 0;
        }
    }
}
