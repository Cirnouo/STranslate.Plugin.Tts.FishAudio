# Bundled language flags

The country artwork is from [flag-icons 7.5.0](https://www.npmjs.com/package/flag-icons/v/7.5.0), Copyright (c) 2013 Panayiotis Lipiridis, under the complete [MIT license](../Licenses/flag-icons.LICENSE.txt). The notice is copied into the distributable. The neutral `language.png` globe is original project artwork.

Pinned source archive: `https://registry.npmjs.org/flag-icons/-/flag-icons-7.5.0.tgz`, SHA-256 `C0B80BF0E08006A60F56621D6BC49F8C7131F4D1FEF6737A165A673431F4B518`. The source directory is `package/flags/4x3`; the complete upstream notice is `package/LICENSE`.

The development-only [generator](../../../tools/generate-language-flags.cjs) reads two-letter lowercase SVG filenames in ordinal order. `countries.json` fixes that trusted order (257 entries), with 48 × 36 raster cells and 16 columns in `countries.png`. Atlas SHA-256: `A1987D94FD4AFA5C367D3D577899A3F33455A807025D2BCD7B566C80E7464EED`. The generic globe is a separate 48 × 36 raster. All three files are embedded assembly resources. Runtime lookups index this fixed list; provider strings never become file paths or resource URIs.

To reproduce, extract the pinned archive into a temporary development directory, install `sharp` 0.35.5 in a temporary Node environment, make that environment available through `NODE_PATH`, and run:

```powershell
node tools/generate-language-flags.cjs <extracted-package>/flags/4x3 STranslate.Plugin.Tts.FishAudio/Assets/Flags
```

Copy the extracted `LICENSE` byte-for-byte to `Assets/Licenses/flag-icons.LICENSE.txt`, then verify the index, atlas hash and compiled resource regressions. Raster encoding can differ across `sharp`/libvips platforms; inspect any changed bytes and raster output before updating the recorded hash. Node and sharp are asset preparation tools only; no production dependency or network lookup is added.
