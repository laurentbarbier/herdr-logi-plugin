# Release checklist

Open items, then the steps for every release. Tick items off as they are done.

## Done for 0.1.x

- [x] Create `github.com/niklasberglund/herdr-logi-plugin` and add it as the
      remote. The manifest and README URLs match that name.
- [x] Commit and push; CI passes on GitHub.
- [x] Hardware verification: all three groups render, ring colours, offline
      ring, pane cycling, terminal detection and the `terminalApp` override.
- [x] `supportedDevices: MxCreativeKeypad`. The MX Keypad is the MX Creative
      Keypad hardware relaunched; verified to load under the Node runtime (0.1.1).
- [x] Changelog dated, `npm run check` passes, `v0.1.0` and `v0.1.1` tagged and
      released with `Herdr_X.Y.Z.lplug4` attached.

## C# port (0.2.0)

Logitech pulled the Node.js plugin from the marketplace after a Node.js
vulnerability was announced in October 2026 and asked for a C# version.

- [x] Port to C# on `LogiPluginApi` 6.4.2.3567; remove Node.js from the repo.
- [x] Loads in the Plugin Service and drives the MX Keypad from a dev link.
- [x] Full hardware pass: all three groups render, ring colours, offline ring,
      labels, pane cycling, terminal detection and the `terminalApp` override.
- [x] Check that keys assigned under 0.1.x keep working after updating from an
      installed 0.1.2 package (the action names are unchanged).
- [ ] Reply to Logitech's email when resubmitting: built on the C# SDK, no
      Node.js runtime.

## Before marketplace submission

- [ ] Download the latest release asset and install it by double-clicking,
      with the dev link removed (`dotnet clean src/HerdrPlugin`). Confirm the installed copy
      works on its own, then uninstall it from Options+ and relink.
- [ ] Take a photo of the keypad showing live tiles for the listing (and the
      README).
- [ ] Add repo topics: `herdr`, `logitech`, `logi-actions-sdk`, `mx-keypad`,
      `mx-creative-keypad`, `claude-code`, `codex`.

## Marketplace submission

- [ ] Submit at https://marketplace.logi.com/contribute: accept the developer
      agreement, upload `Herdr_<version>.lplug4`, and fill in the listing.
      - Description: the README intro.
      - Detail page description: markdown, 500 characters max including
        markup. Summarise the intro, the ring colours, the three key groups,
        requirements and privacy; link the README for the rest. Check the
        length with `wc -m`.
      - Release notes: markdown. For the first marketplace release, describe
        the whole plugin as "First release" rather than the changelog entries
        since 0.1.0; after that, the changelog entries since the last
        marketplace version. Keep them under 500 characters in case the same
        limit applies.
      - Privacy statement: the README's "Privacy" section.
      - Homepage, support and license URLs: as in the manifest.
      - Reviewer notes: macOS only; needs herdr 0.8.2+ running locally;
        without herdr every key shows the dark red dotted offline ring.
        Ask them to confirm `MxCreativeKeypad`. Needs Logi Plugin Service 6.4+. The manifest's commented-out
        `#pluginFolderWin` line marks it macOS-only (confirmed by Logitech).
- [ ] Review takes about ten working days. If there is no reply, email
      marketplace@logitech.com.
- [ ] After approval, add the marketplace link to the README's Install section.

## Other

- [ ] Submit to awesome-herdr: one or two verb-first sentences under
      "Voice, hardware, and remote bridges". See its `AGENTS.md` for the entry
      format.
- [ ] Decide whether to build remote herdr support. Scoped in
      [docs/work-packages/remote-herdr.md](docs/work-packages/remote-herdr.md).

## Every release

1. Bump `version` in `package/metadata/LoupedeckPackage.yaml`; the build reads it from there.
2. Move the Unreleased notes in `CHANGELOG.md` under the new version with today's date.
3. `dotnet test Herdr.slnx`.
4. Commit, tag `vX.Y.Z`, push the commit and the tag.
5. Check the GitHub Release and its `Herdr_X.Y.Z.lplug4` asset.
