#!/usr/bin/env node
// Renders src/PromptHelper/Assets/PromptHelperLogo.svg natively at each required icon size
// (16, 24, 32, 48, 64, 128, 256) and packs the results into a PNG-frame ICO container.
//
// Unlike tools/GenerateAppIcon.ps1 (which rasterizes once at 256x256 via ImageMagick and
// downsamples the smaller frames from that single raster), every frame here is rendered
// directly from the vector source at its own target resolution, so small sizes are not
// degraded by resampling a larger bitmap.
//
// Usage: node tools/GenerateAppIconNative.js <source.svg> <output.ico>
// Requires the "sharp" package (npm install sharp) to be available to Node's module
// resolution when this script is run.

const sharp = require("sharp");
const fs = require("fs");
const path = require("path");

const SIZES = [16, 24, 32, 48, 64, 128, 256];

async function main() {
  const [, , svgPath, outPath] = process.argv;
  if (!svgPath || !outPath) {
    console.error("Usage: node GenerateAppIconNative.js <source.svg> <output.ico>");
    process.exit(1);
  }
  if (!fs.existsSync(svgPath)) {
    console.error(`Source SVG not found: ${svgPath}`);
    process.exit(1);
  }

  const frames = [];
  for (const size of SIZES) {
    const png = await sharp(svgPath, { density: 384 })
      .resize(size, size, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png({ compressionLevel: 9 })
      .toBuffer();
    frames.push({ size, png });
  }

  const headerSize = 6;
  const entrySize = 16;
  const dirSize = headerSize + entrySize * frames.length;

  const header = Buffer.alloc(headerSize);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // type = icon
  header.writeUInt16LE(frames.length, 4); // count

  const entries = [];
  const imageBuffers = [];
  let offset = dirSize;

  for (const frame of frames) {
    const entry = Buffer.alloc(entrySize);
    const dim = frame.size >= 256 ? 0 : frame.size; // 0 means 256 per the ICO format
    entry.writeUInt8(dim, 0);
    entry.writeUInt8(dim, 1);
    entry.writeUInt8(0, 2); // color count: 0 = true color, no palette
    entry.writeUInt8(0, 3); // reserved
    entry.writeUInt16LE(1, 4); // color planes
    entry.writeUInt16LE(32, 6); // bits per pixel
    entry.writeUInt32LE(frame.png.length, 8);
    entry.writeUInt32LE(offset, 12);
    entries.push(entry);
    imageBuffers.push(frame.png);
    offset += frame.png.length;
  }

  const ico = Buffer.concat([header, ...entries, ...imageBuffers]);
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, ico);
  console.log(`Wrote ${outPath} (${ico.length} bytes, ${frames.length} frames: ${SIZES.join(", ")})`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
