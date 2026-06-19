using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArchitectureSample
{
    // ========================================================================
    // 1. FOUNDATION & EVENT BUS MECHANICS (Zero-Allocation, Double-Buffered)
    // ========================================================================
    
    public interface IEvent { } // Marker Interface

    internal interface ITickBuffer
    {
        void Flip();
    }

    internal class EventBuffer<T> : ITickBuffer where T : struct, IEvent
    {
        private List<T> _incoming = new(1024);
        private List<T> _processing = new(1024);
        private readonly Lock _lock = new();

        public void Enqueue(T @event)
        {
            lock (_lock) // Protects writes during parallel System processing phase
            {
                _incoming.Add(@event);
            }
        }

        public void Flip()
        {
            _processing.Clear();
            var temp = _incoming;
            _incoming = _processing;
            _processing = temp;
        }

        public ReadOnlySpan<T> GetSpan()
        {
            return CollectionsMarshal.AsSpan(_processing); // Direct memory view
        }
    }

    public class EventBus
    {
        private readonly ConcurrentDictionary<Type, object> _buffers = new();
        private readonly List<ITickBuffer> _bufferList = new();
        private readonly Lock _registrationLock = new();

        public void Publish<T>(T @event) where T : struct, IEvent
        {
            var buffer = (EventBuffer<T>)_buffers.GetOrAdd(typeof(T), _ =>
            {
                var newBuffer = new EventBuffer<T>();
                lock (_registrationLock)
                {
                    _bufferList.Add(newBuffer);
                }
                return newBuffer;
            });

            buffer.Enqueue(@event);
        }

        public ReadOnlySpan<T> GetEvents<T>() where T : struct, IEvent
        {
            if (_buffers.TryGetValue(typeof(T), out var bufferObj))
            {
                return ((EventBuffer<T>)bufferObj).GetSpan();
            }
            return ReadOnlySpan<T>.Empty;
        }

        public void Tick()
        {
            for (int i = 0; i < _bufferList.Count; i++)
            {
                _bufferList[i].Flip();
            }
        }
    }

    // ========================================================================
    // 2. BASE ABSTRACTIONS
    // ========================================================================
    
    public abstract class Registry
    {
        public abstract void Update();
    }

    public abstract class System
    {
        protected readonly EventBus EventBus;

        protected System(EventBus eventBus) => EventBus = eventBus;

        public abstract void Update();
    }

    // ========================================================================
    // 3. COMPONENTS, EVENTS & REGISTRIES (Data Storage & Isolation)
    // ========================================================================
    
    public struct PositionComponent
    {
        public int EntityId;
        public float X;
        public float Y;
    }

    // Intent-to-mutate event
    public struct MoveEntityEvent : IEvent
    {
        public int EntityId;
        public float DeltaX;
        public float DeltaY;
    }

    public class MovementRegistry : Registry
    {
        private readonly EventBus _eventBus;
        
        // Internal mutable storage, public read-only layout
        private readonly Dictionary<int, PositionComponent> _storage = new();
        public IReadOnlyDictionary<int, PositionComponent> Components => _storage;

        public MovementRegistry(EventBus eventBus)
        {
            _eventBus = eventBus;
            
            // Seed sample entity data
            _storage[101] = new PositionComponent { EntityId = 101, X = 10f, Y = 0f };
            _storage[102] = new PositionComponent { EntityId = 102, X = 95f, Y = 0f };
        }

        public override void Update()
        {
            // Pull events safely from previous tick
            ReadOnlySpan<MoveEntityEvent> moves = _eventBus.GetEvents<MoveEntityEvent>();

            // Phase 2 is single-threaded per registry: data can be altered safely here
            for (int i = 0; i < moves.Length; i++)
            {
                ref readonly var ev = ref moves[i];
                if (_storage.TryGetValue(ev.EntityId, out var pos))
                {
                    pos.X += ev.DeltaX;
                    pos.Y += ev.DeltaY;
                    _storage[ev.EntityId] = pos;
                    Console.WriteLine($"[Registry Update] Entity {ev.EntityId} written to position X: {pos.X:F1}");
                }
            }
        }
    }

    // ========================================================================
    // 4. PURE LOGIC SYSTEMS (Read State in Parallel -> Publish Intents)
    // ========================================================================
    
    public class PatrolSystem : System
    {
        private readonly MovementRegistry _movementData;

        public PatrolSystem(EventBus eventBus, MovementRegistry movementData) : base(eventBus)
        {
            _movementData = movementData;
        }

        public override void Update()
        {
            // Parallel system read phase. Because _movementData.Components is read-only here, 
            // multiple systems can process this data loop across different threads concurrently.
            Parallel.ForEach(_movementData.Components, pair =>
            {
                int entityId = pair.Key;
                PositionComponent pos = pair.Value;
                int threadId = Environment.CurrentManagedThreadId;

                if (pos.X < 100f)
                {
                    Console.WriteLine($"[Thread {threadId} -> PatrolSystem] Entity {entityId} sees path forward. Emitting movement intent event.");
                    
                    EventBus.Publish(new MoveEntityEvent
                    {
                        EntityId = entityId,
                        DeltaX = 5.0f,
                        DeltaY = 0f
                    });
                }
                else
                {
                    Console.WriteLine($"[Thread {threadId} -> PatrolSystem] Entity {entityId} hit patrol boundary limit.");
                }
            });
        }
    }

    // ========================================================================
    // 5. ENGINE LOOP & RUNNER
    // ========================================================================
    
    class Program
    {
        static void Main(string[] args)
        {
            EventBus eventBus = new EventBus();
            List<Registry> registries = new List<Registry>();
            List<System> systems = new List<System>();

            // Wire up dependencies
            var movementRegistry = new MovementRegistry(eventBus);
            registries.Add(movementRegistry);

            var patrolSystem = new PatrolSystem(eventBus, movementRegistry);
            systems.Add(patrolSystem);

            // Simulation execution loop
            for (int tick = 1; tick <= 3; tick++)
            {
                Console.WriteLine($"\n--- STARTING TICK {tick} ---");

                // STEP 1: Flip the Event Bus Buffers
                // Incoming bucket from last tick now becomes active processing batch
                eventBus.Tick();

                // STEP 2: Parallel Registry Updates (Write Phase)
                // Processes data mutations based on accumulated previous-tick events
                Parallel.For(0, registries.Count, i =>
                {
                    registries[i].Update();
                });

                // STEP 3: Parallel System Processing (Read & Emit Phase)
                // Pure logic evaluations based on newly updated registry values. Safely scales to all CPU cores.
                Parallel.For(0, systems.Count, i =>
                {
                    systems[i].Update();
                });
            }
        }
    }
}