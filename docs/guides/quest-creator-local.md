# Quest creator and editor — local preview

These features are local previews and have not been published to GitHub. Database edits can stop or damage a server. Back up all relevant databases and test on a separate server copy.

## Create a quest

1. Open **Quest creator (local)**. Search the saved list of normal quest-giving NPCs and filter by named faction. Inspect the textured 3D model before copying it: drag to rotate, Shift-drag for free rotation, wheel to zoom, or open the larger view. Only the copied NPC's name is editable; its appearance and faction are inherited.
2. Log into WoW with a character. Stand on solid outdoor ground exactly where the NPC should be, face the desired direction, then **log out to character selection**. Choose that character, confirm the instructions and capture its saved location. Stay logged out until saving. Capture expires after 30 minutes; instance, taxi and transport positions are rejected. Ground height must be verified in game.
3. Open **Quest definition** and fill the shared authoring form:
   - **Story & dialogue:** title, instructions, offer text, incomplete dialogue, quest-log completion text, reward dialogue, optional area text. `$N` inserts player name, `$C` class, `$R` race and `$B` a line break. Example: `Welcome, $N!$BWill you help me?`
   - **Objectives:** up to eight ordered objectives. Kill creatures, collect/deliver items, interact with existing world objects, talk to NPCs, learn a spell, meet reputation thresholds, pay copper, defeat enemy players or explore an existing unused area trigger. Use named search pickers. Add, edit, remove or reorder objectives. No objectives makes a simple talk/turn-in quest.
   - **Rewards:** optional base XP and gold, up to four guaranteed items and six choose-one items, up to five reputation rewards, an item given on acceptance, and existing spells cast on acceptance/completion. Item pickers include custom items, normal catalog filters, tooltips and 3D previews. Actual XP and money follow server rates, level scaling, rounding and level-cap rules.
   - **Requirements & chain:** quest/minimum/maximum level, time limit, group size, once/repeatable/daily/weekly/monthly schedule, sharing and raid options, automatic acceptance, race/class restrictions, previous/next quest, exclusive group, profession and reputation requirements. New quest level follows the selected placement character until manually changed. Keep **All races** for custom server races unless their mask mapping is known.
   - **NPC & presentation:** optional other existing quest giver for turn-in, quest category and log zone, and offer/completion/incomplete emotes.
4. Use **Check ! availability** for a saved-character eligibility summary. Review the complete proposed creation before saving. The copied NPC, quest, objectives, rewards and links are saved together; original templates and character records are preserved.
5. Restart the worldserver yourself, then verify placement, marker, acceptance, objectives and rewards in WoW. The app does not restart the server or reset a player's completed quests.

## Edit or delete a created quest

Open **Created quests & NPCs**, select an active creation and choose **Edit selected quest**. Earlier creations are supported. The same form loads current saved values. You may rename the copied NPC and optionally capture a new saved character position. Leaving placement unchanged does not require logging out a character to edit text or rewards.

Review original and proposed quest definitions before saving. Changed objectives are blocked while a character has the quest active; finish or abandon it first. Text/reward-only edits preserve objective IDs. Conflicting concurrent changes are rejected. Edit snapshots remain in the editor backup database.

**Delete selected NPC + quest** removes the copied NPC, its spawn, the quest, its objectives, emote/dialogue rows, exploration relation and offer/turn-in links. It retains the audit record, original templates, item definitions, earned items and character progress. Deletion is blocked if other content depends on the copied NPC or quest. IDs of deleted quests and objectives are reserved to avoid reusing completed content. Restart the worldserver after deletion.

## Missing ! marker

The marker depends on level, race/class restrictions, prerequisite quests, completion status, repeat schedule and whether the NPC is friendly. Much lower-level quests may need low-level/trivial quest tracking enabled in WoW. Automatic acceptance can put the quest directly into the log. Saved changes are not the running worldserver's cached state until restarted. The availability button reports database checks and relevant limitations; it cannot guarantee the live game's marker.

## NPC model cache

Settings contains a separate **NPC model cache database**, default `wow_item_editor_npc_cache`. It stores the validated NPC list, readable faction names and source/model definitions, while 3D assets remain in the configured client and local asset cache. Normal openings load the saved list without rendering/revalidating every candidate.

The initial evaluation takes time. Use **Settings → Re-evaluate NPC data** after adding NPCs or changing source/client data. It checks normal existing quest givers and publishes the replacement list only after a successful scan. Cancellation or failure keeps the previous saved list. Connection/folder/database changes must be saved and the app restarted before re-evaluating with them. The account needs access to create/write this separate cache database; the app creates missing cache tables automatically. A cache is disposable and can be rebuilt; it is not a server backup.

## Limits

This is a database quest authoring tool. Existing collection drops and world-object quest-credit behavior must already work, or be configured through the relevant item/server tools. Exploration uses existing unused triggers; it does not create new trigger volumes. Escort AI, bespoke events, cinematics and automatic map-objective polygons require additional server/client scripting and are not generated here. The editor does not create new models or change player inventory/progress. Native shader effects are approximated; unsupported copied NPC appearances are excluded.
