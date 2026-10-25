# Quest creator — local preview

Open **Quest creator (local)** from the sidebar. This feature is being tested locally and is not published to GitHub.

1. Search existing normal quest-giving NPCs by name or entry. Select one to load its textured 3D appearance. Drag to rotate, Shift-drag for free rotation, use the wheel to zoom, or open the larger view. If the template has multiple appearances, inspect them with the appearance selector. Only the new name is editable. Unsupported appearances block copying.
2. Log into WoW with a character. Stand on solid outdoor ground exactly where the NPC should stand and face the desired direction. Log out to character selection so the server saves the coordinates and facing. Choose that character, confirm the ground instructions, and capture its saved location. Keep it logged out until creation completes. Location capture expires after 30 minutes. Instances, transports and taxi positions are excluded.
3. Enter the quest title, level, summary, dialogue, and one guaranteed item reward. The reward browser includes custom items. Review the proposed NPC, coordinates, quest and reward before creating them.
4. Restart the worldserver yourself when ready. The app does not restart it. Verify the placement and quest in WoW.

This first version creates a one-time quest accepted and turned in at the same new NPC. Kill/collection objectives and editing previously created quests are not implemented yet. Original NPCs and rewards are preserved. The new NPC retains its source faction, so choose a quest giver friendly to the intended players. Existing scripts, services, quests and loot are not copied. Creation records are stored in the configured backup database.

The viewer uses native character customizations, baked NPC body textures, clothing displays, held equipment and a standing pose. It approximates shader blending and does not simulate spell effects or the full game's animations. Templates with unsupported special geosets, mounted/aura appearances or weapon appearance effects are blocked. The camera fits each model independently; it is not an absolute world-size comparison. Ground height must be checked in game.
