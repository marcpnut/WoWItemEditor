# Architecture and folder guide

The repository contains one WPF application and its existing verification scenarios. The folder reorganization keeps the current behavior and namespace unchanged.

## Layout

```text
WoWItemEditor.slnx
src/WoWItemEditor/
  App.xaml                    Application entry point
  Configuration/              Local encrypted settings
  Data/                       Connections, storage setup and data readers
  Models/                     Shared catalog models
  Features/
    Catalogs/                 Search and reference catalogs
    Drops/                    World-drop and instance-boss planning
    History/                  Snapshots, audit and revert/reapply
    Items/                    Editing, cloning, calculations and safety
    Sets/                     Creation, membership, refresh and review
    Spells/                   Spell copying, explanations and bonus rules
  Rendering/                  Client assets, textures, meshes and skeletal poses
  Controls/                   Reusable WPF controls and previews
  Views/                      Screens grouped by feature; Shell owns MainWindow
  Themes/                     Shared WPF styles
  WoWItemEditor.csproj
tests/WoWItemEditor.Verification/  Existing verification scenarios
docs/
  guides/                     Item, set and weapon-proc instructions
  screenshots/                Selected documentation screenshots
licenses/                     Third-party notices and licenses
```

Character repositories are in Data and their UI is in Views/Characters.

## Working on the app

Open WoWItemEditor.slnx in an IDE or run `dotnet build WoWItemEditor.slnx -c Release` from the repository root. The launchers use the project under src/WoWItemEditor. App.xaml loads Themes/Theme.xaml by its resource path.

Put data reads and connection setup in Data, business rules under the matching Features folder, complete windows in Views, and reusable UI in Controls. Keep mesh/texture/pose decoding in Rendering. Existing types retain the WoWItemEditor namespace so this structural change does not alter XAML type bindings or public model names.

Verification files remain partial MainWindow classes linked into the app by the project file. They need internal editor and WPF access and are not a separate test executable. Add scenarios under tests/WoWItemEditor.Verification and use the documented command-line flags. The paths named Verification in an IDE are linked source files from that directory.

Build outputs, proprietary data, caches, local settings and verification reports are ignored by Git. Existing running builds under artifacts are left in place. Only deliberately selected, reviewed documentation screenshots belong in docs/screenshots.
