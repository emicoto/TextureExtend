# Probably Extend Framework — API Reference

Target: mod developers and AI coding assistants. Covers framework 1.0.0. Game: Probably Stolen 0.46 (Unity 2021.3 Mono, netstandard2.1).

- Mod ID `Lune.ProbablyExtendFramework` · assembly `ProbablyExtendFramework.dll` · namespace `ProbablyExtendFramework` (tags: `ProbablyExtendFramework.TagSystem`)
- Capabilities: none. Optional companion `Lune.ProbablyExtendFramework.Files` (capability `files`): reads images of disabled packs / DLLs, local image folder, cache folder.
- Reference the DLL with `<Private>false</Private>`; add `<Prerequisites><Mod>Lune.ProbablyExtendFramework</Mod></Prerequisites>` to `manifest.xml`.
- Zero-code integration is possible (files + save slot, see §3, §6). Code is only needed for live changes, custom conditions, or generated content.

---

## 1. Model

| Term | Meaning |
|---|---|
| **target** | `PaintTarget.Portrait` (portrait ID, e.g. `femaleLower3`, `clara`) or `PaintTarget.StaticItem` (item ID). |
| **source** | Who provides an image: `"game"`, a mod ID, a pack ID, `"local"`. |
| **image key** | `source:id` or `source:id~n` (n-th image with the same ID from the same source). Bare `id` = let the framework pick the source. `ImageKey.Make/Parse/IdOf/SourceOf/Full`. |
| **variant** | Named alternate image of a target (`angry`, `ep1_accept`). Empty = base image. Normalized to lowercase. Reserved chars: `/ @ # \| : ~`. |
| **variant tags** | Own tags of a variant = name split on `_` (`ep1_accept` → `ep1`,`accept`) + registered tags. Inherits all base tags. Root-key member variants are not split. |
| **wanted tags** | Tags a mod asks for on a target (save slot `want.*`). The framework picks the variant. |
| **image group** | Items: one group per item ID; base = the item's own sprite; each source adds variants; same-name variants from different sources coexist. |
| **pool** | Visitor pool (`femaleLower`, … or `custom`). New portraits can join; the game's own draw is unchanged otherwise. |

**Variant selection** (`VariantTags.Best`): candidates = variants whose own tags are all ⊆ wanted and none banned; pick the one with most tags; tie → shorter name. Effect: wanted `{ep1,accept}` → `ep1_accept` → else `ep1` → else no change.

**Precedence** (high → low):
- Item: player lock (manual pick) > pulse (temporary) > wanted tags > rules (`weight` desc) > time tag (`openshop`/`endshop`/`wakeup`) > base.
- Portrait on counter / in game table: wanted tags > rules > counter event tags (`visit`, `sold[_bonus|_malus]`, `bought[_bonus|_malus]`, `leave`) > source choice.
- Source choice: player per-key choice > item's own mod > active sources by `priority` (`top` wins over non-top) > game > inactive candidates.

**Check points** (no polling; all hooked on the game's `…Late` events, so values written in `…Early` are seen):

| Target | Re-evaluated at |
|---|---|
| Items (managed list) | load (whole store walk), shop open, shop close, sleep settlement (whole store walk; counts as wake-up), `Notify()`, `Refresh(item)` |
| Counter visitor | arrive (`visit`), sell / buy (`sold*`/`bought*`), dismiss (`leave`), `Notify()`, `Want*()` |

Whole-store walk (incl. nested containers) runs only at load and sleep; other points iterate the managed list only.

---

## 2. Lifecycle

```csharp
public void OnEnable()  { Painter.OnReady += Register; }        // fires immediately if already ready
public void OnDisable() { Painter.OnReady -= Register; Painter.UnregisterAll(MyId); }
static void Register()  { Painter.RegisterPack(MyId, json); /* or LoadCsv / code calls */ }
```

Files (`TextureExtend.csv`, `TextureExtend.json`) in the mod root are read automatically on `ModHook.OnModAssetsLoaded`; no code needed. Everything registered under an owner ID is removed by `UnregisterAll(owner)`.

---

## 3. Registration files

Both files may coexist. Enabled mods: read at asset load. Disabled (inactive) packs: read when shelved; they act as candidates until activated by the player.

Image references (`sprite` column / `file` field):

| Form | Meaning |
|---|---|
| `key` | Already-loaded texture of the owning mod. Items: must be in `Textures/Items/` (the game only loads that folder as item sprites). |
| `ModID:path/to.png` | File inside a mod folder, read on demand (works for disabled packs). |
| `res:Resource.Name.png` | Embedded resource in the owning mod's DLL (enabled mods only). |

### 3.1 `TextureExtend.json` (full pack registration)

```json
{
  "pack": "my.pack", "name": "My Pack", "priority": 10, "root": "Portraits/",
  "style": "hd", "scale": 1.0, "dialog": "auto",
  "tagGroups": [ { "id": "outfit", "under": "", "scope": "portrait" } ],
  "tags":      [ { "id": "swimsuit", "group": "outfit", "scope": "portrait" } ],
  "portraits": [ {
      "id": "femaleLower3", "file": "fl3.png", "scale": 1.1, "tags": ["swimsuit"],
      "pool": "femaleLower", "dialog": "high", "frames": { "bust": "fl3_bust.png" },
      "variants": [ { "name": "angry", "file": "fl3_angry.png", "tags": ["mood"], "frames": { "bust": "fl3_angry_bust.png" } } ] } ],
  "items": [ { "id": "wondrous_milk", "tags": ["drink"], "manual": true,
      "variants": [ { "name": "rich", "file": "milk_rich.png", "tags": [] } ] } ]
}
```

| Field | Notes |
|---|---|
| `pack` | Pack ID; default = mod ID. |
| `priority` | int, or `"top"` / `"top N"` (TopPriority: yields only to the player's choice). |
| `root` | Prefix for every `file`. |
| `style` | `hd` (default) or `pixel` (point filtering, vanilla render path). |
| `scale` | Pack display scale 0.25–4; omitted = auto-fit. Per-portrait `scale` overrides it. |
| `dialog` | `auto` `vanilla` `high` `low` `mid`. |
| `portraits[].id` | Vanilla key = replace; new ID = custom NPC. |
| `portraits[].pool` | Visitor pool to join; omitted for a new ID = `custom`. |
| `frames` | `waist` / `bust` dedicated crops; else cropped from the full image (62 % / 42 %). |
| `items[].manual` | `false` = player cannot pick the look by hand. |
| `file` / `sprite` | Either; a bare string in `frames` = `file`. |
| legacy | `{ "name", "priority", "images": [ { "id", "path", "variant" } ] }` still accepted. |

Implementation note: JSON is converted to the CSV rows below and loaded by the same code path.

### 3.2 `TextureExtend.csv`

Header row required (column order free; `#` = comment; a new `kind,…` header may appear mid-file). Bad rows are skipped and logged with line numbers.

| kind | Required | Optional / notes |
|---|---|---|
| `portrait` | `id` + (`sprite` or `tags`) | `tags` (space-separated), `scale`, `dialog`, `frame` (`waist`/`bust` → row is a crop), `manual=no` |
| `state` | `target`, `id`, `tag`, `sprite` | variant; `tags` = the variant's own tags; `frame` |
| `series` | `target`, `id`, `prefix` | registers `prefix` (base) + `prefix_x` (variant `x`); loaded textures only |
| `item` | `id`, `sprite`, `suffixes` | variants `sprite_suffix` for each `a\|b\|c` |
| `rule` | `target`, `id`, `tag`, `when` | `weight`; items: `sprite` = image for this state |
| `pulse` | `target`, `id`, `tag`, `on` | `until` (check point, default `sleep`) |
| `pack` | `id` | `name`, `priority`, `scale`, `style`, `dialog` |
| `pool` | `id`, `group` | `group` = pool name |
| `join` | `id`, `group` | `group` = root key; `when` |
| `auto` | `id` | `when` (visitor live replacement) |
| `tag` | `target`, `group` | `id` = target (or `*`, or `id#variant` = variant tag); empty `id` = define tag/group; `under`, `with`, `without`, `manual=no` |
| `faction` | `id`, `word` | map a word to a faction tag |
| `dynamic` | `id` | item whose image is decided at creation (picker shows the whole library) |

`target`: `item` | `portrait`. `id` may be `@group` in `rule` / `tag` rows.

### 3.3 Convention-based (no registration)

- Portrait files named like vanilla keys (`femaleLower3.png`, `femaleLower3_angry.png`) are recognized by field structure.
- Item variants `itemId_variant.png` in `Textures/Items/` are auto-collected **for local (non-Workshop) mods only**; ignored when the key is another item's base sprite or the variant starts with `inspect`.
- Local images (companion installed): `<persistentDataPath>/ModData/Lune_ProbablyExtendFramework/Local/`, rescanned on every load. Same naming rules; `itemId_variant.png` for items; the shortest same-prefix file in a folder is the base; folder names become tags; a folder named after a pool puts new portraits in that pool.

---

## 4. Conditions (`when` column, `Painter.Expression`)

Atoms: `tag:NAME` · `tag:NAME>=60` (`= != > >= < <=`; non-numeric RHS compares strings) · `day>=N` · `night` (shop closed → sleep settlement) · `visitor` · `visitor:lower` (`lower upper sec bm rev cartel church tourist` or `FACTION_…`) · `rep:upper>=20` · `flag:NAME` · `flag:NAME>=3` · `cond:NAME` (registered via `Painter.Condition`) · `true` `false`.
Operators: `!` `&` (`&&`) `|` (`||`) `( )`; `&` binds tighter. `tag:` reads the evaluated item's tags; with no item (counter portrait) item-tag atoms are false.

---

## 5. Code API

All members static unless noted. "Notify" = applies immediately to what is on screen.

### 5.1 Registration

| Member | Purpose |
|---|---|
| `Painter.OnReady` (event) | Framework ready; invoked immediately if subscribed late. |
| `int Painter.RegisterPack(string modId, string json)` | Same as a `TextureExtend.json`; `file` relative to `modId` folder. Returns rows registered. |
| `int Painter.LoadCsv(string modId, string relPath)` / `LoadCsvText(modId, csv)` | CSV registration. |
| `int Painter.Series(modId, PaintTarget, id, prefix)` | = `series` row. |
| `void Painter.AddItemVariant(modId, itemId, tag, spriteKey)` | Item variant from the mod's loaded sprite. |
| `void Painter.UnregisterAll(string modId)` | Remove everything owned by `modId`. |
| `PortraitLibrary.Register(sourceId, id, Func<Sprite>, params string[] tags)` | Base portrait from code. |
| `PortraitLibrary.RegisterVariant(sourceId, id, variant, Func<Sprite>)` | Variant (needs a base registered; otherwise logged and dropped). |
| `PortraitLibrary.RegisterFrame(sourceId, id, Display.Usage, Func<Sprite>, variant=null)` | Dedicated waist / bust crop. |
| `PortraitLibrary.RegisterImageFile(sourceId, id, modId, relPath, variant, stamp, params tags)` | File in an enabled mod folder; `stamp` changes → rebake. |
| `PortraitLibrary.AddTags(sourceId, id, params tags)` | Add base tags. |
| `PortraitPools.Assign(owner, portraitId, pool)` | Pool membership (`PortraitPools.Custom` = `"custom"`). |
| `Packs.Declare(owner, id, name, priority, dialog=null, top=false)` / `Packs.Style(owner, id, scale, style)` | Pack declaration. |
| `DialogPlacement.SetFor(owner, portraitId, placement)` | Per-portrait dialogue box position. |
| `Painter.Join(modId, portraitId, rootKey, Func<GameItem,bool> when=null)` | Root-key group: `portraitId` becomes a variant of `rootKey`; `when` → auto-switch on items. |
| `Painter.AutoReplace(modId, portraitId, when=null)` | Visitor live replacement for that key. |
| `Painter.DynamicTexture(modId, itemId \| Func<GameItem,bool>)` | Picker shows the whole library for such items. |
| `Painter.AddPortraitResolver(modId, Func<GameItem,string>, bool takeOver=false)` | Tell the framework which portrait a foreign item shows; `takeOver` = always render HD. |

### 5.2 Rules, conditions, pulses, tags

| Member | Purpose |
|---|---|
| `PaintRule Painter.For(modId, PaintTarget, id)` → `.Tag(s).Cond(f).Weight(n).Only(filter).Sprite(mod,key).NoManual()` | Conditional state. `id`: ID, `*`, `@group`. Incomplete rules (no tag / cond) are ignored. |
| `Painter.RemoveRules(modId)` | Remove rules, resolvers, dynamic marks, item variants of `modId`. |
| `Painter.Condition(modId, name, Func<GameItem,bool>)` | Named condition for `cond:name`. Prefix names with your mod. |
| `Func<GameItem,bool> Painter.Expression(string)` | Compile a condition string; throws `FormatException` with position. |
| `PulseDef Painter.Pulse(modId, PaintTarget, id)` → `.On(action).Tag(state).Until(checkpoint)` | Temporary state; highest rule weight. |
| `int Painter.Fire(string action, GameItem item)` | Start matching pulses on that item; returns count. |
| `Checkpoint.Register(name)` / `Checkpoint.Fire(name)` / `Checkpoint.OnFired` | Custom check points; built-ins `load wake open close sleep visit`. Re-entrant fires are queued and deduplicated. |
| `TagDef Painter.Tag(modId, PaintTarget, id)` → `.Under(parent).Group(name).With(tags).Without(tags)` | Tag layer. |
| `List<string> Painter.Pool(PaintTarget, params tags)` | IDs carrying all tags (`@group` accepted). |
| `TagSystem.PortraitTags` / `ItemTags` / `Regist` | Built-in tag libraries and registration (see constants). |

### 5.3 State: wanted tags, flags, live updates

| Member | Purpose |
|---|---|
| `Painter.Want(PaintTarget, id, params string[] tags)` | Persistent (per save). Empty tags = clear. Notifies. |
| `Painter.WantToday(...)` / `Painter.WantNow(...)` | Expire at sleep / at next visitor change. |
| `ICollection<string> Painter.Wanted(PaintTarget, id)` | Current wanted tags (excl. framework time / event tags). |
| `Painter.Ban(string tag, bool on=true)` | Exclude variants carrying `tag` everywhere (per save). |
| `Flags.Set(name, string\|int\|bool)` / `Get` / `IsSet` | Per-save flags for `flag:` conditions; `null` clears. Notifies. |
| `Painter.Notify()` | "I changed the save slot / item tags": re-evaluate managed items + counter now. |
| `Painter.Refresh(GameItem)` | Re-evaluate one item (also adds it to the managed list). |
| `Painter.RefreshManaged()` / `RefreshAll()` | Managed list / whole store (expensive). |
| `Painter.OnStateChanged` (event `GameItem, from, to`) | After an item's state changed; batched; do not revert inside. |
| `Display.OnCounterShown` (event `portraitId, state`) | Counter image changed. |

### 5.4 Query and preview (no save changes)

| Member | Returns |
|---|---|
| `string Painter.Decide(PaintTarget, id)` | Variant that would be used now (`""` = base). Items: generic (item-tag atoms false). |
| `string Painter.StateOf(GameItem)` / `VariantOf` | Variant shown on that item. |
| `string Painter.PortraitOf(GameItem)` | Portrait ID painted on / resolved for the item, or null. |
| `string Painter.ActiveTag(PaintTarget, id, item=null)` | First holding rule's tag. |
| `IList<string> Painter.VariantTagsOf(PaintTarget, id, variant)` | Variant's own tags. |
| `IList<string> Painter.ItemVariants(itemId)` | Usable variants in the item's image group. |
| `Painter.TimeTag` · `Painter.CounterKey` · `Painter.CounterEvents` · `Painter.ManagedItems` | Current time tag; portrait key on the counter; its event tags; managed item count. |
| `Painter.PreviewTime(string tag)` | Pretend time tag (`null` = from store state). |
| `Painter.PreviewCounter(string portraitId, params string[] tags)` | Pretend counter events (no tags = clear). Resets on real visitor change. |
| `Portrait PortraitLibrary.Get(id)` / `Find(params tags)` / `Everything()` | `Portrait`: `Field Tags Source Sources InGameTable External CandidateOnly Variants Has HasAll HasVariant HasFrame`. |
| `PortraitPools.PoolOf / Members / Names / Pick` | Pools. |
| `Sources.Of(id) / Selected(id) / Chosen(id) / Main / Active(source) / Label(source)` | Source state. |
| `Packs.PriorityOf / TopOf / NameOf / ImageScaleOf / AuthoredScale / PixelOf / DialogOf` | Pack info. |
| `Plan Painter.PlanOf(string key)` | How a table key renders (`Mode Source TableOwner Scale Pixel`). |
| `DayNight.IsNight` | Shop closed → sleep settlement. |
| `Painter.Report()` · `Painter.HealthReport()` · `Painter.SurfaceReport()` | Status, recorded problems / slow steps, patch status. |

### 5.5 Drawing on items and screens

| Member | Purpose |
|---|---|
| `bool Painter.Paint(GameItem, string group, string key, int cells=4, string variant=null)` | Paint an image (group `Painter.PortraitGroup`) onto an item and set its shape to cells×cells. `key` with source = pinned. |
| `bool Painter.SetVariant(GameItem, string variant)` | Switch variant (null = base); static items use their image group. |
| `bool Painter.Restore(GameItem)` · `IsPinned` · `Unpin` · `PaintId SourceOf(GameItem)` | Re-attach / pin state. |
| `Sprite Painter.Get(group, key, cells=4, variant=null)` | Baked item-sized image. |
| `Sprite Painter.Resolve(portraitId, state=null[, Display.Usage])` = `Display.Get` | Full image aligned to the vanilla canvas; `WaistUp`/`BustUp` crops. |
| `string Display.Key(portraitId, state=null)` | Registers a game-table key (`texext_…`) usable as `StoreClient.spriteName`. |
| `bool Display.ShowOnCounter(portraitId, state=null)` | Replace the counter image now. |
| `Painter.DefineVanillaGroup / DefineTagGroup / DefineGroup / Keys / HasGroup` | Named image groups for painting. |
| `HdRender.Mark(Sprite)` / `Unmark` / `IsHd` | Render a self-managed sprite at screen resolution while on the counter. |

### 5.6 Companion / infrastructure (not for regular mods)

`Painter.UseCacheStore(ICacheStore)`, `Painter.UseImageReader(Func<string,string,byte[]>)`, `Painter.UseLocalLister(Func<IList<string[]>>)`, `Painter.RescanLocalImages()`, `PackShelf.*`, `PackEntry`, `PortraitLibrary.EmbeddedEntries`, `Painter.Rebuild()`, `Painter.Retire()`, `Painter.DebugButton(modId, page, name, Action)` / `RemoveDebugButtons(modId)` (debug panel; requires `dev.txt` next to the game exe).

---

## 6. Save slot (zero-reference integration)

Game API `ModHelper.SetModData("Lune.ProbablyExtendFramework", key, value)` (per save). Framework reads only; owners write and clear.

| Key | Value |
|---|---|
| `want.portrait.<id>` / `want.item.<id>` | space-separated tags; optional trailing `@sleep` / `@visit` (expiry) |
| `flag.<name>` | any string / number (`flag:name` in conditions) |
| `ban` | space-separated tags |

Without calling `Painter.Notify()` the change applies at the next check point.

### Item tags written by the framework (do not write)

`PEF_CUR` current image (`PaintId.Text`), `PEF_OG` original look, `PEF_INFO` auto/pinned, `PEF_BASE` static-item original + state, `PEF_LOCK` player lock, `PEF_AUTO` state given by tag matching, `PEF_PULSE` active pulses, `PEF_LM_MOOD` lewd_meme mood.

### Files

`ModData/Lune_ProbablyExtendFramework/`: `Sources.json`, `Fit.json`, `PortraitPreset/*.json`, `Workshop/TextureExtend.csv` (workshop export; loaded as source `Lune.ProbablyExtendFramework.Workshop`), `Workshop/Catalog.json`, `Local/`.

---

## 7. Recipes

**Story-driven portrait (e.g. dialogue choice):**
```csharp
choice.SetStartAction(() => Painter.Want(PaintTarget.Portrait, "clara", "ep1", "accept"));
// images: clara.png, clara_ep1.png, clara_ep1_accept.png (any registration channel)
```

**Item look from a game state, no code:** `TextureExtend.csv`
```
kind,target,id,tag,when,weight
rule,item,plant_pot,wet,tag:WET_SOIL,10
```
+ image `Textures/Items/plant_pot_wet.png`.

**Temporary state:** `Painter.Pulse(id, PaintTarget.StaticItem, "plant_pot").On("water").Tag("wet").Until("sleep");` then `Painter.Fire("water", item);`.

**Custom NPC visitor:** register portrait `clara` (pool omitted → `custom`), create a `StoreClient` with `spriteName = "clara"`; arrival / trade / leave variants switch automatically when images named `clara_visit`, `clara_sold`, … exist.

**Hide adult variants:** tag them `adult` (variant tags) and call `Painter.Ban("adult")` from your settings.

---

## 8. Pitfalls

- Item images outside `Textures/Items/` are not item sprites (framework logs `item image in the wrong folder`).
- A variant without a registered base is dropped (logged `variant without a base image`).
- `series` rows and `sprite` keys need the mod enabled; disabled packs must use `ModID:path` / json `file`.
- Auto-collection of `itemId_variant` is local-mods only; Workshop releases must register via csv / json.
- `Painter.Decide(StaticItem, …)` is item-agnostic; use `StateOf(item)` for a specific item.
- The framework's `SpriteDict.GetSprite` postfix runs with `Priority.Last`; other postfixes see the vanilla result. To know the final image use `Painter.PlanOf(key)` / `Painter.Decide`.
- `PaintRule.Cond` / conditions may receive `null` (counter, `Decide`).
- Do not write `PEF_*` item tags or `want.*` keys you do not own.
