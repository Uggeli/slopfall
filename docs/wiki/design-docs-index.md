# Design docs index

Annotated map of `docs/`. These are the deep design/vision docs; this wiki summarises and links them.
Status of each system (built vs aspirational) is in [systems status](systems-status.md).

## Read-first
- [`../behavior.md`](../behavior.md) — the honest scorecard + roadmap for NPC behaviour. The README
  points here repeatedly; best single entry point to the AI design.

## Decision & cognition
- [`../decision_architecture.md`](../decision_architecture.md) — Sense → Deliberate → Execute
  pipeline; the Ad / AffordanceCatalog / modular scoring model.
- [`../architecture_refactor.md`](../architecture_refactor.md) — the refactor that introduced the
  above pipeline.
- [`../action_catalog.md`](../action_catalog.md) — the verb catalog (which actions exist; which are
  implemented vs reserved).
- [`../odd_spec.md`](../odd_spec.md), [`../odd_convergence.md`](../odd_convergence.md) — the ODD
  planner spec and its convergence on the Atoms architecture (O0–O5 phases).
- [`../drive_engine.md`](../drive_engine.md) — the need/drive/deficit-pole model.
- the **cognitive foundation** — current canon (the older `cognitive_substrate_*` S1–S4 docs were
  retired in favour of these): the `what_is_*` series —
  [`../what_is_a_bunny.md`](../what_is_a_bunny.md) /
  [`../what_is_world.md`](../what_is_world.md) /
  [`../what_is_drive.md`](../what_is_drive.md) /
  [`../what_is_a_memory.md`](../what_is_a_memory.md) /
  [`../what_is_an_atom.md`](../what_is_an_atom.md) — with the course correction in
  [`../cognitive_layer_audit.md`](../cognitive_layer_audit.md) and the Phase-0 design/plan under
  `superpowers/`.
- [`../fear.md`](../fear.md) — fear as the sixth drive; threat controller; freeze/fight gating.

## Living world
- [`../living_world.md`](../living_world.md) + L-layers:
  [`L1 entropy`](../living_world_L1_entropy.md) /
  [`L2 lifecycle`](../living_world_L2_lifecycle.md) /
  [`L3 membrane`](../living_world_L3_membrane.md) /
  [`L4 directed drives`](../living_world_L4_directed_drives.md).

## Economy & world
- [`../economy.md`](../economy.md) — base circulation, employment, wages, tax.
- [`../goods_economy.md`](../goods_economy.md) — stock, goods, market hub model.
- [`../subsistence.md`](../subsistence.md) — food / larder / hunger loop.
- [`../industry_layers.md`](../industry_layers.md) — production tiers & recipes (designed, largely
  unbuilt).
- [`../production_and_trade_stage5.md`](../production_and_trade_stage5.md) — the Stage-5 production &
  trade plan (food loop, store-as-hub, employment, guild economies, pricing).
- [`../regional_loader_stage2.md`](../regional_loader_stage2.md) — regional loader (all settlements
  into one unified space).
- [`../regional_finance_stage3.md`](../regional_finance_stage3.md) — per-settlement public finance.

## Items
- [`../items_and_inventory.md`](../items_and_inventory.md) — items as atoms, ownership/theft,
  affordances on items.

## Rendering & data
- [`../render_client_dataflow.md`](../render_client_dataflow.md) — how render snapshots flow to
  clients.
- [`../daggerfall-sprite-and-flat-archives.md`](../daggerfall-sprite-and-flat-archives.md) — sprite /
  flat archive catalog (e.g. City Watch guard = archive 399).

## Specs (dated implementation specs)
See [`../superpowers/specs/`](../superpowers/specs/) for point-in-time implementation specs (town
walls, guards/dynamic-object-zero, region spatial index, kind sprites, world flora, town3d inspect).

> Note: `docs/Architechturesample.cs` is a code sample, not a doc.
