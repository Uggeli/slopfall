# Kind-driven sprites + yaw facing + activity animations — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render town3d actors with kind-appropriate Daggerfall sprites (monsters = curated creature pool, guards = City Watch archive 399, civilians = existing), face them by the sim's `yaw`, and play walk/idle/attack animations driven by the snapshot's `activity`.

**Architecture:** Server-side, `WorldRunner.Build` already emits a per-agent snapshot row `[id,x,z,activity,phase,yaw,kind,groundY]`; we repurpose `kind` to a **render-kind** (Civilian=0 / Guard=1 / Monster=2) and surface a brief `Attack` activity for biting creatures (which have no `BehaviorData`). `SpritePerson.Build` is widened to pack **all** animation records (not just walk+idle) so attack (5–9) and directional idle (15–19) are available, and a new `/asset/spritepools` endpoint serves the three archive pools. The town3d client picks the pool by render-kind, faces by yaw, and selects the animation record from `activity` using Daggerfall's universal record layout. All server changes are read-only snapshot/asset projections — the simulation is untouched, so determinism is preserved.

**Tech Stack:** .NET 10 / C# (`Headless/Sim.Web`, `Headless/Sim.AssetExport`), `DaggerfallConnect.TextureFile`, THREE.js viewer (`wwwroot/town3d.html`).

**Spec:** `docs/superpowers/specs/2026-06-20-kind-sprites-yaw-activity-animation-design.md`

## Global Constraints

- **Daggerfall record layout is universal** (confirmed for archives 255/262/270/399): records `0–4` = walk (facing S/SW/W/NW/N; NE/E/SE = mirror of 3/2/1), `5–9` = primary attack, `10–14` = hurt, `15–19` = idle. **Two categories:** Civilian people use walk `0–4` + idle **record 5**, no attack; Combat mobiles (monsters AND guard 399) use walk `0–4`, attack **5–9**, idle **15–19**.
- **Render-kind values (used verbatim across tasks):** `Civilian = 0`, `Guard = 1`, `Monster = 2`, carried in the snapshot row's `kind` slot (index 6).
- **FPS per state:** walk 6 (combat) / 4 (civilian), attack 10, idle 4. Frame *counts* are per-record and MUST be read from `SpriteMeta.Frames[]` — never hardcoded.
- **Determinism is law:** server changes here are read-only projections (snapshot fields + asset packing); they must not alter sim state. After server changes, `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000` must still print `DETERMINISTIC — parallel == serial OK`.
- **No unit tests** — `Headless/Sim.Tests` is stale/unbuildable (project policy). Verify with: builds, the headless probes specified per task, `--engineworld`, and (for the final rendered result) a human browser eyeball.
- **Env:** every run needs `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2`.
- **Builds:** `dotnet build Headless/Sim.Web/Sim.Web.csproj` and `dotnet build Headless/Sim.AssetExport/Sim.AssetExport.csproj` (and `Headless/Sim.Host` for `--engineworld`).

## File Structure

| File | Responsibility | Task |
| --- | --- | --- |
| `Assets/Sim/Engine/Units/CreatureSystem.cs` | expose `AttackCooldownTicks` so the snapshot can compute a creature's attack window | 1 |
| `Headless/Sim.Web/WorldRunner.cs` | `Build`: emit render-kind in `kind`; derive creature `Attack` activity | 1 |
| `Headless/Sim.AssetExport/SpritePerson.cs` | pack all records; `SpriteMeta.Frames` per record; add guard + monster pools | 2, 3 |
| `Headless/Sim.AssetExport/Program.cs` | `sprite <archive>` diagnostic (prints packed meta) | 2 |
| `Headless/Sim.AssetExport/AssetService.cs` | expose civilian/guard/monster pools | 3 |
| `Headless/Sim.Web/Program.cs` | `/asset/spritepools` endpoint | 3 |
| `Headless/Sim.Web/wwwroot/town3d.html` | pool-by-kind, yaw facing, activity→record animation | 4 |

---

### Task 1: Render-kind + creature attack-activity in the snapshot

Emit a render-kind (Civilian/Guard/Monster) in the snapshot's `kind` slot, and give biting creatures a brief `Attack` activity (they have no `BehaviorData`, so their activity is otherwise always 0).

**Files:**
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (the `AttackCooldownTicks` const)
- Modify: `Headless/Sim.Web/WorldRunner.cs:104-129` (`Build`)

**Interfaces:**
- Consumes: `_world.Identity` (`IdentityData.Kind : EntityKind` — values include `CivilianNPC`, `EnemyMonster`), `_world.Employment` (`EmploymentData.PublicOwner : OwnerId` with `.IsNone`), `_world.Creatures` (`CreatureData.NextAttackTick : long`), `CreatureSystem.AttackCooldownTicks`, `ActivityKind.Attack`.
- Produces: snapshot `AgentRow.Kind ∈ {0,1,2}` (render-kind) and `AgentRow.Activity == (int)ActivityKind.Attack` for creatures within their post-bite window.

- [ ] **Step 1: Make `AttackCooldownTicks` public on `CreatureSystem`**

In `Assets/Sim/Engine/Units/CreatureSystem.cs`, find the creature bite cooldown constant (it is declared near the other `const` fields, e.g. `const long AttackCooldownTicks = 240;`). Change its accessibility to public so the snapshot builder can read it:

```csharp
        public const long AttackCooldownTicks = 240;   // ticks between a creature's bites
```

(Keep the existing numeric value — only add `public`. If the constant has a different value in the file, preserve that value.)

- [ ] **Step 2: Add the render-kind helper + creature-attack window to `WorldRunner.Build`**

In `Headless/Sim.Web/WorldRunner.cs`, replace the body of the `foreach` loop in `Build` (lines ~111-119) so it computes a render-kind and a creature attack activity. New `Build` loop:

```csharp
            var rows = new List<AgentRow>(_world.Position.Count);
            foreach (var kv in _world.Position.All)
            {
                var p = kv.Value;
                if (p == null) continue;

                int act = 0, phase = 0;
                bool isCreature = _world.Creatures.TryGet(kv.Key, out var cr);
                if (_world.Behavior.TryGet(kv.Key, out var b) && b != null) { act = (int)b.Activity; phase = (int)b.Phase; }

                // Creatures have no BehaviorData; surface a brief Attack so the viewer
                // can play the bite. The bite sets NextAttackTick = tick + cooldown, so
                // "just bit" = the cooldown is still almost full.
                if (isCreature && cr.NextAttackTick - tick > CreatureSystem.AttackCooldownTicks - CreatureAttackAnimTicks)
                    act = (int)ActivityKind.Attack;

                rows.Add(new AgentRow(kv.Key.Value, p.X, p.Z, p.Yaw, act, phase, RenderKindOf(kv.Key, isCreature)));
            }
```

Add the render-kind constants + helper as members of `WorldRunner` (e.g. just above `Build`):

```csharp
        // Render categories the viewer maps to sprite pools.
        const int RenderCivilian = 0, RenderGuard = 1, RenderMonster = 2;
        const long CreatureAttackAnimTicks = 15;   // how long after a bite to show the attack pose

        int RenderKindOf(EntityId id, bool isCreature)
        {
            if (isCreature) return RenderMonster;
            if (_world.Identity.TryGet(id, out var ident) && ident != null && ident.Kind == EntityKind.EnemyMonster)
                return RenderMonster;
            if (_world.Employment.TryGet(id, out var emp) && emp != null && !emp.PublicOwner.IsNone)
                return RenderGuard;
            return RenderCivilian;
        }
```

Add `using DaggerfallWorkshop.Sim;` at the top of the file if `ActivityKind` / `EntityKind` / `EntityId` are not already in scope (check the existing usings; `WorldRunner` already references engine types). If `EntityId` is the loop key type, `kv.Key` is already an `EntityId`.

- [ ] **Step 3: Build**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds, 0 errors. (If `CreatureSystem.AttackCooldownTicks` or `_world.Creatures`/`_world.Employment` names differ, fix to the real names — grep `grep -n "Creatures\|Employment" Assets/Sim/Engine/SimWorld.cs`.)

- [ ] **Step 4: Verify determinism is unaffected**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: `DETERMINISTIC — parallel == serial OK`. (The snapshot projection reads settled state only; it must not change the fingerprint.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Engine/Units/CreatureSystem.cs Headless/Sim.Web/WorldRunner.cs
git commit -m "Sim/: emit render-kind (civilian/guard/monster) + creature attack-window in the snapshot"
```

---

### Task 2: Pack all animation records in `SpritePerson.Build`

Today `Build` caps at 6 records (walk 0–4 + idle 5). Combat mobiles need attack (5–9) and directional idle (15–19), so pack all records up to 20. The packed sheet row index equals the source record index, so the client can address records directly.

**Files:**
- Modify: `Headless/Sim.AssetExport/SpritePerson.cs:31` (the `MaxRows` const)
- Modify: `Headless/Sim.AssetExport/Program.cs` (add a `sprite <archive>` diagnostic)

**Interfaces:**
- Consumes: `TextureFile.RecordCount`, `TextureFile.GetFrameCount(r)`, `TextureFile.GetSize(r)` (unchanged usage).
- Produces: `SpriteMeta.Rows` up to 20, `SpriteMeta.Frames[]` with one entry per packed record (row index == source record index).

- [ ] **Step 1: Widen the record cap**

In `Headless/Sim.AssetExport/SpritePerson.cs`, change line 31 from:

```csharp
        const int MaxRows = 6;       // records 0..4 walk + 5 idle
```

to:

```csharp
        const int MaxRows = 20;      // records 0-4 walk, 5-9 attack, 10-14 hurt, 15-19 idle (universal mobile layout)
```

No other change to `Build` is needed: it already loops `for (int r = 0; r < rows; r++)` with `rows = Math.Min(MaxRows, tex.RecordCount)`, measures max cell size across packed records, sizes `Frames[]` to `rows`, and skips missing frames defensively. The packed row index already equals the source record index `r`. Update the file-header comment (lines 1-8) to note the full record layout is now packed (walk/attack/idle), not just walk+idle.

- [ ] **Step 2: Add a `sprite <archive>` diagnostic to AssetExport**

In `Headless/Sim.AssetExport/Program.cs`, add a command that builds a sprite sheet and prints its metadata, so we can confirm the packed records headlessly. Add a case alongside the existing commands (the file already parses `args[0]` as a command and resolves `arena2` from `--arena2`/`$DAGGERFALL_ARENA2`/default):

```csharp
            if (cmd == "sprite")
            {
                if (args.Length < 2 || !int.TryParse(args[1], out int spriteArchive))
                {
                    Console.Error.WriteLine("usage: Sim.AssetExport sprite <archive> [--arena2 <path>]");
                    return 2;
                }
                var (png, meta) = SpritePerson.Build(arena2, spriteArchive);
                Console.WriteLine($"sprite archive {meta.Archive}: rows={meta.Rows} cols(maxFrames)={meta.Cols} cell={meta.CellW}x{meta.CellH} world={meta.WorldW:0.00}x{meta.WorldH:0.00}m png={png.Length}B");
                Console.Write("  frames/record:");
                for (int r = 0; r < meta.Frames.Length; r++) Console.Write($" [{r}]={meta.Frames[r]}");
                Console.WriteLine();
                return 0;
            }
```

Place this before the existing `count`/`dump` handling (or wherever the command switch lives — match the file's existing structure; the command dispatch is near the top of `Main` after `arena2` is resolved). Add `using Sim.AssetExport;` only if `SpritePerson`/`SpriteMeta` are in a different namespace than `Program` (they are both in `Sim.AssetExport`, so no using needed).

- [ ] **Step 3: Build**

Run: `dotnet build Headless/Sim.AssetExport/Sim.AssetExport.csproj`
Expected: build succeeds.

- [ ] **Step 4: Verify the packed records for combat + civilian archives**

Run each and read the output:
```
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.AssetExport -- sprite 255
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.AssetExport -- sprite 399
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.AssetExport -- sprite 381
```
Expected:
- `255` (Rat): `rows=20`, `frames/record` shows walk rows 0–4 with 8 frames, attack rows 5–9 with 6 frames, idle rows 15–19 with 1 frame each (e.g. `[0]=8 [1]=8 … [5]=6 … [15]=1 …`).
- `399` (City Watch): `rows=20`, walk rows 0–4 = 4 frames, rows 5–9 = 5 frames, row 15 = 1 frame.
- `381` (civilian): builds successfully; `rows` = its record count (idle at row 5). 
If `rows` is still capped at 6, the const change didn't take — investigate before committing.

- [ ] **Step 5: Commit**

```bash
git add Headless/Sim.AssetExport/SpritePerson.cs Headless/Sim.AssetExport/Program.cs
git commit -m "Sim/: pack all mobile animation records (walk/attack/idle) + sprite diagnostic"
```

---

### Task 3: Sprite-pool definitions + `/asset/spritepools` endpoint

Expose three archive pools the client picks from by render-kind.

**Files:**
- Modify: `Headless/Sim.AssetExport/SpritePerson.cs` (add `GuardArchives`, `MonsterArchives`)
- Modify: `Headless/Sim.AssetExport/AssetService.cs` (expose the pools)
- Modify: `Headless/Sim.Web/Program.cs:306` area (add `/asset/spritepools`)

**Interfaces:**
- Consumes: `SpritePerson.CivilianArchives` (existing `int[]`).
- Produces: `SpritePerson.GuardArchives`, `SpritePerson.MonsterArchives` (`int[]`); `AssetService.GuardArchives`, `AssetService.MonsterArchives`; HTTP `GET /asset/spritepools` → `{ "civilian": int[], "guard": int[], "monster": int[] }`.

- [ ] **Step 1: Add the guard + monster pools**

In `Headless/Sim.AssetExport/SpritePerson.cs`, just below the `CivilianArchives` array (after line 43), add:

```csharp
        // City Watch guard mobile (archive 399) — uses the combat record layout
        // (walk 0-4, attack 5-9, idle 15-19), same as monsters.
        public static readonly int[] GuardArchives = { 399 };

        // Grounded creatures with the standard walk/attack/idle layout. Excludes
        // flyers and special-animation mobiles (imp/bat/harpy, ghost/wraith,
        // slaughterfish, seducer) whose motion or record layout differs. The client
        // picks one per creature by id-hash (until the sim specifies a creature type).
        public static readonly int[] MonsterArchives =
        {
            255,  // Rat
            257,  // Spriggan
            259,  // Grizzly Bear
            260,  // Sabertooth Tiger
            261,  // Giant Spider
            262,  // Orc
            263,  // Centaur
            270,  // Skeletal Warrior
            272,  // Zombie
            274,  // Mummy
        };
```

- [ ] **Step 2: Expose the pools on `AssetService`**

In `Headless/Sim.AssetExport/AssetService.cs`, next to the existing `CivilianArchives` property (line ~329 `public int[] CivilianArchives => SpritePerson.CivilianArchives;`), add:

```csharp
        public int[] GuardArchives => SpritePerson.GuardArchives;
        public int[] MonsterArchives => SpritePerson.MonsterArchives;
```

- [ ] **Step 3: Add the `/asset/spritepools` endpoint**

In `Headless/Sim.Web/Program.cs`, next to the existing `/asset/civilians` route (line 306 `app.MapGet("/asset/civilians", () => Results.Json(assets.CivilianArchives, jsonOptions));`), add:

```csharp
            app.MapGet("/asset/spritepools", () => Results.Json(new
            {
                civilian = assets.CivilianArchives,
                guard = assets.GuardArchives,
                monster = assets.MonsterArchives,
            }, jsonOptions));
```

Leave `/asset/civilians` in place (harmless; the client will stop using it). `jsonOptions` uses camelCase, so the JSON keys are `civilian`/`guard`/`monster`.

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds.

- [ ] **Step 5: Verify the endpoint serves the three pools**

Start the viewer in the background and curl the endpoint:
```
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web -- Daggerfall Gallotale --port 8086 --tps 0 &
sleep 6
curl -s http://localhost:8086/asset/spritepools
kill %1
```
Expected JSON: `{"civilian":[381,382,...],"guard":[399],"monster":[255,257,259,260,261,262,263,270,272,274]}`. If the route 404s, the endpoint wasn't registered — investigate before committing.

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.AssetExport/SpritePerson.cs Headless/Sim.AssetExport/AssetService.cs Headless/Sim.Web/Program.cs
git commit -m "Sim/: guard + monster sprite pools and /asset/spritepools endpoint"
```

---

### Task 4: Client — pool-by-kind, yaw facing, activity animations

Make the viewer pick the sprite pool by render-kind, face actors by `yaw` when stationary, and select the animation record (walk/attack/idle) from `activity` using the universal layout.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (the agents block: lines ~497-681, plus the pool fetch near line ~770)

**Interfaces:**
- Consumes: `/asset/spritepools` → `{civilian:int[], guard:int[], monster:int[]}`; snapshot row `[id,x,z,activity,phase,yaw,kind,groundY]` (kind ∈ {0,1,2}, activity int); `SpriteMeta.Frames[]` (per-record frame counts).
- Produces: rendered billboards keyed by render-kind, yaw facing, activity-driven records.

- [ ] **Step 1: Fetch the pools instead of just civilians**

Find where the client fetches civilians (search for `'/asset/civilians'`, ~line 770). Replace the fetch + the `civilianArchives` global with a pools object. Change the global declaration (line 500) from:

```javascript
let civilianArchives = null;
```
to:
```javascript
let spritePools = null;   // { civilian:[], guard:[], monster:[] }
```

And replace the fetch site:
```javascript
  spritePools = await (await fetch('/asset/spritepools')).json();
```

- [ ] **Step 2: Add render-kind → pool/profile constants and a picker**

Near the orientation constants (after line 511), add:

```javascript
  // Render-kind (server WorldRunner): 0 civilian, 1 guard, 2 monster.
  const RK_CIVILIAN = 0, RK_GUARD = 1, RK_MONSTER = 2;
  // Animation FPS per state.
  const FPS_WALK_PEOPLE = 4, FPS_WALK_COMBAT = 6, FPS_ATTACK = 10, FPS_IDLE = 4;
  // ActivityKind.Attack ordinal (BehaviorRegistry.ActivityKind). Verify against the
  // enum; it is the value the server sends in row[3] when an actor is attacking.
  const ACT_ATTACK = 19;

  function poolFor(kind) {
    if (!spritePools) return null;
    if (kind === RK_MONSTER) return spritePools.monster;
    if (kind === RK_GUARD)   return spritePools.guard;
    return spritePools.civilian;
  }
```

> NOTE on `ACT_ATTACK`: confirm the integer value of `ActivityKind.Attack` in `Assets/Sim/Registries/BehaviorRegistry.cs` (count the enum members from 0; `None=0, Idle=1, …`). Set `ACT_ATTACK` to that ordinal. If wrong, the attack animation simply never triggers (walk/idle still work) — so verify it during the eyeball check.

- [ ] **Step 3: Carry kind + activity through the snapshot**

In `onSnap` (line 676), extend the per-entity tuple to include kind (row index 6) and activity (index 3):

```javascript
  // id -> [x, z, yaw, groundY, kind, activity]
  curSnap = new Map(msg.entities.map(e => [e[0], [e[1], e[2], e[5], e[7] || 0, e[6] || 0, e[3] || 0]]));
```

- [ ] **Step 4: Pick the pool by kind in `updateAgents`**

In `updateAgents` (line 621-625), replace the archive selection:

```javascript
  for (const [id, c] of curSnap) {
    seen.add(id);
    const kind = c[4] | 0;
    const pool = poolFor(kind);
    if (!pool || pool.length === 0) continue;
    const archive = pool[hash32(id) % pool.length];
    const s = getSheet(archive);
    if (!s) continue;
```

- [ ] **Step 5: Replace facing + record selection with yaw + activity**

Replace the facing/record block (lines ~650-666, from `// Facing from actual movement` through the `setCellUV(...)` call) with state-aware selection. Walk facing keeps the verified velocity math; idle/attack face by `yaw`; the record block is chosen by activity + category:

```javascript
    // Direction index 0-7 (S,SW,W,NW,N,NE,E,SE) relative to the camera-locked
    // billboard. Moving: derive from velocity (verified). Else: derive from yaw.
    const mvx = c[0] - pr[0], mvz = c[1] - pr[1];
    const speed = Math.hypot(mvx, mvz);
    let fx, fz;
    if (speed >= 0.05) { const fl = 1 / speed; fx = mvx * fl; fz = mvz * fl; }
    else { const ry = c[2] * Math.PI / 180; fx = Math.sin(ry); fz = Math.cos(ry); }   // yaw is degrees = atan2(dx,dz)
    let dx = camera.position.x - x, dz = camera.position.z - z;
    const dl = Math.hypot(dx, dz) || 1; dx /= dl; dz /= dl;
    const dot = dx * fx + dz * fz, cross = dx * fz - dz * fx;
    let ori = Math.round(DIR_SIGN * Math.atan2(cross, dot) / (Math.PI / 4));
    ori = ((ori % 8) + 8) % 8;

    const combat = (kind === RK_GUARD || kind === RK_MONSTER);
    const attacking = combat && (c[5] | 0) === ACT_ATTACK;

    let base, fps, directional;
    if (attacking)      { base = 5;  fps = FPS_ATTACK; directional = true; }   // attack rows 5-9
    else if (speed >= 0.05) { base = 0; fps = combat ? FPS_WALK_COMBAT : FPS_WALK_PEOPLE; directional = true; }  // walk rows 0-4
    else if (combat)    { base = 15; fps = FPS_IDLE; directional = true; }     // combat idle rows 15-19
    else                { base = 5;  fps = FPS_IDLE; directional = false; }    // civilian idle row 5

    let record = directional ? base + ORI_RECORD[ori] : base;
    let flip = directional ? ORI_FLIP[ori] : false;
    if (record >= s.meta.rows) { record = ORI_RECORD[ori]; flip = ORI_FLIP[ori]; }   // archive lacks this block → fall back to walk

    const fc = s.meta.frames[record] || 1;
    const frame = Math.floor(t * fps) % fc;
    setCellUV(p.geo, s, record, frame, flip);
```

(`x`, `z`, `pr`, `t` are already in scope above this block from the position-interpolation code; leave that code unchanged. Remove the now-replaced `walkFrame`-only logic; `walkFrame` at line 617 can stay unused or be deleted.)

- [ ] **Step 6: Build the web project**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds (the HTML is static content; this confirms the project still builds).

- [ ] **Step 7: Human browser eyeball (the rendered result cannot be verified headlessly)**

Run:
```
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web -- Daggerfall Gallotale --port 8086 --tps 30
```
Open `http://localhost:8086/town3d.html`. Confirm:
- **Monsters** render as varied creatures (rats/spiders/orcs/skeletons…), not villagers, and face the direction they move.
- A monster biting a civilian plays its **attack** frames briefly.
- **Guards** render as the City Watch and play an attack swing when intercepting a monster. *(If guard archive 399's records 5–9 render wrong, the documented fallback is to treat guards as non-attacking: in Step 5, change `const combat = (kind === RK_GUARD || kind === RK_MONSTER);` so guards still use combat idle (record 15) but skip attack — e.g. gate `attacking` on `kind === RK_MONSTER` only. Apply only if the guard swing looks broken.)*
- **Civilians** look and animate as before (walk + idle, idle record 5).
- Facing is correct while standing still (yaw), not just while moving.

Because this is a visual check, the implementer should report DONE_WITH_CONCERNS noting "rendered result pending human confirmation" rather than claiming the visuals were verified.

- [ ] **Step 8: Commit**

```bash
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "Sim/: town3d picks sprite pool by kind, faces by yaw, animates walk/idle/attack"
```

---

## Self-Review

**Spec coverage:**
- Render-kind in snapshot (spec §2) → Task 1. ✓
- Creature attack surfacing (spec §3) → Task 1 (Step 2, `CreatureAttackAnimTicks` window). ✓
- Pack all records (spec §5) → Task 2. ✓
- Sprite pools + endpoint (spec §4) → Task 3. ✓
- Client pool-by-kind + yaw facing + activity→record (spec §6) → Task 4. ✓
- Universal record layout / two categories (spec §1) → encoded in Task 4 Step 5 (civilian idle 5 / combat idle 15 + attack 5). ✓
- Guard-attack fallback (spec §1 note) → Task 4 Step 7 fallback instruction. ✓
- Determinism unaffected (spec §8) → Task 1 Step 4. ✓
- Monster pool excludes flyers/special (spec §4) → Task 3 Step 1 list. ✓

**Placeholder scan:** No TBD/TODO. The two values flagged for in-code verification — `ACT_ATTACK` ordinal and the `yaw` degrees/radians convention — are given concrete defaults with a one-line "verify during eyeball" note and a non-fatal failure mode (attack just won't trigger / facing offset), not deferred work.

**Type consistency:** `RenderKind` 0/1/2 defined in Task 1, consumed as `RK_*` in Task 4. `SpriteMeta.Frames[]` widened in Task 2, read in Task 4. Pool names `civilian/guard/monster` produced in Task 3, consumed in Task 4 `poolFor`. `AttackCooldownTicks` made public in Task 1 Step 1, used in Task 1 Step 2. Consistent.
