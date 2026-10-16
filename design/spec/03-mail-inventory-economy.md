# 03 — Mail, Destinations, Inventory, Economy

The core objective is to deliver mail and packages to Destinations. This chapter defines the mail item model, the destination types and their acceptance rules, the grid inventory system and its interaction rules, and the money economy.

## 1. Mail

### 1.1 Mail kinds

Sizes and stack limits are from the design doc. Postcards and Cursed mail are additions.

| Kind | Grid size | Max stack | Base value (¢, tunable) | Weight class | Notes |
|---|---|---|---|---|---|
| Postcard | 1x1 | 40 | 4 | Light | Bulk filler; introduced shift 2 |
| Letter | 1x1 | 20 | 8 | Light | Baseline item |
| Small Package | 1x2 | 1 | 30 | Light | Fits in a backpack column |
| Medium Package | 2x2 | 1 | 70 | Medium | 4 fit in base inventory |
| Large Package | 2x4 | 1 | 160 | Heavy | Player carries at 92% speed (weight formula, §3.2; chapter 13 resolves the earlier 85% figure) |
| Cargo | 5x8 | 1 | 600 | Bulk | Cannot be carried by hand; vehicle, belt, or boat only. Business Dock destination |
| Cursed Mail (modifier) | inherits | inherits | ×1.5 | inherits | Postage Stamp only; misdelivery spawns a mini-raid |

value on a spawned item is baseValue × distanceMultiplier × shiftMultiplier, where distanceMultiplier = 1 + 0.25 × (district index of destination − 1) and shiftMultiplier = 1 + 0.1 × (shift − 1). Farther, later mail is worth more; this rewards reaching new districts.

### 1.2 Mail item model

```
MailItem {
  id            : uint32          // unique per run, allocated by server
  kind          : MailKind
  addressId     : AddressId       // districtId:streetId:number[:unit]
  value         : uint16          // ¢ at spawn, already multiplied
  spawnShift    : uint8
  deadlineShift : uint8           // spawnShift by default; +1 for Cargo
  flags         : bitset          // Cursed, Fragile (deferred), Priority (deferred)
}
```

Letters and postcards stack only if they share addressId. This is the key sorting mechanic: an unsorted pile of 20 letters occupies 20 cells; sorted by address they compress. The stack is a list of MailItem.ids under one grid entry.

### 1.3 Spawn schedule and mix

Per shift the PO generates mail totalling spawnValue (chapter 01 §3.2) in batches every 15 s. Mix by value share (tunable):

| Shift | Postcard | Letter | Small | Medium | Large | Cargo |
|---|---|---|---|---|---|---|
| 1 | 0% | 60% | 30% | 10% | 0% | 0% |
| 2 | 10% | 40% | 30% | 15% | 5% | 0% |
| 3 | 10% | 30% | 25% | 20% | 10% | 5% |
| 4 | 10% | 25% | 20% | 20% | 15% | 10% |
| 5 | 10% | 20% | 20% | 20% | 15% | 15% |

Destination selection weights every unlocked address equally, then applies a "streak" rule: 30% of items in a batch share a street with another item in that batch, so sorting by street pays off. Cargo only targets Business Docks; if none is unlocked, Cargo share is redistributed to Large.

### 1.4 Labels

Every mail item shows its address on the model (readable at ≤ 3 m) and in the inventory tooltip. The label also shows a district colour swatch and a street colour stripe so players can sort by colour before they can read text at distance. Address text uses the same font as street signs.

## 2. Destinations

### 2.1 Types

| Type | Accepts | Capacity | Interaction | Payment |
|---|---|---|---|---|
| House mailbox | Postcard, Letter, Small, Medium, Large addressed to this house | Unlimited (mail vanishes on insert) | Hold interact for 0.4 s with mail selected, or belt/inserter output facing the mailbox | Instant on insert |
| Apartment Mail Room | All non-Cargo kinds addressed to any unit of this complex | Unlimited | Same as mailbox; the mail room has one interaction point. Unit number must match the item's unit. | Instant |
| PO Box Bank | Postcard, Letter addressed to a PO Box number | Unlimited | One interaction point per bank; box number must match | Instant |
| Business Dock | Cargo addressed to this dock | Unlimited | Boat or vehicle unloading zone, belt end, or crane (deferred) | Instant |

Every house has exactly one mailbox (doc). Mailboxes are indestructible; enemies target constructs and players, not residents.

### 2.2 Acceptance rules

1. Item kind must be allowed by the destination type.
2. item.addressId must equal the destination's address (including unit/box number).
3. If both hold, the item is consumed and the team is paid value × timeliness, where timeliness = 1.0 if currentShift <= deadlineShift, 0.5 if one shift late, 0 if later. A Delivered event is emitted.
4. If rule 1 fails, the insert is rejected (nothing happens, UI shows "Won't fit here").
5. If rule 1 passes but rule 2 fails: the item is consumed, misdeliveryPenaltyRatio × value (0.5) is deducted from the team wallet (wallet can go negative to −500 ¢, below which misdeliveries are rejected), the complaint meter increases, and a Misdelivered event is emitted. Automation output into the wrong mailbox is treated identically, which is why sorters matter.

### 2.3 Automated delivery to destinations

A belt end, inserter, pipe outlet, or NPC vehicle "drop" that faces a mailbox pushes items one at a time at the destination's insertRate (baseline 1 item / 0.5 s). Stacks are inserted item by item. A mailbox can be fed by at most one automated feeder at a time (the first placed wins; the second is rejected at build time).

## 3. Grid inventory

### 3.1 Containers

From the design doc, plus internal containers:

| Container | Grid (rows x cols) | Cells | Owner | Notes |
|---|---|---|---|---|
| Hotbar | 1x8 | 8 | Player | Always visible; also holds tools |
| Base inventory | 2x8 | 16 | Player | Fixed |
| Backpack | 2x8 | 16 | Player (equipped) | Adds a third and fourth row to the player's panel |
| Bike | 2x8 | 16 | Vehicle | Rider can access while mounted |
| Mail truck | 10x8 | 80 | Vehicle | Access from rear door or driver seat |
| Mail semi | 40x100 | 4000 | Vehicle | Deferred (Free Play) |
| Small boat | 2x8 | 16 | Vehicle | Rowboat |
| Medium boat | 10x8 | 80 | Vehicle | Motor boat |
| Large boat | 40x100 | 4000 | Vehicle | Deferred |
| Train car | 20x40 | 800 | Vehicle | Deferred |
| PO Intake | 16x20 | 320 | World | Mail spawns here |
| PO Depot | 16x20 | 320 | World | Team storage |
| Chest | 4x8 | 32 | Construct | Cheap storage |
| Depot | 10x16 | 160 | Construct | Belt-accessible storage |
| Sorter buffer | 2x4 | 8 | Construct | Internal |
| Death bag | 4x8 (+ overflow) | — | World | Dropped on death |

Grids larger than ~10x16 are not rendered as full grids. Containers with more than 160 cells use a "manifest" view: a scrollable list grouped by kind and address with counts, plus a mini-grid showing fill percentage. Drag and drop still works against the manifest (drop onto a group, or onto "any free space").

### 3.2 Placement rules

- Items occupy a rectangle of w x h cells; rotation by 90° is allowed for all items (a 1x2 becomes 2x1). Rotation is a property of the placement, not the item.
- A placement is valid if every covered cell is inside the grid and unoccupied (or occupied by a compatible stack, see below).
- Stacking: dropping stackable mail onto a stack with the same kind and addressId merges up to maxStack; excess remains on the cursor. Non-mail stackables (Logs, Stone, etc.) merge by item type.
- Auto-place: shift-click or quick-move finds the first valid placement scanning row-major, trying stack merges first, then unrotated, then rotated.
- Sort button: any container the player has open has "Sort by address" and "Sort by size". Sorting is a server operation that repacks the container using a first-fit-decreasing bin packer grouped by the chosen key. This is the manual equivalent of an Address Sorter and is intentionally allowed to be strong: players sorting a truck by hand is a valid strategy.
- Weight: total weightClass points in a player's containers set a speed multiplier: Light 1, Medium 3, Heavy 8 points. Speed multiplier = clamp(1 − 0.01 × points, 0.6, 1.0). Vehicles ignore weight.

### 3.3 Interaction rules

- A player may have open at most: their own panel (hotbar + inventory + backpack) and one external container (chest, vehicle, depot, another player's death bag). Opening another closes the first.
- External containers can be opened by multiple players simultaneously. All operations are server-validated and applied in arrival order; a conflicting move (cell now occupied) is rejected and the client's optimistic preview is rolled back.
- Cursor item: picking an item up puts it on the player's cursor, which is a server-side one-slot container. Disconnecting with a cursor item returns it to base inventory or drops it.
- Dropping in the world spawns a WorldItem (physics-less, sits on the ground, 5 min despawn, mail despawn returns the mail to PO Intake to avoid losing quota value).
- Hotbar slot 1 is reserved for the "hands" (interact) and cannot hold items; slots 2–8 hold tools or mail. Selecting a slot with mail enters "delivery stance": interacting with a mailbox delivers the selected stack.
- Throwing: see chapter 13 §4.1. Any held stack can be thrown; thrown mail entering a mailbox volume follows the acceptance rules in §2.2 (including misdelivery).

### 3.4 Range and speed of manual operations

| Operation | Time |
|---|---|
| Mailbox insert (one stack) | 0.4 s hold |
| Pick up world item | Instant on interact |
| Open container | Instant, within 2.5 m |
| Transfer item between containers | Instant (UI), server round-trip |
| Harvest hit | 0.6 s swing |

## 4. Economy

### 4.1 Wallet

One team wallet (integer ¢). Delivery income adds, misdelivery subtracts, purchases subtract, refunds add. Balance may go negative only via misdelivery, floored at −500 ¢. Shop purchases require sufficient balance.

### 4.2 Income sources

| Source | Amount |
|---|---|
| Correct delivery | value × timeliness |
| Salvage (deconstruct a construct) | 100% of material cost returned as items (not money) in Prep, 50% during Delivery |
| Shift bonus | +10% of shift earnings if zero misdeliveries that shift |
| Selling raw materials at the PO Shop | 25% of the material's shop buy price (money sink relief for over-harvesters) |

### 4.3 Sinks

| Sink | Notes |
|---|---|
| PO Shop items | Tools, vehicles, blueprints, refined materials (chapter 10). No crafting station — the shop is the source for plank, rope, iron_ingot, glass, and tools. |
| Building recipes | Placement costs only: recipes consume materials from inventory/depot when a construct is placed, not money. Some recipes require a blueprint bought once from the shop. There is no forge or handcraft item production. |
| Repairs | Repair hammer consumes materials at 25% of build cost per full repair |
| Misdelivery | 50% of value |
| Postal Inspector | −10% earnings for one shift (event) |

### 4.4 Starting kits

Selected per player in the lobby. Contents are placed in the player's inventory at spawn; team items go to the PO Depot.

| Kit | Personal items | Team items (once per run, from host's kit choice) |
|---|---|---|
| Land (default) | Backpack, Axe, 4 Fiber | Bike, 10 Logs |
| Sea (Rank 2) | Backpack, Axe, Rope ×2 | Rowboat blueprint, 10 Logs |
| Pipes (Rank 10) | Backpack, Pickaxe | 8 Pipe, 1 Pump blueprint, 10 Iron Ingot |

Kits are unchanged from the design baseline. Sea still grants Rope (shop material, not a craft product). The Sea kit's rowboat blueprint unlocks build placement (or the rowboat can be bought from the shop); there is no craft recipe for the boat. (v1 ships the Land kit only — chapter 13.)

### 4.5 Reference economy check

Chapter 11 verifies that a solo player delivering by hand at a realistic pace (about one delivery per 12 s on shift 1 in a compact district) earns ~900 ¢ over a 240 s Delivery, comfortably above the 600 ¢ shift-1 quota, and that from shift 2 onward hand delivery alone falls short of quota so a bike, then belts or a truck, become necessary.
