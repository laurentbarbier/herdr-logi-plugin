# Contributing

## Build from source

Needs the .NET 10 SDK (`brew install dotnet`) and Logi Options+.

```sh
git clone https://github.com/niklasberglund/herdr-logi-plugin.git
cd herdr-logi-plugin
dotnet build src/HerdrPlugin
```

A Debug build on a Mac writes
`~/Library/Application Support/Logi/LogiPluginService/Plugins/HerdrPlugin.link`,
which points the Logi Plugin Service at `build/Debug`, and asks the service to
reload the plugin. `dotnet watch build --project src/HerdrPlugin` rebuilds and
reloads on every change. `dotnet clean src/HerdrPlugin` removes the link.

Do not keep a dev link and an installed package at the same time: the service
loads whichever it finds first and refuses the other ("plugin 'Herdr' is
already loaded"). Uninstall the package in Options+ before linking.

The reload request does not always take: if a change does not show, restart
the Plugin Service (`killall LogiPluginService`, then open
`/Applications/Utilities/LogiPluginService.app` if it does not come back).

## Commands

```sh
dotnet test Herdr.slnx                             # unit tests
dotnet build Herdr.slnx -c Release                 # package layout in build/Release
logiplugintool pack build/Release Herdr.lplug4     # package for distribution
logiplugintool verify Herdr.lplug4
dotnet run --project tools/Images -- icon          # regenerate package/metadata/Icon256x256.png
dotnet run --project tools/Images -- deck          # regenerate the README key images in docs/images
```

Install the packaging tool with
`dotnet tool install --global LogiPluginTool --version 6.4.2.3599`.

Environment variables: `HERDR_SOCKET_PATH` overrides the herdr socket,
`HERDR_LOGI_CONFIG` overrides the config file path.

## How it works

```
MX Keypad ─────────── Logi Plugin Service ── this plugin (C#, loaded in-process)
                                                 │ every second:
                                                 │   session.snapshot
                                                 │ on press:
                                                 │   workspace.focus / pane.focus
                                                 ▼
                                         ~/.config/herdr/herdr.sock
```

- The plugin polls herdr's `session.snapshot` once a second and calls
  `ActionImageChanged()` for each key whose status or label changed. The
  service then asks for the new image and label, and the plugin answers with
  an 80x80 PNG ring drawn with no image library.
- Each of the 27 keys is its own `TileCommand` instance, added with
  `AddDynamicAction()` and named `herdr_space_1` and so on, the names the
  Node.js versions used. Profiles that already have Herdr keys on them keep
  them, and the per-key icons in `package/actionicons` and
  `package/actionsymbols` match by that name. The service also tries to create
  a `TileCommand` itself and logs a harmless "Cannot create dynamic action"
  warning when it finds no parameterless constructor.
- Presses are queued so rapid taps see the focus state left by the previous
  one. A Space key press fetches a fresh snapshot to decide between focusing
  the workspace and cycling its panes.
- Recent Agent order changes only when an agent's status changes, never on
  focus, so keys do not reorder under the press that focused them. Agents
  already running when the plugin starts keep their workspace order.
- The terminal to raise is found by walking up the process tree from the
  herdr client to its `.app` bundle, then falling back to a known terminal
  that is running, then to one that is installed. The result is cached for
  30 seconds.

## Layout

```
Herdr.slnx                         solution
Directory.Build.props              shared settings; the version is read from the manifest
src/Herdr.Core/                    everything that does not need the Logi Plugin API
  HerdrClient.cs                   herdr socket client and snapshot shaping
  Terminal.cs                      config file and terminal detection and activation
  Tiles.cs                         Space, Agent and Recent Agent tiles and the press queue
  TileBoard.cs                     poll loop; decides which keys need redrawing
  TileFaces.cs                     ring colours, image cache and label truncation
  RecencyTracker.cs                most-recently-active ordering for the Recent Agent keys
  RingPng.cs                       dependency-free PNG ring renderer
src/HerdrPlugin/                   the plugin: Herdr.Core compiled in, plus the SDK glue
tests/Herdr.Core.Tests/            unit tests (xUnit)
tools/Images/                      icon and README image renderer
package/                           plugin manifest, icon, per-action icons
assets/                            keypad frame the README pictures are drawn into
docs/                              README images, work packages
```

`Herdr.Core` is a separate project so the tests and the image tool can use it
without the Logi Plugin API. The plugin compiles its sources in directly, so it
ships as a single `HerdrPlugin.dll`.

The plugin needs a `ClientApplication` subclass (`HerdrApplication`) even
though it is not tied to an app: without one the service refuses to load it
and logs only "Cannot load plugin".

## README images

`dotnet run --project tools/Images -- deck` draws the README pictures with the
plugin's own ring renderer, so they cannot drift from the key faces the device
gets. The faces go behind `assets/keypad-frame.png`, whose key windows are
transparent; the windows are found in its alpha channel, so replacing the
frame moves the faces with it. Add `--live` to draw the herdr session running
right now instead of the demo one. It needs an SVG rasteriser
(`brew install librsvg`) and writes the SVG instead of the PNG if none is
installed.

## Releases

See [RELEASE.md](RELEASE.md). In short: bump the version in
`package/metadata/LoupedeckPackage.yaml`, update `CHANGELOG.md`, and push a
`vX.Y.Z` tag. The release workflow attaches `Herdr_X.Y.Z.lplug4` to a GitHub
Release.
