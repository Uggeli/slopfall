# Daggerfall Sprite & Flat TEXTURE Archive Reference

## Introduction

Every visual asset in Daggerfall's game world is stored in binary files named `TEXTURE.NNN` (zero-padded three-digit number). These archives serve two distinct purposes:

**Geometry textures (000–499, most of the range):** Wall/floor/ceiling tiles applied to 3-D building and dungeon geometry. These are tiled rectangular bitmaps, not sprites. They are NOT rendered as billboards and are NOT covered in detail here beyond identifying their range.

**Sprite / flat archives:** Individual billboard images or animation sequences rendered as camera-facing quads in the world. They come in two sub-kinds:
- *Mobile sprites* (creatures and humanoid enemies): multi-record archives with directional walk/attack/idle animation sequences, one record per facing.
- *Flats / decoration billboards*: single-frame or short animated images placed in the world as furniture, nature props, light sources, animals, NPC civilians, loot piles, and editor markers.

All TEXTURE archives use the same `TextureFile` binary format (DaggerfallConnect `Arena2.TextureFile`). Each archive has a record count; each record has a frame count. A palette file (`DFPAL.COL` or archive-embedded) maps pixel indices to RGBA.

**Total archives confirmed present on disk:** 472 files (out of the 0–511 range).  
Archives **missing** from this copy of ARENA2: 21, 32, 34, 51, 52, 78, 187–189, 191–193, 196, 219–232, 243–244, 294, 367, 373, 421, 441, 471–472, 496–499.  
(Some of these numbers may never have existed in the original game data.)

---

## Mobile Enemies / Creatures

Mobile sprites use the directional animation model described in the **Animation & Record Model** section. Each creature has a `MaleTexture` and `FemaleTexture` archive (most creatures use the same archive for both; only human enemy classes have distinct male/female archives).

Source: `Assets/Scripts/Utility/EnemyBasics.cs` (data-confirmed; extracted directly from the `Enemies` array).

### Creature sprites (IDs 0–42, non-human)

| ID | Name | Archive(s) M/F | Notes |
|----|------|----------------|-------|
| 0 | Rat | 255 / 255 | Uses `RatIdleAnims` (non-standard flip) |
| 1 | Imp | 256 / 256 | Flying |
| 2 | Spriggan | 257 / 257 | |
| 3 | Giant Bat | 258 / 258 | Flying |
| 4 | Grizzly Bear | 259 / 259 | |
| 5 | Sabertooth Tiger | 260 / 260 | |
| 6 | Spider | 261 / 261 | |
| 7 | Orc | 262 / 262 | |
| 8 | Centaur | 263 / 263 | |
| 9 | Werewolf | 264 / 264 | |
| 10 | Nymph | 265 / 265 | |
| 11 | Slaughterfish | 266 / 266 | Aquatic; `SlaughterfishMoveAnims` (bounce) |
| 12 | Orc Sergeant | 267 / 267 | |
| 13 | Harpy | 268 / 268 | Flying |
| 14 | Wereboar | 269 / 269 | |
| 15 | Skeletal Warrior | 270 / 270 | |
| 16 | Giant | 271 / 271 | |
| 17 | Zombie | 272 / 272 | |
| 18 | Ghost | 273 / 273 | Spectral; uses `GhostWraithMoveAnims` + `GhostWraithAttackAnims` |
| 19 | Mummy | 274 / 274 | |
| 20 | Giant Scorpion | 275 / 275 | |
| 21 | Orc Shaman | 276 / 276 | Has `HasSpellAnimation` |
| 22 | Gargoyle | 277 / 277 | |
| 23 | Wraith | 278 / 278 | Spectral; uses `GhostWraithMoveAnims` + `GhostWraithAttackAnims` |
| 24 | Orc Warlord | 279 / 279 | |
| 25 | Frost Daedra | 280 / 280 | |
| 26 | Fire Daedra | 281 / 281 | |
| 27 | Daedroth | 282 / 282 | |
| 28 | Vampire | 283 / 283 | |
| 29 | Daedra Seducer | 284 / 284 | Special transform animations (records 20–23) |
| 30 | Vampire Ancient | 285 / 285 | |
| 31 | Daedra Lord | 286 / 286 | |
| 32 | Lich | 287 / 287 | |
| 33 | Ancient Lich | 288 / 288 | |
| 34 | Dragonling | 289 / 289 | Flying |
| 35 | Fire Atronach | 290 / 290 | |
| 36 | Iron Atronach | 291 / 291 | |
| 37 | Flesh Atronach | 292 / 292 | |
| 38 | Ice Atronach | 293 / 293 | |
| 39 | Horse | (unused) | ID 39 exists in code but no texture assigned |
| 40 | Dragonling (large) | 295 / 295 | Flying; distinct from ID 34 |
| 41 | Dreugh | 296 / 296 | Aquatic |
| 42 | Lamia | 297 / 297 | Aquatic |

### Human enemy class sprites (IDs 128–146)

Human enemies have gender-distinct archives. Archive 399 is the City Watch guard.

| ID | Class | Male archive | Female archive |
|----|-------|-------------|----------------|
| 128 | Mage | 486 | 485 |
| 129 | Spellsword | 476 | 475 |
| 130 | Battlemage | 490 | 489 |
| 131 | Sorcerer | 478 | 477 |
| 132 | Healer | 486 | 485 |
| 133 | Nightblade | 490 | 489 |
| 134 | Bard | 484 | 483 |
| 135 | Burglar | 484 | 483 |
| 136 | Rogue | 480 | 479 |
| 137 | Acrobat | 484 | 483 |
| 138 | Thief | 484 | 483 |
| 139 | Assassin | 480 | 479 |
| 140 | Monk | 488 | 487 |
| 141 | Archer | 482 | 481 |
| 142 | Ranger | 482 | 481 |
| 143 | Barbarian | 488 | 487 |
| 144 | Warrior | 488 | 487 |
| 145 | Knight | 488 | 487 |
| 146 | City Watch (guard) | 399 | 399 |

**Distinct archives used by human enemies:** 399, 475, 476, 477, 478, 479, 480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490.

### Corpse flat archives (referenced in `CorpseTexture(archive, record)`)

Corpse flats are static single-record billboards used after a creature dies. The archive is encoded as `(archive << 16) + record` in EnemyBasics.

| Archive | Used for corpses of |
|---------|---------------------|
| 96 | Human enemy corpses (records 0–5: dragonling, gargoyle, orc, vampire, giant bat, werewolf/wereboar) |
| 305 | Aquatic creatures (dreugh r0, slaughterfish r1, lamia r2) |
| 306 | Undead (ghost r0, skeleton r1, lich r2, ancient lich r3, zombie r4, mummy r5) |
| 380 | Human enemy corpses (fallback; record 1) |
| 400 | Daedra (daedroth r1, fire daedra r2, frost daedra r3, daedra lord r4, seducer r6) |
| 401 | Animal corpses (bat r0, bear r2, tiger r3, spider r4, scorpion r5; r1 = rat) |
| 405 | Atronach corpses (flesh r0, iron r1, fire r2, ice r3) |
| 406 | Daylight creature corpses (centaur r0, giant r1, nymph r2, spriggan r3, harpy r4, imp r5) |

---

## People / NPC Sprites

### Civilian archives (slopfall SpritePerson.cs — data-confirmed)

These 24 archives are the hardcoded civilian set used by `SpritePerson.CivilianArchives`. Each archive packs records 0–4 (walk S/SW/W/NW/N) + record 5 (idle). The web server exposes them at `/asset/civilians`.

| Race | Gender | Archives (4 outfit variants) |
|------|--------|-----------------------------|
| Redguard | Male | 381, 382, 383, 384 |
| Redguard | Female | 395, 396, 397, 398 |
| Nord | Male | 387, 388, 389, 390 |
| Nord | Female | 392, 393, 451, 452 |
| Breton | Male | 385, 386, 391, 394 |
| Breton | Female | 453, 454, 455, 456 |

The client hashes each entity's ID into one of these 24 archives to select a visual.

### Other people flat archives (from RDBLayout.cs and TextureReader.cs — data-confirmed)

These appear in dungeon and interior NPC placement records.

| Archive | Label in code | Description |
|---------|---------------|-------------|
| 175 | (dungeon NPC) | Dungeon NPC flat set |
| 176 | (dungeon NPC) | Dungeon NPC flat set |
| 177 | (dungeon NPC) | Dungeon NPC flat set |
| 178 | (dungeon NPC) | Dungeon NPC flat set |
| 179 | (dungeon NPC) | Dungeon NPC flat set |
| 180 | (dungeon NPC) | Dungeon NPC flat set |
| 181 | `templePeople` | Temple interior NPC set; record 3 = child |
| 182 | `mediumCommonPeople` | Common interior NPCs; records 4/5/6/18/36–38/42–43/52–53 = children |
| 183 | (dungeon NPC) | Dungeon NPC flat set |
| 184 | `flatPeople2` | NPC flat set 2; record 15 = child |
| 186 | `testBigFlats` | Large-scale NPC flats; records 4–7/19/37–39/43–44/53–54 = children |
| 197 | `kludgeTown` | Town NPC kludge set; record 3 = child |
| 334 | `daggerfallPeople` | Daggerfall city exterior townspeople |
| 346 | `wayrestPeople` | Wayrest city exterior townspeople |
| 357 | `sentinelPeople` | Sentinel city exterior townspeople |

Archives 334, 346, 357 plus 175–184 are listed in `RDBLayout.NPCFlatArchives` as the authoritative dungeon NPC flat list.

### Paper doll / inventory-icon adjacent archives

| Archive | Description |
|---------|-------------|
| 245–248 | Female paper doll clothing layers (`firstFemaleArchive = 245`, ItemBuilder.cs) |
| 249–252 | Male paper doll clothing layers (`firstMaleArchive = 249`, ItemBuilder.cs) |
| 253 | Mixed interior / dungeon prop sprites; many records are emissive lights |
| 432 | Artifact male texture (world-placed item icons) |
| 433 | Artifact female texture (world-placed item icons) |

---

## Flats / Decorations

### Nature & climate flats (500–511 — data-confirmed, DFLocation.ClimateSet)

The climate set used for a location is stored in the map data and selects which 500-series archive provides the outdoor tree/plant/rock billboards. These archives are **sprite/flat archives** (not geometry), confirmed present on disk.

| Archive | ClimateSet name | Description |
|---------|-----------------|-------------|
| 500 | Nature_RainForest | Rainforest trees, palms, tropical plants |
| 501 | Nature_SubTropical | Sub-tropical nature (Hammerfell coasts) |
| 502 | Nature_Swamp | Swamp trees, reeds, mangroves |
| 503 | Nature_Desert | Desert cacti, scrub, rocks |
| 504 | Nature_TemperateWoodland | Temperate deciduous trees (summer) |
| 505 | Nature_TemperateWoodland_Snow | Temperate woodland — winter/snow |
| 506 | Nature_WoodlandHills | Highland/forest trees (summer) |
| 507 | Nature_WoodlandHills_Snow | Woodland hills — winter |
| 508 | Nature_HauntedWoodlands | Dead/haunted trees, Oblivion aesthetic |
| 509 | Nature_HauntedWoodlands_Snow | Haunted woodlands — winter |
| 510 | Nature_Mountains | Mountain scrub, rocks |
| 511 | Nature_Mountains_Snow | Mountain — winter |

All 12 (500–511) are confirmed present on disk.

Climate/season keying: the engine selects the archive at `RMBLayout.AddNatureFlats()` time based on `ClimateSet` (always a 500-series archive) and `ClimateWeather`. The +1 convention for snow variants means, e.g., temperate summer = 504, winter = 505.

### Interior furniture & clutter (reference-derived, confirmed partially via TextureReader emissive list)

These archives contain single-frame or short-animated billboard props placed inside buildings and dungeons.

| Archive | Label / description |
|---------|---------------------|
| 97 | Misc interior props (cited in TextureReader commented MiscFlatsTextureArchives) |
| 200 | Interior furniture / clutter set A; records 7–10 are emissive (candles, fire) |
| 201 | Animals flat set (`AnimalsTextureArchive = 201` in TextureReader.cs) |
| 202 | Dungeon/interior props; record 2 = glowing statue (emissive) |
| 203 | Interior furniture / clutter |
| 204 | Clothing / loot piles (`clothingArchive = 204` in DaggerfallLootDataTables.cs) |
| 205 | Boxes & bottles (`boxesNbottlesArchive = 205`) |
| 206 | Interior props |
| 207 | Combat/weapon prop icons (`combatArchive = 207`) |
| 208 | Alchemist / misc interior; record 2 = brewing potion (emissive) |
| 209 | Academic props / books (`academicArchive = 209`) |
| 210 | **Light sources** (`LightsTextureArchive = 210`): candles, torches, lanterns — records 0–29 mostly emissive; the engine places point-light components for these |
| 211 | Misc clutter (`miscArchive = 211`) |
| 212 | Additional interior furniture |
| 213 | Additional interior props |
| 215 | Interior decoration |
| 216 | **Fixed treasure / loot pile icons** (`FixedTreasureFlatsArchive = 216`; `randomTreasureArchive = 216`) |
| 217 | Interior props |
| 253 | Mixed dungeon / interior; many records are emissive (archive 253 records 10/17–19/22/41/48–52/75/77 cited in emissive list) |
| 301 | Misc flats (cited in TextureReader commented list) |

Archives 200–218 are confirmed present on disk (except 219+ which are missing).

### Lights (dedicated archive)

| Archive | Description |
|---------|-------------|
| 101 | Exterior light sources (wall sconces, lanterns): records 2/3/5–9/11/12 are emissive |
| 190 | Additional light/glow billboards; records 3/4/5 emissive |
| 210 | Interior lights (see above under Furniture) |

### Animals

| Archive | Description |
|---------|-------------|
| 201 | Animals flat archive (`AnimalsTextureArchive = 201`); confirmed present |

### Treasure / loot

| Archive | Description |
|---------|-------------|
| 204 | Clothing piles (used as loot container icon in some building types) |
| 205 | Boxes and bottles |
| 207 | Combat gear / weapon loot |
| 209 | Academic / book piles |
| 211 | Misc loot |
| 216 | Random treasure chest / pile icons (fixed and random treasure spawns) |

### Editor / marker flats

| Archive | Constant | Description |
|---------|----------|-------------|
| 199 | `EditorFlatsTextureArchive` | **Editor marker archive** — the record index encodes the marker type (enemy spawn, treasure, entrance, etc.). Detected at runtime to place gameplay logic rather than render a visible billboard. |

All editor marker objects in RMB (block flat records), RDB (dungeon flat records), and interior flat records that reference archive 199 trigger special behaviour rather than a visible sprite. The record index is cast to `InteriorMarkerTypes` or the dungeon equivalent.

### Spell / effect billboards (not standard flats)

| Archive | Description |
|---------|-------------|
| 375 | Cold missile projectile |
| 376 | Fire missile projectile |
| 377 | Poison missile projectile |
| 378 | Shock missile projectile |
| 379 | Magic missile projectile |
| 356 | Fire walls / magical barriers (`FireWallsArchive = 356`); animated at 5 fps |
| 473 | Ghost/spectral glow effect (14 records; also in TextureFile.IsTranslucent) |

### Misc billboard archives

| Archive | Description |
|---------|-------------|
| 380 | Human corpse / blood effects; record 1 = humanoid corpse, record 3 = emissive (blood) |
| 434 | Spell/enchantment effect; record 3 is emissive |

---

## Animation & Record Model

### Directional sprite layout (mobile creatures)

All mobile sprites (enemies, creatures) in a TEXTURE archive use **records 0–4 for directional walk/move animation**, with east-side directions achieved by flipping west-side records in the renderer:

| Record | Facing | Notes |
|--------|--------|-------|
| 0 | South (front) | |
| 1 | South-west | Also used mirrored for south-east |
| 2 | West | Also used mirrored for east |
| 3 | North-west | Also used mirrored for north-east |
| 4 | North (back) | |
| 5–9 | Primary attack — same 5-direction set | |
| 10–14 | Hurt animation — same 5-direction set | |
| 15–19 | Idle animation — same 5-direction set | |
| 20–24 | Ranged attack 1 (humanoids only) | |
| 25–29 | Ranged attack 2 (475, 489, 490 only) | |

Facing NE/E/SE reuse records 3/2/1 with `FlipLeftRight = true`. This gives 8 logical orientations from 5 stored records.

Special cases noted in EnemyBasics.cs:
- **Ghost/Wraith** (`GhostWraithMoveAnims`, `GhostWraithAttackAnims`): non-standard flip pattern within the 0–9 record range.
- **Rat** (`RatIdleAnims`): inverted flip conventions for idle facing.
- **Slaughterfish** (`SlaughterfishMoveAnims`): `BounceAnim = true` — animates 0→N→0 rather than looping.
- **Daedra Seducer**: has additional special records 20–23 for its unique transform sequences (all 8 orientations point to same record, player-facing only).

### Civilian NPC sprite layout (SpritePerson.cs)

Civilian people archives (the 381–456 set) use a simpler model:

| Record | Content |
|--------|---------|
| 0 | Walk facing south |
| 1 | Walk facing south-west |
| 2 | Walk facing west |
| 3 | Walk facing north-west |
| 4 | Walk facing north |
| 5 | Idle (all orientations; typically single frame) |

NE/E/SE again mirror 3/2/1. Maximum 6 records are packed (`MaxRows = 6` in SpritePerson.cs).

### DFBitmap / palette / world-scale facts

- **Palette**: each archive has an embedded palette name (`tex.PaletteName`); SpritePerson calls `tex.LoadPalette(arena2 + paletteName)` before decoding. Index 0 is always transparent (`transparentIndex0: true` in TextureDecode.Rgba).
- **World scale formula** (SpritePerson.cs line 97–98):
  ```
  worldW = (sz.Width  + sz.Width  * scale.Width  / 256f) * 0.025f
  worldH = (sz.Height + sz.Height * scale.Height / 256f) * 0.025f
  ```
  `GlobalScale = 0.025f` converts from Daggerfall's pixel units to metres. The `scale` field is a per-record sub-texel scale baked into the TEXTURE binary.
- **Animation FPS** (EnemyBasics.cs defaults): move = 6 fps, attack = 10 fps, hurt = 4 fps, idle = 4 fps, ranged = 10 fps.
- The spectral archives (273 Ghost, 278 Wraith, 473 glow effect) are identified by `TextureFile.IsTranslucent()` and rendered with an additive/alpha blend rather than opaque.

---

## How Slopfall Uses These Today

### Civilian NPC rendering

- **`SpritePerson.cs`** (`Headless/Sim.AssetExport/SpritePerson.cs`): `Build(arena2, archive)` reads a civilian archive, packs records 0–5 into a single transparent-background PNG atlas (cols × rows cells), and returns a `SpriteMeta` struct with pixel dimensions, world metres, and per-record frame counts.
- **`/asset/civilians`** (`Sim.Web/Program.cs:306`): returns `SpritePerson.CivilianArchives` as JSON — the 24 archive numbers the client hashes entity IDs into.
- **`/asset/spritesheet/{archive}`** (`Program.cs:308`): serves the packed PNG for any archive number.
- **`/asset/spritemeta/{archive}`** (`Program.cs:316`): serves the `SpriteMeta` JSON.

### Snapshot fields driving sprite selection (render plane 3, town3d.html)

Each agent snapshot carries:
- `kind` — maps to an archive (currently always a civilian archive from the 24-entry list)
- `yaw` — world rotation → selects one of the 8 facing orientations (mapped to records 0–4 with optional UV flip)
- `activity` — determines whether walk (records 0–4) or idle (record 5) animation set is used
- `phase` — frame index within the current record's frame sequence

The 3-D WebGL viewer (`wwwroot/town3d.html`) fetches the spritesheet PNG and spritemeta JSON per archive, then UV-maps the correct cell onto a camera-facing quad at the agent's world position.

### Texture lookup

**`/asset/texture/{archive}/{record}`** (`Program.cs:324`): serves a PNG of any single flat record from any TEXTURE archive — used for ad-hoc debugging and potentially future flat/prop rendering passes.

---

## Provenance

| Claim | Status |
|-------|--------|
| All 43 creature/NPC enemy entries (IDs 0–42, 128–146) with MaleTexture/FemaleTexture | **Data-confirmed**: read directly from `EnemyBasics.cs` Enemies array |
| 24 civilian archives (381–456 range) | **Data-confirmed**: read directly from `SpritePerson.CivilianArchives` |
| Nature archives 500–511 with ClimateSet names | **Data-confirmed**: `DFLocation.ClimateSet` enum in `DFLocation.cs` |
| Editor flat archive 199, Animals 201, Lights 210, Treasure 216 | **Data-confirmed**: constants in `TextureReader.cs` |
| Corpse archives 96, 305, 306, 380, 400, 401, 405, 406 | **Data-confirmed**: encoded in EnemyBasics `CorpseTexture()` calls |
| RDB dungeon NPC archives {175–184, 334, 346, 357} | **Data-confirmed**: `RDBLayout.NPCFlatArchives` list |
| daggerfallPeople=334, wayrestPeople=346, sentinelPeople=357 | **Data-confirmed**: `TextureReader.IsChildNPCTexture()` |
| Loot pile archives 204, 205, 207, 209, 211 | **Data-confirmed**: `DaggerfallLootDataTables.cs` constants |
| Spell missile archives 375–379, fire wall 356 | **Data-confirmed**: `DaggerfallMissile.cs` and `MaterialReader.cs` |
| Human paper-doll archives 245–252 | **Data-confirmed**: `ItemBuilder.cs` firstFemaleArchive / firstMaleArchive |
| Interior furniture archives 200, 202–203, 206, 208, 212–213, 215, 217, 253, 301 | **Reference-derived**: inferred from the emissive-texture list in `TextureReader.cs` and the commented `MiscFlatsTextureArchives` array; confirmed present on disk but detailed record-level descriptions come from community Daggerfall modding documentation, not from code constants |
| Archive existence (all 472 confirmed) | **Data-confirmed**: `ls /home/uggeli/df-data/arena2/TEXTURE.*` |
| Paper doll clothing detail (what records 245–252 contain) | **Reference-derived**: known from Daggerfall modding community; code only provides the base archive number constants |
| Detailed interior prop descriptions (what each record in archives 200–217 looks like) | **Reference-derived**: standard Daggerfall knowledge; slopfall code only names the archives, not the individual records |
