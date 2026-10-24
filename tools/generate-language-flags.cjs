// Development-only raster generation; the plugin has no SVG/Node dependency.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');

(async () => {
    const [source, destination] = process.argv.slice(2);
    if (!source || !destination) throw new Error('Usage: node generate-language-flags.cjs <flag-icons/flags/4x3> <output-directory>');
    fs.mkdirSync(destination, { recursive: true });
    const countries = fs.readdirSync(source).filter(name => /^[a-z]{2}\.svg$/.test(name))
        .map(name => name.slice(0, -4)).sort();
    const tiles = [];
    for (let index = 0; index < countries.length; index++) {
        const input = await sharp(path.join(source, countries[index] + '.svg')).resize(48, 36).png().toBuffer();
        tiles.push({ input, left: index % 16 * 48, top: Math.floor(index / 16) * 36 });
    }
    await sharp({ create: { width: 16 * 48, height: Math.ceil(countries.length / 16) * 36,
        channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } } })
        .composite(tiles).png().toFile(path.join(destination, 'countries.png'));
    fs.writeFileSync(path.join(destination, 'countries.json'), JSON.stringify(countries) + '\n');
    // Original neutral language icon, used for unknown codes and numeric regions.
    const generic = '<svg xmlns="http://www.w3.org/2000/svg" width="48" height="36" viewBox="0 0 48 36"><g fill="none" stroke="#888" stroke-width="2.5"><circle cx="24" cy="18" r="14"/><ellipse cx="24" cy="18" rx="6" ry="14"/><path d="M10 18h28M13 10h22M13 26h22"/></g></svg>';
    await sharp(Buffer.from(generic)).png().toFile(path.join(destination, 'language.png'));
    console.log(`Rendered ${countries.length} flags and the neutral language icon.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
