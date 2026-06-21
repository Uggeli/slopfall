# Systems status

Implemented / partial / absent inventory, from a codebase survey (2026-06-21). This is the *grounding*
behind [`../../TODOS.md`](../../TODOS.md); when they disagree, trust the code and update both.

Legend: ✅ implemented · 🟡 partial · ⛔ absent/stub.

## Sim / gameplay
| System | State | Note |
|--------|:---:|------|
| ODD decision planner (`OddSystem` + `OddTree`) | ✅ | Depth-1 Enables-DAG, backward-propagating scores, wired into `Decide`. Deeper planning/goals pending. |
| Movement & pathfinding | 🟡 | Hierarchical pathfinding works; **no steering/separation** → agents stack. "Wander but don't move" needs a repro. |
| Work-spot binding | 🟡 | Jobs target the *employer building centre*; no marked work/water/fishing spots → fishers fish at home. |
| Economy & money | ✅ | Harvest→market→wage→tax→export cycle; conserves. Coins are fractional `double` (should be whole). |
| Items & inventory | 🟡 | Ownership/location/theft-guilt exist; items don't advertise verbs; no stacking/containers/equip/weight. |
| Time / scheduling | ✅ | Hour/day/month boundaries, dawn/dusk events, hour-gated activities. No curfew mandate. |
| Skills & leveling | 🟡 | XP/advancement engine works; **no defined skill list, no UI**. |
| Factions & reputation | 🟡 | Pairwise Familiarity/Regard only; no faction membership/standing. |
| Magic | 🟡 | Effects lifecycle + Magicka exist; `CastSpell` reserved only — no casting/spellbook/spells. |
| Creatures & combat | 🟡 | Systems exist but sparse placeholders (few creatures, ~1 HP bites); no creature AI. |
| Caravans / inter-settlement travel | ⛔ | Pathfinding is intra-town only. |

## Cognitive substrate / living world
| Layer | State | Note |
|-------|:---:|------|
| S2 Emotion (`AffectsSystem`) | ✅ | Directed emotions as residuals; emotion-as-controller prunes the marketplace. |
| S1 Membrane / L3 | 🟡 | `interpret()` collapsed into the decision path, not a full per-tick subjective-view pipeline. |
| S3 Meanings | 🟡 | Proto category nodes + slow decay; no full consolidation (delta-bag/MINT/SETTLE/surprise/false-memory). |
| S4 Conscience | 🟡 | Data structure only; begging-shame seeded; no learned installer, guilt pole, taboo/sacred edge. |
| L1 Entropy | ✅ | Satisfaction/decay model; placeholder rates. |
| L2 Lifecycle | 🟡 | Aging→death→despawn work; **no birth/reproduction**; population is immigration-only. |
| L4 Directed drives | 🟡 | `Ad.Target` exists; one directed drive (alms); Chat/SeekHelp still interrupt, not scored ads. |
| Gossip / rumor spread | ⛔ | Agents re-learn independently. |
| Crime witnessing & enforcement | ⛔ | Theft/attack charge guilt; no witnesses/guards-response/deterrent. |

## Rendering / viewer (`Sim.Web` + `town3d.html`)
| Feature | State | Note |
|---------|:---:|------|
| Terrain / buildings / walls / flats | ✅ | Per-map-pixel ring streaming around the camera. |
| NPC sprites | ✅ | Animated, 8-direction, yaw/activity-aware (walk/attack/hurt/idle rows). |
| Flat/prop animation | 🟡 | Nature/prop billboards are static (no wind/sway). |
| Agent / building / ODD-tree inspect | ✅ | Click an agent or building; live-updating watch; decision-tree view. |
| Settlement / house inspect | 🟡 | Only nested inside building detail; no dedicated panel or interior view. |
| Water (mesh/shader) | ⛔ | No water rendering at all. |
| Building interiors / doors / stairs | ⛔ | Exterior block geometry only. |
| Sky / day-night / weather tint | ✅ | Hour-based sun arc, twilight, weather desaturation/fog. |
| Seasons | 🟡 | Flag carried; no visual swap. |
| Post-processing | ⛔ | No bloom/tonemap/AA. |

## Tests
- Suite is **substantive** (determinism, ODD scoring, industry chains, subsistence, social) — not
  "meaningless happy cases".
- Real risk is a **build break** (e.g. a missing `RenderSnapshot` in `Sim.Net`) gating most of the
  suite. Fix the build first, then audit for assumptions left by the render-client timestep refactor.
- `Sim.SpatialTests` runs independently.
