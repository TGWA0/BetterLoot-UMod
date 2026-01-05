## Support and Assistance
Please refer to our [Discord Server](https://discord.com/invite/ArTgweeM6q) for fast support and bug fixes. Whether on looty or the plugin itself.

### SRT Bull's Tutorial
You can watch his amazing up to date tutorial -> [right here](https://www.youtube.com/watch?v=I9L4EDGh5gk)!

### Converting your Old Loot Tables
Old loot table files are **not compatiable** with V4, to convert them go to [looty.cc/converter](https://looty.cc/converter), upload your existing `LootTables.json` (found in `oxide/data/BetterLoot/`). All future updates will be automatically converted by the plugin.

## Web Configuration
Use [looty.cc/betterloot-v4](https://looty.cc/betterloot-v4) to easily edit, Loot tables, Loot groups, item rng, skins, attachments and more

### Automatic Upload to your Server
After creating or converting a loot table on [Looty](https://looty.cc/betterloot-v4):
1. Press **[Download]** and then **[Download via Command]** to generate your unique loot table ID.
2. Copy the command shown and paste it into either RCON or Chat.
- **Usage** — `/looty {looty-id}`
- **Permission** — `BetterLoot.admin`

## Commands & Tools
### Blacklist (Chat)
Add or remove an item from the blacklist to block it from being added to NPC or loot container inventories.
- **Usage** — `/blacklist <additem | deleteitem> "item shortname"`
- **Permission** — `BetterLoot.admin`

### Backup / Restore
Manual commands to back up or restore edited files. Backups are also created automatically when using the Looty API command.

**Backup**
- **Usage** — `/bl-backup`
- **Permission** — `BetterLoot.admin`

**Restore**
- **Usage** — `/bl-restore`
- **Permission** — `BetterLoot.admin`

## Basic Configuration
- **Blueprint Probability**  
  Chance (%) of a blueprint spawning.

- **Log Updates on Load**  
  Lists plugin updates when loading, useful for debugging.

- **Remove Stacked Containers**  
  Clears any containers stacked inside each other.

- **Watched Prefabs**  
  Prefabs the plugin manages, including scientists.

- **Hammer Loot Cycle Time**  
  Seconds until loot refreshes when hitting a container with a hammer.

  When enabled, admins can use a hammer to hit a valid entity and open its loot panel, which will keep updating until closed - great for testing loot generation.

- **Loot Multiplier**  
  General multiplier for loot inside containers (more detailed settings available on [Looty](https://looty.cc/betterloot-v4)).

- **Scrap Multiplier**  
  Basic multiplier for scrap inside containers.

## Default Configuration File
```json
{
  "Chat Configuration": {
    "Chat Message Prefix": "[<color=#00ff00>BetterLoot</color>]",
    "Chat Message Icon SteamID (0 = None)": 0
  },
  "General Configuration": {
    "Blueprint Probability": 0.11,
    "Log Updates On Load": true,
    "Remove Stacked Containers": true,
    "Watched Prefabs": [
      "...list of prefabs..."
    ]
  },
  "Loot Configuration": {
    "Enable Hammer Hit Loot Cycle": false,
    "Hammer Loot Cycle Time": 3.0,
    "Loot Multiplier": 1,
    "Scrap Multipler": 1,
    "Allow duplicate items": false,
    "Enable logging for item attachments auto balancing operations": false,
    "Always allow duplicate items from bonus items list (if set, will override 'Allow duplicate items option')": true
  },
  "Loot Groups Configuration": {
    "Enable creation of example loot group on load?": true,
    "Enable auto profile probability balancing?": true,
    "Always allow duplicate items from loot groups (if true overrides 'Allow duplicate items option')": true
  }
}
```
(Note: We’ve left out the full list of prefabs here to keep things shorter.)

## Stored Data
- `oxide/config/BetterLoot.json` — Global plugin settings.
- `oxide/data/BetterLoot/LootGroups.json` — Custom loot group data.
- `oxide/data/BetterLoot/LootTables.json` — Individual prefab loot table data.
- `oxide/data/BetterLoot/Blacklist.json` — Disabled item shortnames.

## Developer API
**NPCPlayerCorpse**
```csharp
// Return true/false to cancel loot population. Corpse ID / NPC steam id may be reffered from `corpse.playerSteamID`
object? ShouldBLPopulate_NPC(ulong corpseId)
```
**LootContainer**
```csharp
// Return true/false to cancel loot population
object? ShouldBLPopulate_Container(ulong lootContainerId)
```

## Credits
- **dcode** — Original author.
- **Fujicura** — Maintainer.
- **Misticos** — Maintainer.
- **Tryhard** — Maintainer.
- **[TGWA](https://umod.org/user/TGWA)** — Author of Version 4 & Current Maintainer.