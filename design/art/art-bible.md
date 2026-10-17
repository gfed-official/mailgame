# Art Bible: Performative Mail (working title)

> **Status**: Under Review
> **Owned By**: art-director
> **Last Updated**: 2026-10-15
> **Art Director Sign-Off (AD-ART-BIBLE)**: CONCERNS 2026-10-15 → REVISED 2026-10-15 (all 11 concern items applied; §1 tone reconciliation flagged for creative-director)

`/art-bible` writes these nine sections in this order and replaces each
section's `[To be designed]` line as that section is approved. A section still
holding its placeholder is incomplete; a later run fills only those.

## 1. Visual Identity Statement

**One-line visual rule: "Every parcel is a toy; every mistake is a prop gag."**

Everything the player touches is chunky, saturated and toy-like, and every delivery outcome — good or bad — plays out as a visible physical event in the world, never only as a HUD number. The silliness carries the identity (Muck / Crab Game lineage); the clarity discipline of the principles below keeps it readable at eight players and a belt-heavy factory (Satisfactory lineage).

**Tone: full slapstick.** Exaggerated faces, cartoon physics, loud UI. The post office and the rival courier firm are both overtly goofy; nothing is played po-faced. (Owner decision, 2026-10-15. The alternative considered was deadpan-corporate "absurd seriousness"; it was not chosen.)

**How this squares with the pillars' "a silly job played straight":** the *carriers* are earnest — they take the job seriously and never wink at the camera — while the *world's* reactions, physics and UI are the slapstick. Earnest people, absurd consequences. (Flagged for creative-director: this is the art-director's reading of the pillar; if the pillar means the whole presentation is deadpan, it conflicts with the Tone decision above and the pillar wins pending the creative-director's call.)

**Enemy fiction (closes spec 13 §7):** the raiders are a **rival courier company's hired goons** sabotaging the player's routes. They are people in a branded uniform, not monsters. The Clash-of-Clans roster names (Barbarian, Archer, Giant, Wall Breaker) are placeholders to be re-dressed as rival-uniform archetypes distinguished by silhouette and gear.

### Supporting principles

1. **Address before ornament** — serves Pillar 1, *The mail must flow*.
   *Design test:* when a decoration, texture or prop competes with an address or destination marker, choose the address. Every mailbox and house carries a large, high-contrast colour-and-shape glyph (roof or door) readable from throwing distance (~20 m+); address text is a secondary, close-range layer.

2. **Honest outcomes are physical** — serves Pillar 2, *Friendslop surface, factory depth*.
   *Design test:* when a delivery result is ambiguous, choose the version shown in the world, not only in the HUD. A correct delivery pops the mailbox flag and the box does a satisfied bounce; a misdelivery makes the wrong house visibly react (wobble, confused resident); the throw arc keeps its green / red / white language and nothing softens a bad throw.

3. **Chunky silhouettes, flat state colour** — serves Pillar 5, *Scales from 1 to 8*, and Pillar 3, *Hands beat machines, until they don't*.
   *Design test:* when two designs have equal appeal, choose the one with the more distinct silhouette at 30 px tall. Machines get exaggerated Satisfactory-like forms; belt and sorter state uses a small fixed palette (running / blocked / idle) on emissive panels, not on the whole machine. Players differ by hat or silhouette first and tint second, so eight teammates stay distinct when colours collide. An operated machine or vehicle shows a visible driver.

### Rival-courier visual implications

One hostile accent colour, one logo, matching vehicles; none of it may collide with the player palette. Each raider type is told apart by silhouette and gear. The sky-dim and red shift timer (spec 13 §4.5) already announce a raid; the enemy kit's job is to read as *hostile people*, in slapstick register.

### Known tensions (to be resolved in sections 3, 4 and 7)

- **Low-poly vs address legibility** — text on small faces fails at distance with eight players and belts on screen; glyph + colour codes first, text second.
- **Saturation budget** — slapstick tends toward uniform brightness; reserve the loudest colours for gameplay-critical information (addresses, machine state, raid threat) and keep the environment mid-saturation.
- **No day/night cycle** — the sky-dim is the only in-run lighting change available; normal lighting must stay stable so the dim reads strongly.

## 2. Mood & Atmosphere

**Baseline lighting (never changes during a run outside a raid).** One fixed sun, ~40° elevation, from the south-west so every house's address face (roof + door, see §3) is lit. Key light warm (≈5200 K), sky fill slightly cool, soft-edged shadows, no fog. This stability is deliberate: the raid dim is the only *in-run* lighting event we own (spec 13 §4.5) — the Lobby set and the Run Over desaturation are out-of-run and do not compete with it — so everything else must be a flat, cheerful constant for the dim to land. Artists: do not add time-of-day, weather or local lighting moods per district — district identity is carried by colour and shape (§4), not by light.

| State | Emotional target | Lighting | Adjectives | Energy | Mood-carrying element |
|---|---|---|---|---|---|
| **Lobby / menus** | "The break room before the shift": banter, dressing up, anticipation | In-world set, warm interior tungsten pool against a bright window; the only place warmer than the baseline | cosy, scuffed, chatty, hand-labelled | Low–medium | Your carrier and hat live on a coat-hook wall; cosmetics are tried on in the room, not in a menu |
| **Prep** | "Morning before the rush": calm with a ticking clock | Baseline | tidy, fresh, expectant, chirpy | Medium-low | The new district's coloured curbs and its ghost outline; the PO shop's OPEN flip-sign; idle machines with unlit panels |
| **Delivery** | "The rush": joyful overload, mail everywhere | Baseline, unchanged | busy, bouncy, breathless, flappy | High | Motion: throw arcs, mailbox flags popping, belts rolling, mail bouncing off roofs; no static props may move during Delivery so player-caused motion stays the signal |
| **Raid** | "The goons are here — hold the line, laughing" | **The only in-run change:** sky and ambient drop to ~55 % luminance and tint toward slate-blue; sun intensity to ~70 %, direction unchanged; a ground haze tinted the *same slate* as the sky shift rolls from the spawn edge only, so it reads as weather arriving, never as the rival's colour on the ground (owner decision 2026-10-15). **Stays stable:** sun direction, all emissive panels, address glyphs (unlit shader, so legibility never drops), player tints, mail colour | squally, rowdy, cluttered, cartoon-menacing, urgent | Peak | Magenta vests, vans and van headlights at the perimeter — the only magenta in frame, warm against the slate haze; exploding "FRAGILE" boxes; shift timer red (spec 13 §4.5) |
| **Payday / Draft** | "Clock-out": relief, appraisal, a little greed | World returns to baseline the instant the raid ends (0.5 s ramp); gold-framed HUD takes the stage | gilded, tallied, stamped, breezy | Medium | Earnings counter as a coin-fed stamp machine; draft cards drawn as oversized postage stamps |
| **Results — Victory** | Giddy triumph, team pose | Baseline + gold confetti and a raised PO flag | triumphant, gilded, goofy, loud | High, short | The whole crew posed on the PO steps with their hats; the rival van limping away |
| **Results — Run Over** | "Fired, but funny" — the gag, never grim | World desaturated 40 % and tinted cool (world-only post-process; **VERIFY-IN-ENGINE**, Godot 4.7.1, that the desaturation leaves the HUD and the RETURN TO SENDER stamp untouched); one hard spotlight on the PO | deflated, stamped, drizzly, sheepish | Low | A giant **RETURN TO SENDER** stamp slams across the screen; the Post Office slumps (prop gag, principle 2) |

**Swimming / water.** A gag, not a hazard (no drowning). Water is flat-shaded Tier-C blue (§4) with a white foam ring; the player bobs, the hat floats beside them. No tint change — the inability to throw or build is communicated by the HUD, not the mood.

**In a vehicle.** "Momentum": dust puffs, visible driver (principle 3), headlights that switch on only during the raid dim so the truck reads as a mobile safe spot. A stalled vehicle in water sits nose-down, hazard lights blinking, for the 5 s before teleport.

Each state is distinct by *what moves and what colour owns the HUD*, not by lighting — the budget only allows one *in-run* lighting change, and the raid gets it; the Lobby set and the Run Over desaturation sit outside the run and outside that budget.

## 3. Shape Language

**Characters: hat first, tint second (principle 3, Pillar 5).** Carriers are 2.5 heads tall, no neck, oversized hands (throwing is the verb), feet as rounded blocks. At 30 px the silhouette must resolve to *head + hat + hands*. Eight hat silhouettes, one per player slot, chosen so no two share a top profile: peaked postal cap, pith helmet, bucket hat, beanie with bobble, hard hat, cowboy hat, bicycle helmet, headband-with-antenna. Uniform is always Postal Blue; the player tint goes on hat band, gloves and backpack only. Test: render all thirteen head silhouettes in greyscale at 30 px — the eight player hats, the four goon headgears (backward cap, floppy sun hat, overhead pallet, box-with-legs; table below) and the NPC driver's pork-pie cap with ear defenders (§5) — and all thirteen must separate; if any pair is confusable, change the hat or headgear, never the tint.

**Rival goons: people, not monsters (identity statement; Pillar 1 — combat serves mail).** Same 2.5-head proportion as players so they read as *rival humans*, plus the magenta sash and logo. Each archetype is one exaggerated prop:

| Placeholder | Re-dress | Silhouette trait |
|---|---|---|
| Barbarian | **Runner** (melee swarmer) | Lean, forward-leaning sprint, backward cap, clipboard-club |
| Archer | **Flyer Tosser** (ranged) | Floppy down-turned-brim sun hat (distinct from the cowboy slot's up-turned brim), crouched, chest satchel, rubber-band launcher |
| Giant | **Pallet Jack** (siege) | Top-heavy: a stacked pallet carried overhead as a battering ram |
| Wall Breaker | **Rush Delivery** (suicide) | A "FRAGILE" box bigger than the carrier — a box with legs |

Megas are the same silhouette at ×1.4 plus a "Manager" tie and a magenta aura ring; no new shapes.

**Environment: the house is an address first (principle 1).** House = box + oversized roof prism, roof ≥ 55 % of the silhouette height, because the roof is the district colour field. Door is 1.3× human scale and carries the district **shape glyph**; the mailbox stands 1.2 m tall at the kerb, flag the size of a dinner plate, its face showing the same glyph above a large house number. This is the low-poly vs legibility resolution: at 20 m+ you read roof colour → district, glyph → district confirmed, stripe → street; number text is a 3 m-and-closer layer (spec 03 §1.4). No house carries ornament on the roof or door face. The Post Office is the sole landmark: tallest building, a clock-tower with a postal horn, visible from every district edge. Roads are flat with chunky rounded kerbs (district colour); the island has rounded, flat-shaded cliffs and beaches — soft forms so hard-edged constructs stand out.

**Machines: Satisfactory chunk, slapstick proportions (Pillar 3).** Belts: wide, visible rollers, mail rides on top, one emissive edge strip for state. Sorters: a hopper with a giant filter-label face. Inserters: a bulbous-headed arm with an exaggerated swing. Depot: a warehouse with a mouth. Truck: snub-nosed, boxy, big wheels, open driver window (principle 3). Walls: palisade logs / stone blocks, flat tops. Turret: a mail-cannon on a tripod. All machines are blocky and orthogonal so they never read as houses.

**UI grammar.** Containers: rounded rectangles (r = 12 px at 1080p), 3 px outline. Interactive: pill shapes with a heavier outline. Alerts (raid, Mega sighted): an eight-point burst. Icons: single-weight filled silhouettes. Postage-stamp perforation reserved for draft cards and the results header.

**Hero vs supporting.** Heroes: the parcel, the mailbox, the Post Office, and during a raid the goons. Secondary: machines and vehicles. Supporting: houses, trees, roads, water — soft, low-detail, quiet. The eye goes to the mail.

## 4. Color System

**Primary palette (8).**

| Name | Hex | Role |
|---|---|---|
| Parcel Kraft | #C89A5E | Mail, cardboard, the toy. Dominant on everything carried |
| Postal Blue | #2457B8 | Player side: uniform, PO, friendly constructs' bodies |
| Stamp Red | #D7263D | Wrong / blocked / threat. Reserved — never decorative |
| Go Green | #3DBF5A | Right / running / go |
| Chalk White | #F4F1EA | Neutral: ground-arc, idle, house walls, paper |
| Payday Gold | #F2B705 | Money, quota, Postal Rank, victory |
| Rival Magenta | #C2187A | The rival courier: sashes, logo, vans, van headlights — people and vehicles only |
| Charcoal | #23252B | Outlines, construct base skirts, belt frames, the mouth line, UI panels. The one near-black; nothing else may be darker |

**Named secondaries.**

| Name | Hex | Role |
|---|---|---|
| Goon Grey | #3A3D46 | Rival goon jumpsuit only. Sits between Charcoal and the Tier C band so a goon body is darker than any environment surface but never reads as an outline or a skirt |

**Semantics.** Arc: green = matching mailbox, red = non-matching, white = ground (spec 09 §3.2, spec 13 §4.1). Belt/sorter panels: running = green with moving chevrons; blocked = red, static, with an X; idle = unlit Chalk White, no motion. Raid: red shift timer, red-filling complaint envelope, magenta for *people and vehicles*. Gold is HUD-only; it takes the stage at Payday/Results (wallet and stack counts use it all shift). Rule: **red means "you got it wrong", magenta means "they did it to you" — never swap.**

**Cursed Mail (Postage Stamp modifier, spec 03 §1.1):** glow is **acid yellow-green #B5E61D** with a skull stamp icon on the label — *not* purple. (Owner decision 2026-10-15: purple collided with Rival Magenta and Violet D4 for protan/tritan players. Cursed = sickly, never rival.)

**Saturation budget (resolves the §1 tension).** Saturation is HSV saturation as read in a colour picker; L* is CIELAB lightness. Tier A (loud, ≥ 55 % sat — every district hex clears it, Violet D4 at 55 % is the floor; owner decision 2026-10-15 to keep the palette rather than raise the floor to 80 %): address glyphs (roofs, doors, mailboxes, kerbs), street stripes, state panels, arc, raid cues, HUD alerts, cursed glow; Chalk White elements in Tier A (ground arc, idle panels) carry value, not chroma, and are exempt from the floor. Tier B (40–60 %): mail, machine bodies, vehicles, resident pyjamas; **Postal Blue #2457B8 (80 % sat) is the named Tier A exception worn on bodies, the PO and constructs** — its job is to be the loudest thing on a carrier after the hat band. Tier C (≤ 35 % sat **and** L* 55–70): grass, water, sky, roads, cliffs, trees. The separation between tiers is numeric, not by hue family: a Tier C colour may sit in any hue family — a Tier C blue sky under a Postal #56B4E9 roof is fine — because at ≤ 35 % sat it never reaches district chroma, and the L* band keeps it a quiet mid-tone stage under a Tier A roof and above a Charcoal-outlined carrier. Test: pick the colour's swatch; if the picker reads above 35 % or outside L* 55–70 it is not Tier C, whatever its hue.

**House walls are the named exception to the Tier C L* band.** Walls stay at ≤ 20 % sat but sit *above* the band, at L* 80–95: Chalk White #F4F1EA (L* ≈ 95) or one of six named wall tints. Walls are the backdrop the dark, outlined carrier must separate from (§6 legibility test), so they are deliberately the lightest large surfaces in the world; nothing else in Tier C may leave the band.

| Wall tint | Hex | HSV sat | L* (approx.) |
|---|---|---|---|
| Cream | #F3EBD3 | 13 % | 93 |
| Putty | #D9D2C5 | 9 % | 84 |
| Sand | #E6D9BC | 18 % | 87 |
| Dove | #D6D9DE | 4 % | 86 |
| Oatmeal | #E3DCCB | 11 % | 88 |
| Clay | #D9C3B4 | 17 % | 80 |

**District system (5, unlock per shift) — colour + shape, always paired.** (Okabe–Ito-derived.)

| District | Colour | Glyph |
|---|---|---|
| 1 (PO) | Postal #56B4E9 | Circle |
| 2 | Tangerine #E69F00 | Triangle |
| 3 | Jade #009E73 | Square |
| 4 | Violet #7E57C2 | Cross |
| 5 | Butter #F0E442 | Diamond |

*Postal* (the D1 district colour, light sky-blue #56B4E9) and *Postal Blue* (the uniform and PO, #2457B8) are different hexes in one family and never swap surfaces: the district colour goes on roofs, doors, kerbs and mailboxes; Postal Blue goes on bodies, the PO and constructs. Shared hue is intentional — District 1 is the PO's own district — and the swap is caught by the §3 rule that a roof is never a body colour.

Streets: stripe from the 8-hue street set below (spec 09 §5 fixes the count and the patterns, not the hexes; these are the art-director's proposal, Tier A, all *deep* — L* 30–58 — so a stripe is never mistaken for a district swatch or a player tint, both of which are light; owner decision 2026-10-15: eight hues with exclusions rather than six) with a pattern (solid / stripes / dots / chevrons); within a district no two streets share a hue *or* a pattern.

| Stripe | Hex | Not used in |
|---|---|---|
| Rust | #C2461B | — |
| Chocolate | #6E3A14 | — |
| Mustard | #B38600 | D2 Tangerine, D5 Butter (same hue family as the swatch) |
| Olive | #7A8C12 | — (same family as Cursed #B5E61D but ~30 L* darker and never pulses) |
| Forest | #167A1F | — |
| Petrol | #0E7C86 | D1 Postal (same hue family as the swatch) |
| Cobalt | #2A3BE6 | — |
| Plum | #6B1F9E | D4 Violet (same hue family as the swatch) |

A stripe is excluded from a district when its hue family matches that district's own swatch, so a label never shows the same family twice; every district still has ≥ 6 hues × 4 patterns. The street set and the player-tint set are **different sets with no shared hex** (stripes deep, tints light); the two never share a surface — stripes live on labels, signs and mailbox faces, tints on bodies — so adjacent hues across the sets (Rust / Brown, Petrol / Teal, Plum / Lavender) are separated by value wherever they could meet. The label is swatch-with-glyph + patterned stripe (spec 03 §1.4), so a fully colourblind player can still sort by shape and pattern. The "force patterns" accessibility setting (spec 09 §5) makes glyph and pattern 1.5× larger.

**Player tints (hat band, gloves, backpack only).** Eight, light (L* ≥ 60 except Brown), none shared with the street-stripe set:

| Tint | Hex |
|---|---|
| Sky | #4FC3F7 |
| Orange | #FF8C1A |
| Lime | #8BE03A |
| Lemon | #FFF14D |
| Teal | #2BD9C4 |
| Lavender | #B99CFF |
| White | #FFFFFF |
| Brown | #A0622D |

Tint overlap with district colours (Sky / Postal D1, Lemon / Butter D5, Lavender / Violet D4) is acceptable because the hat profile, not the tint, is the player ID (§3), and a tint is never on a roof, door or kerb. No pink/magenta anywhere in the player set; rival magenta stays exclusive and goons never wear Postal Blue.

**UI palette.** Charcoal #23252B panels with Chalk White text; it is the one place the world's mid-tone greys are abandoned so Tier A accents (gold, red, green) pop on a dark ground. The HUD never uses district colours except on the compass bands and address labels.

**Colourblind failures and backups.**

| Pair | Fails for | Backup |
|---|---|---|
| Arc green / red | Deutan, protan | Green arc is solid with a ring landing marker; red arc is dashed with an X marker; distinct hit/miss sound |
| Belt running / blocked | Deutan, protan | Motion (chevrons) vs static X; blocked belt ticks audibly |
| Tangerine D2 / Butter D5 | Tritan | Triangle vs diamond glyph; glyph only — different kerb heights not allowed |
| Jade D3 / Violet D4 | Protan | Square vs cross glyph |
| Rival Magenta / Violet D4 | Protan, tritan | Magenta is on people and vehicles only, never on buildings or ground |
| Gold / Lemon tint | Mild | Gold is HUD-only; tint is on a hat |
| Cursed #B5E61D / Butter D5 / Lime tint | Tritan, mild | Skull stamp icon; cursed glow pulses, Butter and Lime never pulse |

Everything above is checkable: an artist can hold a swatch, a glyph, a saturation tier and a 30 px render against each rule.

## 5. Character Design Direction

**One rig for every human.** Players, goons, NPC drivers and residents share the §3 body: 2.5 heads, 1.4 m tall (head 0.56 m, a rounded cube), no neck, mitten hands with a thumb (three fingers + thumb) at 1.5× head width, block feet. One skeleton, one animation set, one expression atlas, budgeted for 48 humans in a shift-5 raid. Who someone is comes from four slots an artist can check: **head cover, torso colour, back, carried prop**.

**The carrier.** Postal Blue short-sleeve shirt and shorts, Chalk White socks, Kraft satchel-backpack (the only backpack in the game), slot hat (§3). Tint on hat band, glove cuffs, backpack straps. Skin: six flat tones, unshaded. Name tag: Chalk White chest plate, legible ≤ 6 m only; the hat is the ID.

**Face rig.** One front quad on the head with a 4×4 expression atlas swapped by UV offset; no blendshapes, no facial bones. Ten cells: neutral, blink, grin (delivered), strain (wind-up), shock (hit), dizzy (knocked down), smug, panic (raid horn), wet (swimming), asleep (idle 20 s). Eyes are two ovals ≥ 25 % of face width, mouth a thick Charcoal line; it reads at ≤ 10 m and vanishes by design at 20 m. The third-person camera sits behind your own carrier, so emotion at distance goes to **hat and body, not face**: hat lifts 15 cm on shock, flattens on strain, flies off on knockdown (returns on respawn); body squash 0.8–1.2 on landings and catches; limbs stretch ≤ 1.3× on throws. Hat and squash are two bones on the shared rig.

**Pose style: exaggerated, with holds.** Full slapstick (§1) and the 30 px test both rule out stiff or naturalistic motion: a pose must read as a silhouette. Every animation obeys anticipation → snap → hold: wind-up ≥ 0.2 s, action ≤ 0.1 s, hold ≥ 0.15 s. Test: pause any frame; the character must be in a pose you could cut from paper and name.

**Carry / throw / catch (throwing is the verb).** Carried items sit at or above shoulder height, forward and 20° to the dominant side, so the over-shoulder camera always sees the parcel past the hat; nothing is carried at the hip. Light: one hand aloft, waiter-style. Medium: hugged to the chest, chin on top. Heavy: overhead, knees bent, waddle. Throw: lean back 20° with arm cocked (strain face), full extension with rear foot off the ground, 0.15 s follow-through pointing at the arc. Catch: both arms up, palms out, parcel squashes on contact, body dips. Miss: parcel bounces off the hat. Goons share the set; the Flyer Tosser's launcher and the Pallet Jack's pallet are the light and heavy poses with a prop.

**Telling people apart at a glance.**

| Who | Head cover | Torso | Back | Prop / where |
|---|---|---|---|---|
| Teammate | Slot hat (8 profiles) | Postal Blue | Kraft backpack, tint straps | Parcel; anywhere |
| Rival goon | Archetype headgear (§3) | Goon Grey #3A3D46 jumpsuit, Rival Magenta sash shoulder-to-hip | None | One oversized prop |
| NPC driver | Flat pork-pie cap with ear defenders (not a slot profile) | Postal Blue, no tint, grey gloves, "TEMP" tag | None | Only in a truck seat; one face (whistling; panic when fleeing) |
| Resident | **No hat**: hair silhouette, 6 variants | Tier B striped pyjamas or dressing gown, non-district hues | None | Mug, newspaper, curlers; doorway or window only, 3 s, holding the wrong parcel |

Rules: hat = works for somebody; bare head = civilian; backpack = player; sash = hostile; Postal Blue never on goons, magenta never on players. Residents stand inside the 1.3× district-coloured door, so Tier B pyjamas read against a Tier A door, not the white wall.

**LOD.** Three LODs, same thresholds as houses. LOD0 (≤ 10 m): face atlas, fingers, name tag, hat detail. LOD1 (10–35 m): eyes become two dots, hands become mitts, tint and backpack stay. LOD2 (> 35 m, ≈ 30 px at 1080p): head + hat + hands + backpack/sash as one mesh, tint band kept. All characters and carried items get a 2 px Charcoal outline (inverted hull, scaled to 1080p) and a blob shadow; nothing else in the world is outlined (except the screen-space throw arc, §7). **The outline is the rule; §8 records the technical-artist's Forward+ feasibility check at 48 characters and any fallback.** Poly counts belong to §8.

**Cosmetics (spec 13 §2.2: hats and a few uniform colours).** The body stays Postal Blue; "uniform colours" are **trim only** — the tint set on band, gloves and straps, plus collar and socks as unlocks (owner decision 2026-10-15). Hat unlocks are **variants within a slot's profile family** (peaked cap → captain's cap; bucket → fisherman's) so §3's thirteen-silhouette greyscale test still passes without per-lobby re-checks; every unlock must pass it before it ships (owner decision 2026-10-15).

## 6. Environment Design Language

**Architecture: a postcard town that worships its post office.** The culture is "everyone is waiting for the mail": the PO is the town's cathedral (tallest, clock tower, postal horn, flagpole, a carrier statue holding a parcel aloft on its square), and each house's proudest object is its mailbox, the best-painted, most upright thing on the lot. Houses use the §3 box + oversized roof from a 12-variant kit: 3 footprints (square, long, double) × 2 roofs (steep gable, hipped) × 2 heights. Roof = district colour; per-house variation goes to the wall (Chalk White or one of the six named wall tints in §4 — Cream, Putty, Sand, Dove, Oatmeal, Clay — all ≤ 20 % sat at L* 80–95, the named exception to the Tier C value band), window count, and one chimney on the rear ridge only. Corner-lot shops are post-adjacent (stamp shop, Tape & String, Parcel Cafe) with pictorial signs only, so no text competes with addresses. Period: vaguely 1950s seaside; no cars but mail trucks, no power lines, bunting instead.

**Texture philosophy: flat colour, one palette atlas.** No PBR surface textures, no hand-painted detail. Every environment mesh is flat colour from one 256² palette atlas per saturation tier, constant roughness, baked soft AO at the base. Allowed: hard two-tone colour breaks (wall/trim, dry/wet sand). Forbidden: any pattern with features smaller than 25 cm in world space (grass noise, brick, wood grain, shingles). Exceptions: address atlas text (≤ 3 m), emissive state panels, the 60 m+ belt scroll texture, water foam ring, raid haze. Why: one material per tier batches for 60 fps with 8 players, belts and 40 goons; it holds Tier C in a quiet value band (L* 55–70); and painted or PBR detail reads as micro-saturation and noise at 30 px, exactly what principle 1 forbids.

**Prop density (client dressing, spec 02 §5).**

| Area | Rule |
|---|---|
| PO yard (6×6 PO + 20×20-tile clearance) | Zero dressing inside the clearance; the PO's own set only (flagpole, dock, OPEN sign, statue). Lamps on the perimeter kerb. The yard is the players' factory canvas |
| Residential street | ≤ 3 props per lot (fence run = 1, tree ≤ 1, one small: bench/bin/planter). Nothing within 2 m of the mailbox; nothing > 1 m tall between kerb and door face (the address lane). Lamp every second lot, one side |
| Forest / rocky harvest | Nodes are the density. Dressing ≤ 1 per 50 m², never above 60 % of node height; nodes Tier B and chunky, dressing Tier C |
| Beach / cliff | ≤ 1 prop per 100 m² on beaches (driftwood, deckchair, a crab); cliffs zero. Perimeters are spawn edges: kept empty so arriving goons read |
| Roads with belts | Roads Tier C warm grey, no paint markings anywhere, so the belt's emissive strip is the only line on a road. No dressing within 1 m of a road edge. Dressing on a buildable tile is "crushable": hidden when a construct occupies the tile |

**Environmental storytelling, no text.** Played-straight PO world: mailbox pride, the statue, benches with the horn, a "days without misdelivery" tally board on the PO (numerals only), Postal Blue and Chalk White bunting, a noticeboard. Rival story: one wrecked rival van at each spawn edge, visible from the district edge (a vehicle, so magenta is legal); the Manager's wanted poster in the lobby set; after a raid, the Flyer Tosser's Chalk White flyers litter the ground until Prep sweeps them. Magenta never touches ground or buildings (§4).

**Five districts beyond colour.** Each district owns a glyph echo in its kit weighting, one tree silhouette and one signature prop.

| D | Glyph echo | Tree | Signature |
|---|---|---|---|
| 1 Postal | Round: globe lamps, ball topiary, hipped roofs | Lollipop | Flower planters |
| 2 Tangerine | Triangle: steep gables dominate, A-frame bins | Conical pine | Pointed-picket fences |
| 3 Jade | Square: flat boxes, cube hedges | Box-clipped | Grid trellis |
| 4 Violet | Cross: two-storey, crossed-timber trim as sole wall detail | Tall poplar | Crossed-pole washing lines |
| 5 Butter | Diamond: low beach houses, lattice fences | Palm | Bunting |

Test: greyscale a screenshot; tree shape and roof pitch alone must name the district.

**Constructs against the environment.** Environment is soft, rounded, outline-free, non-emissive, Tier C. Constructs are orthogonal, Tier B, and each carries Postal Blue on ≥ 15 % of visible surface or as a continuous cap stripe (palisade and stone walls get the stripe so they never read as trees or cliffs). Every construct sits on a 10 cm Charcoal base skirt; no environment object has one. Belts: Charcoal frame, Kraft mail, one emissive strip.

**Ruins and damage.** < 50 % HP: one Chalk White sticking-plaster decal; < 25 %: two, crossed. Ruin (0 HP): the construct's LOD2 mesh desaturated to grey, tilted 8°, sunk 20 % into the ground, plasters kept, a looping Chalk White cartoon smoke puff. A prop gag, never debris or scorch; no red (that means "you got it wrong"), no magenta (never on buildings). Prep rebuild pops it upright with a squash. Vehicle wrecks: same, wheels off.

**Legibility against characters (hard requirement).** The environment never carries detail below 25 cm, never an outline, never emissive, and stays in L* 55–70 (house walls excepted *upward* to L* 80–95, §4 — which is why this test includes a wall); characters are L* ≤ 45 bodies (Postal Blue, Goon Grey) with a 2 px Charcoal outline, a blob shadow and a Tier A hat band. A carrier is therefore the darkest, hardest-edged, most-saturated-topped moving thing in frame. Test: 30 px greyscale render over grass, sand, road and a Chalk White wall; the figure must separate on all four.

**Dependencies on other systems** (recorded 2026-10-15 for the owning GDDs to pick up):
- **World Generation** (spec 02 §3.5, `design/gdd/` World Generation GDD): per-district house-kit variant weighting and tree/signature-prop assignment per district.
- **Building System / Presentation & World Dressing**: "crushable" dressing props hidden when a construct occupies the tile; the 10 cm base skirt on every construct; ruin and damage-decal states driven by construct HP (Defenses & Construct Damage).

## 7. UI/HUD Visual Direction

**Diegetic vs screen-space — the rule.** If a thing is *about a point in the world*, it lives in the world; if it is *about the team's shift*, it lives on screen. In-world: mailbox flags, machine state panels, throw arc, speaking icon and name tag over heads, pings (3D marker + compass echo), ghost previews, damage plasters. Screen-space: shift/phase timer, quota, wallet, complaint envelope, compass, hotbar/inventory, event feed, toasts, interact prompt. Nothing is duplicated except pings (world marker for position, compass for direction). Principle 2 holds: a HUD number is never the *only* report of an outcome.

**Named exceptions to the rule (owner decisions 2026-10-15):** (a) the throw arc is screen-space-rendered and carries a 2 px Charcoal outline — the one outline outside §5's characters-and-carried-items rule; (b) **blocked belts/sorters get a compass badge** at the lowest compass priority, because machine panels are unreadable past ~25 m.

**Typography.** Personality: a hand-stencilled parcel stamp, not a corporate sans. One family, a rounded geometric grotesque with a stencil-cut display weight (Black) above a clean text weight (Medium/Bold); think "rubber-stamped" at the top, "typed label" below. Hierarchy at 1080p / 100 %: Display 48 px Black (RETURN TO SENDER, Payday totals), Heading 28 Bold, Body 20 Medium, Label 16 Medium, Micro 14 Medium — **hard floor 14 px; minimum body 16 px, secondary 14 px**; text scale (spec 09 §5) grows the UI, never the floor. Per spec 03 §1.4, address labels, street signs, mailbox numbers and the inventory address chip all use the Bold weight of this same family, tabular numerals, Charcoal on Chalk White — the one typographic voice shared by world and HUD. In-world address text cannot follow the text-scale setting: the 2.5 m interact prompt (which does scale) is the authoritative read, in-world numerals are flavour, mailbox numeral cap height ≥ 20 cm; an optional "large address text" toggle swaps to a second atlas.

**Colour on the HUD (contrast rule).** Chalk White on Charcoal (~13:1) is the text pairing. **Stamp Red and Rival Magenta are never text colours** (3.1:1 and 2.7:1 on Charcoal fail); they are fills behind Chalk White text or glyphs — the raid timer, the raid-edge compass icon. Panel outline is light grey **#5A5E6B** (not Charcoal) so panel edges survive the 55 % raid dim. The HUD renders above post-processing; the raid haze never touches it (**VERIFY-IN-ENGINE**, Godot 4.7.1 — that a CanvasLayer HUD composites after the 3D viewport's post-process and the haze pass is training-era 4.x knowledge, not sourced for 4.7). Compass declutter priority: raid edge > construct under attack > pings > teammates/voice icons > blocked-machine badge.

**Iconography.** Single-weight filled silhouettes, Chalk White on Charcoal, 2 px inner Charcoal gap when stacked. Mail kinds are *shape* families: Letter = flat rect, Parcel = cube, Large = tall box, Cargo = crate with slats, Cursed = any of these with the skull stamp. Construct types are *orthogonal profile* icons matching §3 (belt = two rollers, sorter = hopper with label face, turret = tripod cannon). Perks are postage stamps: perforated frame, rarity as frame colour (Chalk / Go Green / Postal Blue / Payday Gold), category badge as a cancellation mark across one corner. **Perforation is a border only, ≥ 8 px clear of text, never on HUD widgets or the prompt.**

**Animation.** Anticipation → snap → hold, same timings as §5: squash 0.2 s, pop ≤ 0.1 s, hold ≥ 0.15 s. Bounces: toasts, wallet increments, mailbox flag, complaint envelope shake, draft cards slamming. Never moves: timer digits, compass bands, quota bar outline, address text. §2's Delivery rule reads "no ambient/idle motion" — player-caused world reactions and HUD motion are exempt. **Reduced motion** (spec 09 §5): all travel → 0.1 s opacity cuts; shakes → a single outline flash plus a count; timer pulse → static red fill. **Alerts and photosensitivity:** the eight-point burst appears once with a ≤ 0.3 s scale-in, no strobe, one alert queued at a time; raid pulse ≤ 1 Hz, opacity 100 → 80 %, replaced by a held static state under reduced-flash; the sky dim ramps in over ≥ 1 s as a single transition.

**Grid inventory.** Cells 56 px, **r = 6 px** (r = 12 reads as a pill, and §3 says pill = button; panels keep r = 12), 2 px Charcoal gutters on a Charcoal-light #2E3138 well. **Focus is thickness-based, never colour-based:** a 4 px Chalk White ring *outside* the 3 px cell outline plus a corner caret; hover = ring alone; selected/dragging = ring + 60 % lift; no gold, no district hue; the ring scales with the cell at 150 % text. Stack count bottom-right in Label weight, Payday Gold. Mail cells carry a swatch-with-glyph tab top-left and the patterned street stripe along the bottom edge (district colours are permitted here as labels — the §4 clause bars decorative use); sort-by-address orders by district hue then stripe so a sorted grid reads as a colour gradient. Pending moves at 60 % opacity with a dashed outline. At ≥ 125 % text the two-address interact prompt stacks vertically; "force patterns" 1.5× plus 150 % text is capped at 2× label size.

**Throw arc (screen-space).** Green = matching mailbox, red = non-matching, white = ground (§4). The line carries the 2 px Charcoal outline above; **the landing marker is the signal at 20 m, not the line** — ring (green) / X (red) / flat **disc** (white) at constant ≥ 24 px screen size. Red dashes are screen-space length so they stay dashed at distance; dashes are static (reduced-motion removes any marching).

**Build-mode ghosts.** Valid = Go Green 40 % fill, solid outline; invalid = Stamp Red 40 %, dashed outline with an X, reason text in Label weight on a pill; unaffordable = Chalk White 25 %, "missing" chips in red. Never magenta, never district colour.

**Payday motifs.** The earnings counter is a coin-fed stamp machine: each tally line stamps down (Payday Gold frame, Charcoal panel). Draft cards are oversized stamps dealt face-down and flipped; the picked card gets a cancellation mark.

**Art direction vs readability — conflicts raised by ux-designer and how they were decided (2026-10-15):** (1) red/magenta text contrast → fills only, grey panel outline; (2) cell radius and focus state → r = 6, thickness-based ring; (3) white arc over white walls → arc outline + disc marker; (4) burst repetition → single non-strobing alert, ≤ 1 Hz raid pulse. None reduces delivery or combat readability once applied.

## 8. Asset Standards

> The only binding budget is the project's: 16.6 ms client frame at 1080p on a mid-range GPU during a shift-5 raid (spec 07 §8). Every number below is a **proposed ceiling** derived from it; nothing in spec 07 or `docs/engine-reference/godot/` sets poly, draw-call or texture figures. Items marked VERIFY-IN-ENGINE or NOT SOURCEABLE must be confirmed on the pinned engine before they are relied on.

### Art-director standards

**Formats.** Meshes: glTF 2.0 as `.glb` (binary, textures embedded only for characters; environment references the shared atlases). Reason: Godot's first-class importer, no Blender install required on every machine, one file per asset, stable material slot order. `.blend` stays a source format; `.fbx` is not accepted. Textures: PNG, lossless. Our atlases are 256² flat colour; KTX2 buys nothing until the technical artist proves a VRAM need. Audio: out of scope.

**Naming.** C# scripts and scenes keep PascalCase. Asset files are lowercase snake: `[category]_[name]_[variant]_[lod].[ext]`. Categories: `char_`, `hat_`, `goon_`, `env_`, `prop_`, `con_` (construct), `veh_`, `ui_`, `vfx_`, `tex_`, `decal_`. House kit: `env_house_[sq|long|dbl]_[gable|hip]_[lo|hi]` — no district suffix; district is the roof material. District-owned trees/props: `_d1`…`_d5`. Hats: `hat_s01`…`hat_s08` plus family name (`hat_s03_bucket_fisherman`). Goons: `goon_runner`, `_mega` suffix. LOD: `_lod0/_lod1/_lod2`. Street-stripe patterns: `_solid/_stripe/_dot/_chev`.

**Resolution tiers.** Palette atlases: 256² × 3 (Tier A/B/C), never larger, **district colours as rows inside the Tier A atlas** (see conflicts). Address atlas: generated per map, **2048×1024 single-channel SDF**, text sized so a house number fills ≥ 64 px at 3 m. UI: authored at 1080p, 2048² atlas, 9-slice panels. Emissive state strips: 128×32. Decals (plasters, flyers, logo): 256–512². Expression atlas: 512² (4×4 cells of 128). Lobby set: 512² max.

**LOD expectations.** Character: §5. House: LOD0 door glyph, windows, chimney; LOD1 glyph + roof + wall; LOD2 box + roof prism, roof colour only. Prop: LOD0 full; LOD1 two-tone; LOD2 silhouette blob or culled. Construct: LOD0 panels, skirt, rollers; LOD1 skirt + strip; LOD2 box + Postal Blue stripe (also the ruin mesh, §6). Vehicle: LOD0 driver window, headlights; LOD1 wheels + body; LOD2 box. Goon: as character, sash kept to LOD2.

**Export.** 1 unit = 1 m, 2 m build grid, Y-up, transforms applied, scale 1.0. Pivots: ground-centre for houses/props/constructs/vehicles; feet for humans; head-socket origin for hats; belts at tile centre so 60 m+ scroll textures tile cleanly. Flat shading everywhere; smooth normals only on cliffs/beaches/water. Material slots per the technical-artist table below: one per saturation tier the object touches, so a pure Tier C mesh carries one and a house three.

**Source policy.** Repo: `.glb`, `.png`, `.tres`. Source (`.blend`, `.kra`, `.afdesign`) in `source/` under Git LFS; every `.glb` must be re-exportable from it.

**Acceptance checklist (every asset).** 1) 30 px greyscale silhouette reads and names itself. 2) Correct saturation tier; Tier C in L* 55–70 (house walls L* 80–95, ≤ 20 % sat, §4). 3) No feature < 25 cm. 4) Outline on characters/carried items only. 5) Three LODs present, naming correct. 6) Pivot and scale correct on the 2 m grid. 7) Constructs: skirt + ≥ 15 % Postal Blue; no magenta on ground/buildings. 8) Hats and headgear: the thirteen-silhouette greyscale test (§3) passes.

### Technical-artist hard constraints

**Poly budgets (triangles, authored LODs).**

| Asset | LOD0 | LOD1 | LOD2 |
|---|---|---|---|
| Human (shared rig; goon = rig + prop ≤ 300) | 1,500 | 600 | 200 |
| House (12-kit) | 400 | 150 | 40 |
| Mailbox | 150 | 60 | 20 |
| Prop | 120 | 40 | 12 |
| Belt tile / other construct | 200 / 300–800 | ×0.4 | ×0.1 |
| Mail item (MultiMesh, no per-instance LOD) | 24–40 | — | — |
| Vehicle | 800 | 300 | 80 |
| PO landmark | 3,000 | 1,200 | 300 |

Raid sanity sum: 48 humans ≈ 28k (+28k hull), ~200 near belts 40k, ~1,500 visible items 48k (+48k hull), 150 houses 18k (150 is conservative; v1 Small Island has ~50, spec 13 §2.1), props 12k, constructs 50k, vehicles 6k, PO 3k ≈ **0.3 M tris** — far below a 1 M ceiling. **Draw calls and overdraw are the real limits, not vertices**; proposed cap ~2,000 draws/frame.

**Textures.** Three 256² palette atlases ≈ 0.8 MB total: the flat-atlas rule removes per-asset textures entirely. Exceptions: address atlas — 300 cells at 128×32 px in 2048×1024 single-channel SDF (2 MB), legible ≤ 3 m; face atlas 512² (1 MB); UI sheet 2048² (~16 MB); decals/plasters 512²; busy-belt scroll and foam 256². Proposed cap 64 MB VRAM, expected ~24 MB.

**Material slots.** Environment, per object: pure Tier C objects (grass, roads, cliffs, trees, water) 1 (Tier C atlas); houses 3 (Tier A atlas for roof, door and kerb glyph colours; Tier C atlas for walls; unlit address-glyph/number material); mailboxes 2 (Tier A atlas + unlit glyph). Constructs 2: Tier B atlas + unlit emissive panel. Characters 2: body/face atlas (face is a UV offset, not a slot) + the outline hull as a `next_pass`-style second pass (long-standing 4.x API; its 4.7 status is NOT SOURCEABLE from the engine reference). Mail items 2 (Kraft + address atlas) or 1 if one shader samples both.

**Inverted-hull outline (§5 rule; feasibility check recorded here).** Cost = every outlined mesh drawn twice (front-face cull, normal-extruded, screen-constant 2 px). 48 humans ≈ 100–150 extra draws and ~28k tris — cheap. Hazard: flat-shaded split normals open gaps at corners; **export a smoothed-normal set in a spare attribute (COLOR/UV2) for the hull.** Belt items: a second MultiMeshInstance sharing the buffer with the hull shader is one extra draw per segment; whether a per-material second pass applies to MultiMesh instances is **VERIFY-IN-ENGINE**. Fallback order: hull on humans + items ≤ 20 m → depth/normal post-process edge pass on a character layer (~0.3–0.5 ms, VERIFY-IN-ENGINE) → no outline on carried items.

**glTF import.** `.glb`, 1 unit = 1 m, transforms applied, Y-up (glTF and Godot agree). Flat shading via split normals plus the smoothed set above. 4.5+ uses fixed node naming for new imports (sourced: `docs/engine-reference/godot/breaking-changes.md`, 4.4 → 4.5). Authored-LOD import naming and the state of `-col`/`-convcol`/`-colonly` collision suffixes in 4.4+: **NOT SOURCEABLE** — plan a C# import post-process keyed on our `_lod0/1/2` suffix.

**Batching.** Forward+ sorts by material but does not merge separate instances (training-era knowledge; **NOT SOURCEABLE for 4.7**). One material per tier therefore buys merging *within a tier*, not across objects: pure Tier C environment merges per 32 m chunk (one draw each); houses batch by their three shared materials (three draws per chunk — Tier A, Tier C, glyph — not one per house), and the 12-house kit and props go as per-chunk MultiMeshes under those same materials. Trade-off: merged chunks weaken occlusion culling.

### Art preference vs technical constraint — decided (owner, 2026-10-15: constrained standard)

| Conflict | Ideal (art) | Constrained (tech) | Decision |
|---|---|---|---|
| Per-district palette atlases | 15 small atlases, one per district × tier | Breaks chunk merging across district borders | **District colours as rows inside the single Tier A atlas** |
| Address atlas resolution | 4K | 16–64 MB for no legibility gain at ≤ 3 m | **2048×1024 single-channel SDF** |
| Per-asset materials | Unique materials where the artist wants them | Fatal for environment batching | **Heroes only** (PO landmark, vehicles); environment stays one material per tier |

## 9. Reference Direction

Each reference is a single borrowed technique tied to a locked section; none is a mood board. In order of precedence.

**1. Satisfactory** (owner reference). *Take:* machine readability — orthogonal bodies, one dark frame colour, state carried on a small emissive strip rather than the whole mesh, and belts where the item visibly rides on top. Serves §3 machines, §4 state panels, §6 construct rules. *Avoid:* PBR metal, grime, decals, the brown-grey industrial palette and real-world scale. Our machines are Tier B flat colour at toy proportions; a Satisfactory screenshot pasted into our town must look wrong. *Adds:* the logistics grammar none of the others have.

**2. Muck / Crab Game** (owner reference). *Take:* the one-rig crowd — 2.5-head low-poly humans, one flat material, cheap enough for 48 on screen, with identity carried by one prop. Serves §5 shared rig and §3 hat-first slots. *Avoid:* their untextured-grey, asset-flip inconsistency and lighting-dependent colour. We add the 2 px outline, the saturation tiers and the thirteen-silhouette greyscale test; the cheapness stays, the sloppiness does not. *Adds:* the friendslop body and the budget that makes eight players possible.

**3. PlateUp** (vision doc). *Take:* carried items held high and oversized so the over-shoulder camera always sees the parcel past the hat, and failure shown as a physical object in the world (the burnt plate) rather than a number. Serves §5 carry poses and principle 2. *Avoid:* its top-down diorama framing and its single-room scale; our failure gags happen on houses and mailboxes at 20 m. *Adds:* the "outcome is a prop" rule in motion.

**4. Hergé's ligne claire** (non-game; carries a committed technique). *Take:* uniform-weight contour with unshaded flat fills and zero hatching — the exact spec for §5's 2 px Charcoal outline and §6's no-texture rule. *Avoid:* outlining everything. Ligne claire outlines the world; we outline characters and carried items only, so the figure is the hardest edge in frame (§6 legibility test). *Adds:* the line discipline that keeps outlines from becoming decoration.

**5. Mid-century postal ephemera** — parcel stencils, cancellation marks, perforated stamps (non-game; carries a committed technique). *Take:* stencil-cut display type, the cancellation mark as a "chosen" badge, perforation as a border only. Serves §7 typography and Payday motifs. *Avoid:* sepia, paper grain, distressing — all sub-25 cm noise and off-palette. *Adds:* the typographic voice; nothing else above has text.

**Tiebreak.** If two references disagree, Satisfactory wins on anything a player must read to act (state, address lane, construct vs environment); Muck/Crab Game wins on anything about the human body and its cost. Reason: Pillar 1 says the mail must flow, so legibility outranks charm — but the charm lives in the carrier, and the carrier must stay cheap enough for eight.
