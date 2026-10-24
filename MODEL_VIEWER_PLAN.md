# Built-in 3D set viewer: feasibility

A viewer can live inside this desktop app. A separate external application is not a technical requirement. Reusing a renderer is a much smaller project than implementing WoW's model, animation, material and character-equipment formats from scratch.

The inspected server data folder contains DB2/GT definitions, cameras and terrain/navigation data. Appearance tables are present: ItemModifiedAppearance, ItemAppearance, ItemDisplayInfo, ItemDisplayInfoMaterialRes, ItemDisplayInfoModelMatRes, ChrModel and character material/texture-layer tables. These provide mappings, not the actual character/item meshes and textures. A matching game client's assets must be located and verified against the installed server tables (build 1.60.1.70450). The first implementation now uses C:\Program Files (x86)\World of Warcraft, client 1.60.1.70532. Actual Dreadnaught shoulder meshes/skin and a weapon were verified against server appearance data; broader cross-build compatibility is still unproven.

Our custom item IDs must resolve through their current appearance mappings. Sending a newly created item ID to an online public item viewer would not reliably show its appearance. Duplicated items can reuse their base appearances, including Dreadnaught-looking pieces with mail gameplay properties. Changing armor material does not itself require making a new model.

Suggested implementation:

1. Verify asset access for the exact client build. Resolve one native item and a duplicated custom item to the same appearance. Load one weapon/helm/shoulder with correct textures, orbit and zoom inside an isolated read-only viewport.
2. Render a selectable character race/body with the entire selected set. Full outfits require texture compositing, geoset visibility and equipment attachments; armor is not always a separate standalone mesh.
3. Add saved-character appearance, animations, hide helm/cloak, lighting and screenshots. Cache loaded assets and update equipment without reloading the catalog. The viewport should fail independently of database editing and never write game assets.

Original candidate approaches (the implementation below uses native WPF):

- Reuse an existing WoW-capable renderer, hosted in the desktop window. WoW Model Viewer's current project demonstrates an embedded renderer and equipment/character display. Exact compatibility with this custom build still needs testing: https://github.com/wowmodelviewer/wowmodelviewer
- Use an asset-conversion/cache stage and an embedded 3D viewport. wow.export documents M2 previews, client/CDN asset access and glTF export: https://github.com/Kruithne/wow.export
- Implement a native reader/renderer. This offers control but has the largest scope because of skinning, attachments, BLP textures, materials, animation and client-version differences.

Before reusing source or distributing a renderer, check its license and packaging requirements. Start with a small compatibility prototype before selecting the permanent rendering stack. A first standalone-item implementation is now included in the subsequent model-viewer update: WPF 3D with a bounded M2/SKIN reader, BLP2 texture decoder and TACTSharp archive access. Static dressed-character rendering is included in the character pass below.


## Implemented character pass

Static character rendering now resolves ChrRaceXChrModel/ChrModel displays, saved customization choices, body-material layers and regional armor textures. Equipment geosets, helmet hiding and attachment positions compose a complete compatible outfit. Shared previews expose item-only and on-character choices, while Characters provides a preview-only armory. Native WPF rotation uses Shift-drag trackball input alongside XYZ sliders. The enlarged window hides its own enlargement button. Dreadnaught and an actual saved character are read-only rendering fixtures. Animation, shader parity, some collection/customization models and live character equipment writes are outside this pass.
