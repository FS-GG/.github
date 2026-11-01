# Headless Chromium on fdev: one supported route

Delivery: [#5193](https://github.com/FS-GG/.github/issues/5193), under [#5179](https://github.com/FS-GG/.github/issues/5179).
Atlas: `environment/fdev-headless-chromium`.

Playwright's `chrome-headless-shell` (chromium-1243, Chrome 153) does not start on fdev. The image has no
`libnspr4`, `libnss3`, `libatk*`, `libatspi` or `libX*` runtime libraries (`ldd` lists them as not found), and no
fonts, so pages render without text. Root-disk packages are not durable (the disk is regenerated on upgrade) and image
changes are Home-only, so the route below keeps everything on the persistent home disk. Do not borrow another
lane's library directory (`work-827/chromelibs` and similar).

## Use

```bash
fdev-chromium-env --no-gpu -- <cmd...>     # software rendering (SwiftShader), the default
fdev-chromium-env --gpu    -- <cmd...>     # hardware: ANGLE over Vulkan, RADV on the host's AMD GPU
fdev-chromium-env --gpu --check            # ldd both Playwright binaries, show the font match
fdev-chromium-env --gpu --print-args-json  # the launch flags for the mode
```

`~/.local/bin/fdev-chromium-env` prepends the bundle's `usr/lib` to `LD_LIBRARY_PATH` and sets `FONTCONFIG_FILE`.
Neither Chromium nor Playwright reads launch flags from an environment variable, so the wrapper exports them as
`FDEV_CHROMIUM_ARGS` (space separated) and `FDEV_CHROMIUM_ARGS_JSON` for your launcher to pass as `args`:

```js
const browser = await chromium.launch({ args: JSON.parse(process.env.FDEV_CHROMIUM_ARGS_JSON) });
```

| Mode | Flags | WebGL renderer observed |
|---|---|---|
| `--no-gpu` | `--disable-gpu --enable-unsafe-swiftshader --use-angle=swiftshader` | `ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) ...` |
| `--gpu` | `--use-angle=vulkan --enable-features=Vulkan --ignore-gpu-blocklist` | `ANGLE (AMD, Vulkan 1.4.354 (AMD Ryzen 9 7900 12-Core Processor (RADV RAPHAEL_MENDOCINO) ...), radv)` |

Without flags Chromium falls back to SwiftShader on its own, so a lane that wants hardware must pass the `--gpu`
flags. `--use-angle=gl` produced no WebGL context on fdev; `--use-gl=angle --use-angle=gl-egl` reaches radeonsi
([FS.GG.Game#746](https://github.com/FS-GG/FS.GG.Game/issues/746) probes). Raw `chrome` also needs `--no-sandbox`;
Playwright adds it. Both Playwright's headless shell and `channel: 'chromium'` work in both modes.

## Bundle

`~/.local/share/fdev-chromium/<version>/` (`current` points at the active one) holds `root/usr/lib` (libraries),
`root/usr/share/fonts` (DejaVu, Liberation), `fonts.conf` (system fontconfig rules plus the bundled fonts, cache in
the bundle), `smoke.mjs`/`smoke.html`, and `manifest.json`, which records each package's name, version, repository,
file sha256, signature sha256 and download URL, and every extracted file's sha256. Packages are official Arch
packages from the dated Arch Linux Archive snapshot configured in `/etc/pacman.d/mirrorlist`, downloaded and never installed:

```bash
sudo -n pacman -Sw --noconfirm -dd --disable-sandbox --cachedir "$DL" nss nspr at-spi2-core \
  libxcomposite libxdamage libxfixes libxrandr libcups libxi libxtst ttf-dejavu ttf-liberation
sudo -n chown -R "$USER": "$DL"
bsdtar -xf "$DL"/<pkg>.pkg.tar.zst -C "$BUNDLE/root" --exclude='.*'   # per package; prune include/man/doc/locale
FONTCONFIG_FILE="$BUNDLE/fonts.conf" fc-cache -f
```

Bundle libraries come first on `LD_LIBRARY_PATH` only for commands run through the wrapper. Do not export it
globally: it would put these libraries ahead of the system's for every process.

## Smoke test

```bash
fdev-chromium-env --gpu -- node ~/.local/share/fdev-chromium/current/smoke.mjs <playwright-core dir> \
  ~/.local/share/fdev-chromium/current/smoke.html [headless-shell|chromium]
```

It renders the local page, screenshots it, and checks a red box, the white background, dark glyph pixels for three
text blocks, the page text, the fonts Chromium chose (`CSS.getPlatformFontsForNode`) and the WebGL renderer string.
It exits with `pass: true` in the JSON when every check holds.

## Durable fix

The image should carry these packages so the bundle's libraries become redundant. That is a Home-only image change,
held as a proposal in the SystemAdmin source-proposal custody. Until it lands, this wrapper is the supported route.
Do not run `pacman -S` on fdev for this: the root disk is regenerated on upgrade.
