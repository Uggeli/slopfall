# What Is a Bunny?

## Two-Level Behavior Model

**Activity** = verb-phrase intent, scored by Wants, composed of Actions, chains to other Activities.  
**Action** = atomic physical act + effect on doer/target, chains via Enables.

Both levels use the same scored-tree algorithm; backward propagation scores an action higher when
its Enables chain leads toward the winning goal. The activity tree picks the intent; the action
tree inside it picks the next physical step.

---

## Wants (drives)

| Want         | Range | Rises when…                        |
|--------------|-------|------------------------------------|
| Nourishing   | 0–1   | time since last meal               |
| Hydration    | 0–1   | time since last drink              |
| Dental       | 0–1   | time since last gnaw               |
| Safety       | 0–1   | threat perceived                   |
| Comfort      | 0–1   | ungroomed time; soiled fur         |
| Social/Bond  | 0–1   | isolation time from trusted peer   |
| Territorial  | 0–1   | unfamiliar scent detected          |
| Reproductive | 0–1   | season + receptive partner nearby  |
| Energy       | 0–1   | rest surplus; playful arousal      |
| Curiosity    | 0–1   | novel stimulus; unvisited zone     |

---

## Activities

### Forage
```
Signature:    Curiosity 0.6 / Nourishing 0.4
RequiredPerceived: OpenArea | NovelZone
Movement:     Wander (random walk biased toward unvisited)
Actions:      [Sniff, Step, Periscope]
Enables:      Feed, Drink, Mark-Territory
```
Forage is the default "idle" activity. It seeds the others: a foraging rabbit that perceives
Edible transitions into Feed; perceiving Water transitions into Drink.

---

### Feed
```
Signature:    Nourishing 0.9
RequiredPerceived: Edible
Movement:     Toward
Actions:      [Sniff, Step, Bite, Chew]
Chains-after: Groom-Self (Comfort rises post-meal), Cecotrophy (morning only)
```

---

### Cecotrophy
```
Signature:    Nourishing 0.8 (micronutrient subtype)
RequiredPerceived: CecotropePresent (self-produced, morning)
Movement:     None (self-directed)
Actions:      [Reach, Bite-Cecotrope]
Note:         Skipped if interrupted; rabbit will retry immediately.
```

---

### Drink
```
Signature:    Hydration 0.9
RequiredPerceived: Water
Movement:     Toward
Actions:      [Sniff, Step, Sip]
```

---

### Chew
```
Signature:    Dental 0.8
RequiredPerceived: Chewable (wood, hay, cardboard)
Movement:     Toward
Actions:      [Sniff-Object, Bite-Object, Chew-Sustained]
Note:         Runs concurrently with Forage (opportunistic).
```

---

### Groom-Self
```
Signature:    Comfort 0.85
RequiredPerceived: none
Movement:     None
Actions:      [Lick-Paw, Wipe-Face, Lick-Fur, Scratch-Ear]
Chains-after: Rest
```

---

### Groom-Social
```
Signature:    Social/Bond 0.8
RequiredPerceived: TrustedPeer (proximity < threshold)
Movement:     Toward
Actions:      [Step, Nudge, Lick-Other]
Chains-after: Rest (mutual)
```

---

### Mark-Territory
```
Signature:    Territorial 0.85
RequiredPerceived: UnfamiliarScent | TerritoryEdge
Movement:     Patrol
Actions:      [Chin-Rub, Spray, Thump]
Note:         Spray only Applies() if reproductive-intact.
```

---

### Alert
```
Signature:    Safety 0.95  (interrupt priority — preempts all)
RequiredPerceived: ThreatStimulus
Movement:     None (Freeze)
Actions:      [Freeze, Periscope, Thump]
Chains-after: Flee (if threat confirmed) | Resume-Prior (if threat gone)
```
Alert is the only activity that can interrupt a committed action mid-tick.

---

### Flee
```
Signature:    Safety 1.0
RequiredPerceived: ConfirmedThreat
Movement:     Away (max speed)
Actions:      [Sprint, Hide, Binky-Escape]
Chains-after: Rest (Comfort spike after adrenaline drop)
```

---

### Rest
```
Signature:    Comfort 0.7 / Energy 0.3 (inverse — fires when Energy is low)
RequiredPerceived: SafeZone
Movement:     None
Actions:      [Loaf, Flop, Sleep-Cycle]
```

---

### Play
```
Signature:    Energy 0.8
RequiredPerceived: SafeZone | TrustedPeer
Movement:     Burst
Actions:      [Binky, Sprint-Zoomie, Box, Circle]
Note:         Low-stakes; aborts instantly on Safety spike.
```

---

### Court
```
Signature:    Reproductive 0.85
RequiredPerceived: ReceptivePartner
Movement:     Circle
Actions:      [Circle, Honk, Chase, Mount]
Chains-after: Breed → Rest
```

---

### Dig
```
Signature:    Territorial 0.6 / Curiosity 0.5
RequiredPerceived: SoftGround
Movement:     None (in-place)
Actions:      [Sniff-Ground, Scratch, Dig-Sustained, Tamp]
```

---

## Actions (atomic)

| Action          | Applies()                  | Effect (doer → target)                        | Enables                        |
|-----------------|----------------------------|-----------------------------------------------|--------------------------------|
| Sniff           | always                     | Perceive tags on nearby objects               | Step, Bite, Sip (if tag found) |
| Step            | always                     | Move doer Δpos toward/away target             | Bite, Sip, Lick-Other          |
| Periscope       | always                     | Expand perception radius; tag ThreatStimulus  | Step (new direction), Thump    |
| Bite            | target.Edible              | Nourishing +Δ on doer; target.Resource −Δ    | Chew                           |
| Bite-Cecotrope  | target == self.Cecotrope   | Nourishing(micro) +Δ on doer                  | —                              |
| Bite-Object     | target.Chewable            | Dental +Δ on doer; target.Durability −Δ      | Chew-Sustained                 |
| Chew            | follows Bite               | Nourishing +Δ (continued); teeth wear reset   | —                              |
| Chew-Sustained  | follows Bite-Object        | Dental +Δ (continued)                         | —                              |
| Sip             | target.Water               | Hydration +Δ on doer                          | —                              |
| Scratch         | ground.Soft                | Dig progress +Δ                               | Dig-Sustained                  |
| Dig-Sustained   | follows Scratch            | Burrow depth +Δ; Territorial +Δ on doer      | Tamp                           |
| Tamp            | follows Dig-Sustained      | Burrow entry sealed                           | —                              |
| Lick-Paw        | always                     | Paw.Clean = true                              | Wipe-Face                      |
| Wipe-Face       | follows Lick-Paw           | Face.Clean = true; Comfort +Δ                 | Lick-Fur                       |
| Lick-Fur        | always                     | Fur.Clean = true; Comfort +Δ                  | Scratch-Ear                    |
| Scratch-Ear     | always                     | Ear.Clean = true; Comfort +Δ                  | —                              |
| Nudge           | target.TrustedPeer         | Solicits grooming (sets target.Solicited)     | Lick-Other                     |
| Lick-Other      | target.TrustedPeer         | Bond +Δ on both; target.Comfort +Δ            | —                              |
| Chin-Rub        | target.Object              | target.ScentTag = doer.ID                     | —                              |
| Spray           | reproductive-intact        | area.ScentTag = doer.ID (strong)              | —                              |
| Freeze          | ThreatStimulus perceived   | doer.Velocity = 0; perception radius +Δ       | Periscope, Thump               |
| Thump           | ThreatStimulus perceived   | Broadcast AlarmSignal to nearby agents        | Flee, Periscope                |
| Sprint          | always (flee context)      | doer.Velocity = max                           | Hide, Binky-Escape             |
| Hide            | Cover.Available            | doer.Concealed = true; Safety +Δ             | —                              |
| Binky-Escape    | Sprint active              | doer.Direction = random (evasion)             | Sprint                         |
| Binky           | Energy high, Safe          | doer.Joy signal; Energy −Δ                    | Sprint-Zoomie                  |
| Sprint-Zoomie   | follows Binky              | doer.Velocity = max (random path)             | Binky                          |
| Box             | target nearby              | target.Boundary signal; Energy −Δ             | Circle                         |
| Circle          | target nearby              | doer orbits target; Reproductive/Energy −Δ   | Honk, Mount, Box               |
| Honk            | Court context              | Broadcast CourtSignal to target               | Mount                          |
| Chase           | target.Fleeing             | doer pursues target                           | Mount                          |
| Mount           | target.Receptive           | Breed event fired                             | —                              |
| Loaf            | SafeZone                   | doer.PostureAlert; Comfort +Δ                 | Flop                           |
| Flop            | follows Loaf (Comfort hi)  | doer.PostureRelaxed; Comfort +Δ               | Sleep-Cycle                    |
| Sleep-Cycle     | follows Flop               | Energy +Δ; Comfort +Δ (short cycle, ~10 min) | Loaf (on wake)                 |
| Reach           | self (cecotrophy)          | doer contorts to access cecotropes            | Bite-Cecotrope                 |
| Sniff-Ground    | Ground nearby              | Perceive SoftGround tag                       | Scratch                        |
| Sniff-Object    | Object nearby              | Perceive Chewable tag                         | Bite-Object                    |

---

## Activity Chains (coarse flow)

```
Forage ──perceives Edible──→ Feed ──post-meal──→ Groom-Self → Rest
       ──perceives Water──→ Drink
       ──perceives Scent──→ Mark-Territory

Alert (interrupt) ──threat confirmed──→ Flee → Rest
                  ──threat gone──────→ Resume prior activity

Feed ──morning + cecotrope──→ Cecotrophy → Groom-Self → Rest

Rest ──Energy surplus──→ Play (Binky / Zoomie loop)

Forage / Play ──partner nearby──→ Court → (Breed) → Rest

Forage ──SoftGround──→ Dig
```
