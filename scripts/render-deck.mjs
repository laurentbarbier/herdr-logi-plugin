// Renders the README's key-face images: the keypad with nine live tiles in it,
// and a strip of the seven tile states. The rings are the plugin's own PNGs,
// drawn by the same code that answers GetActionImage on the device, so the
// pictures cannot drift from what the keypad shows. Labels go through
// labelText() as well; only the key face around them is this script's own
// approximation of how Options+ draws an icon with its title underneath.
//
//   node scripts/render-deck.mjs          demo state (what the README ships)
//   node scripts/render-deck.mjs --live   the herdr session running right now
//   node scripts/render-deck.mjs --mockups  candidate key-face layouts, under docs/images/mockups/
//
// assets/keypad-frame.png is a rendering of the keypad whose nine key windows
// are transparent, so the faces are drawn underneath it and the frame supplies
// the bezels and their gloss on top.
//
// Rasterising the SVG needs librsvg (`brew install librsvg`) or ImageMagick.
// Without either, the SVG is written instead and can be converted anywhere.
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { inflateSync } from 'node:zlib';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { circlePng, facePng } from '../src/png.ts';
import { projectColor } from '../src/project-color.ts';
import { IMAGE_SIZE, labelText, styleFor } from '../src/status-bridge.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const OUT_DIR = join(root, 'docs/images');
const FRAME = join(root, 'assets/keypad-frame.png');

// One key face in design units: the ring at the size the device receives it,
// with room for the label underneath.
const TILE = 120;
const RING_TOP = 12;
const LABEL_BASELINE = TILE - 17;
const LABEL_SIZE = 15;

const FACE = '#000000';
const BEZEL = '#212226';
const LABEL = '#e7e7e9';
const FONT = "'SF Pro Text','Helvetica Neue',Helvetica,Arial,sans-serif";

// A plausible herdr session: six workspaces open, one waiting on you, two
// agents at work, one turn just finished, and three slots still empty.
const DEMO_DECK = [
  { status: 'blocked', tab: 1, project: 'fix-login' },
  { status: 'working', tab: 2, project: 'refactor-auth' },
  { status: 'done', tab: 3, project: 'migrate-db' },
  { status: 'working', tab: 4, project: 'add-tests' },
  { status: 'idle', tab: 5, project: 'review-pr' },
  { status: 'unknown', tab: 6, project: 'bench' },
  { status: 'none', tab: 7, project: '' },
  { status: 'none', tab: 8, project: '' },
  { status: 'none', tab: 9, project: '' },
];

const STATES = [
  { status: 'blocked', label: 'blocked' },
  { status: 'working', label: 'working' },
  { status: 'done', label: 'done' },
  { status: 'idle', label: 'idle' },
  { status: 'unknown', label: 'unknown' },
  { status: 'none', label: 'empty' },
  { status: 'offline', label: 'offline' },
];

// Each ring is encoded once per size and reused by every tile showing that
// status, exactly as the live bridge caches it. Drawing it at the pixel size
// it will occupy keeps it sharp instead of letting the renderer scale it.
const rings = new Map();
function ring(status, pixels) {
  const key = `${status}@${pixels}`;
  let cached = rings.get(key);
  if (!cached) {
    const { rgb, style } = styleFor(status);
    cached = circlePng(pixels, rgb, style).toString('base64');
    rings.set(key, cached);
  }
  return cached;
}

// The frame's key windows are holes in its alpha channel, so the layout is
// read from the image rather than written down here: replace the frame and the
// faces still land in its windows.
function alphaChannel(png) {
  if (png.readUInt32BE(0) !== 0x89504e47) throw new Error(`${FRAME} is not a PNG`);
  let width = 0;
  let height = 0;
  const idat = [];
  for (let at = 8; at + 8 <= png.length; ) {
    const length = png.readUInt32BE(at);
    const type = png.toString('ascii', at + 4, at + 8);
    const body = png.subarray(at + 8, at + 8 + length);
    if (type === 'IHDR') {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      const [depth, colour, , , interlace] = [body[8], body[9], body[10], body[11], body[12]];
      if (depth !== 8 || colour !== 6 || interlace !== 0) {
        throw new Error(`${FRAME} must be a non-interlaced 8-bit RGBA PNG, with the key windows transparent`);
      }
    } else if (type === 'IDAT') idat.push(body);
    else if (type === 'IEND') break;
    at += 12 + length;
  }
  const raw = inflateSync(Buffer.concat(idat));
  const stride = width * 4;
  const pixels = Buffer.alloc(stride * height);
  // Undo the per-scanline filters; only the alpha byte of each pixel is kept,
  // but filtering refers to whole pixels, so every channel is reconstructed.
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const a = x >= 4 ? pixels[y * stride + x - 4] : 0;
      const b = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const c = x >= 4 && y > 0 ? pixels[(y - 1) * stride + x - 4] : 0;
      let value = line[x];
      if (filter === 1) value += a;
      else if (filter === 2) value += b;
      else if (filter === 3) value += (a + b) >> 1;
      else if (filter === 4) {
        const p = a + b - c;
        const [pa, pb, pc] = [Math.abs(p - a), Math.abs(p - b), Math.abs(p - c)];
        value += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      pixels[y * stride + x] = value & 0xff;
    }
  }
  const alpha = new Uint8Array(width * height);
  for (let i = 0; i < alpha.length; i++) alpha[i] = pixels[i * 4 + 3];
  return { width, height, alpha };
}

// Runs of consecutive indices whose count clears the threshold, which turn the
// holes into three key rows and, within a row, three key columns.
function bands(counts, threshold) {
  const found = [];
  let start = -1;
  counts.forEach((count, i) => {
    if (count > threshold && start < 0) start = i;
    else if (count <= threshold && start >= 0) {
      found.push([start, i - 1]);
      start = -1;
    }
  });
  if (start >= 0) found.push([start, counts.length - 1]);
  return found;
}

function keyWindows({ width, height, alpha }) {
  const opaque = (x, y) => alpha[y * width + x] > 128;
  // A hole only counts where the body encloses it, so the transparent
  // background around the keypad and its rounded corners are ignored.
  const hole = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    let first = -1;
    let last = -1;
    for (let x = 0; x < width; x++) {
      if (opaque(x, y)) {
        if (first < 0) first = x;
        last = x;
      }
    }
    for (let x = first + 1; x < last; x++) if (!opaque(x, y)) hole[y * width + x] = 1;
  }

  const rowCounts = Array.from({ length: height }, (_, y) => {
    let count = 0;
    for (let x = 0; x < width; x++) count += hole[y * width + x];
    return count;
  });
  const rows = bands(rowCounts, 30);
  if (rows.length !== 3) throw new Error(`expected 3 rows of key windows in ${FRAME}, found ${rows.length}`);

  const windows = [];
  for (const [top, bottom] of rows) {
    const columnCounts = Array.from({ length: width }, (_, x) => {
      let count = 0;
      for (let y = top; y <= bottom; y++) count += hole[y * width + x];
      return count;
    });
    const columns = bands(columnCounts, 10);
    if (columns.length !== 3) {
      throw new Error(`expected 3 key windows in a row of ${FRAME}, found ${columns.length}`);
    }
    for (const [left, right] of columns) {
      windows.push({ x: left, y: top, width: right - left + 1, height: bottom - top + 1 });
    }
  }
  return windows;
}

const escapeText = (text) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const dataUri = (base64) => `data:image/png;base64,${base64}`;

// One key face, `scale` pixels per design unit, with its top-left corner at
// (x, y). An empty slot keeps the space its label would occupy so the rows
// stay aligned, which is what the device does too.
function face(tile, x, y, scale, { rounded = false } = {}) {
  const { status, label } = tile;
  const size = TILE * scale;
  if (tile.project !== undefined) {
    // A full 80x80 key face, drawn by the same facePng() the device uses
    // (tab, state ring and the project's own band colour), not approximated.
    const px = Math.round(size);
    const spec = { ring: styleFor(status), band: projectColor(tile.project) };
    if (tile.tab !== undefined) spec.tab = tile.tab;
    if (tile.project) spec.project = tile.project;
    return `
  <g transform="translate(${x},${y})">
    <image x="0" y="0" width="${px}" height="${px}" href="${dataUri(facePng(spec).toString('base64'))}"/>
  </g>`;
  }
  const ringPixels = Math.round(IMAGE_SIZE * scale);
  const text = labelText(label).trim();
  const plate = rounded
    ? `<rect width="${size}" height="${size}" rx="${16 * scale}" fill="${FACE}" stroke="${BEZEL}" stroke-width="${scale}"/>`
    : `<rect width="${size}" height="${size}" fill="${FACE}"/>`;
  return `
  <g transform="translate(${x},${y})">
    ${plate}
    <image x="${(size - ringPixels) / 2}" y="${RING_TOP * scale}" width="${ringPixels}" height="${ringPixels}" href="${dataUri(ring(status, ringPixels))}"/>
    ${text ? `<text x="${size / 2}" y="${LABEL_BASELINE * scale}" text-anchor="middle" font-family="${FONT}" font-size="${LABEL_SIZE * scale}" fill="${LABEL}">${escapeText(text)}</text>` : ''}
  </g>`;
}

// The nine faces sit in the frame's key windows and the frame goes on top, so
// its bezels clip them and its gloss falls over them. The face is scaled to
// the window's width and centred in its slightly taller opening, which keeps
// the ring round.
function keypad(tiles) {
  const png = readFileSync(FRAME);
  const { width, height, alpha } = alphaChannel(png);
  const windows = keyWindows({ width, height, alpha });
  const faces = tiles
    .map((tile, i) => {
      const window = windows[i];
      const scale = window.width / TILE;
      return face(tile, window.x, window.y + (window.height - TILE * scale) / 2, scale);
    })
    .join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${faces}
  <image x="0" y="0" width="${width}" height="${height}" href="${dataUri(png.toString('base64'))}"/>
</svg>
`;
}

// The legend strip: the same faces, standing on their own rather than in the
// keypad, because offline and empty never share a deck with the other states.
function strip(tiles, scale = 2) {
  const size = TILE * scale;
  const gap = 10 * scale;
  const width = tiles.length * size + (tiles.length - 1) * gap;
  const faces = tiles
    .map((tile, i) => face(tile, i * (size + gap), 0, scale, { rounded: true }))
    .join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${size}" viewBox="0 0 ${width} ${size}">${faces}
</svg>
`;
}

// librsvg renders at exactly the requested size and honours the embedded PNGs;
// ImageMagick is the fallback. QuickLook is deliberately not used: it insets
// and rescales the drawing.
function rasterise(svg, out) {
  const scratch = join(tmpdir(), `herdr-deck-${process.pid}.svg`);
  writeFileSync(scratch, svg);
  try {
    for (const [command, args] of [
      ['rsvg-convert', [scratch, '-o', out]],
      ['magick', [scratch, out]],
    ]) {
      try {
        execFileSync(command, args, { stdio: ['ignore', 'ignore', 'pipe'] });
        return out;
      } catch (error) {
        if (error.code !== 'ENOENT') throw error;
      }
    }
  } finally {
    rmSync(scratch, { force: true });
  }
  const fallback = out.replace(/\.png$/, '.svg');
  writeFileSync(fallback, svg);
  console.warn(
    `No SVG rasteriser found, so ${fallback} was written instead of the PNG. Install one with: brew install librsvg`,
  );
  return fallback;
}

// --live reads the running herdr through the plugin's own action classes, so a
// picture of a real session goes through exactly the code path the device
// does. Without herdr running every tile would be offline, which says nothing,
// so that is an error rather than a picture.
async function liveDeck() {
  const { SpaceAction, SLOT_COUNT } = await import('../src/actions.ts');
  const { getSnapshot } = await import('../src/herdr.ts');
  let snapshot;
  try {
    snapshot = await getSnapshot();
  } catch (error) {
    console.error(`--live needs herdr running: ${error.message}`);
    process.exit(1);
  }
  return Array.from({ length: SLOT_COUNT }, (_, i) => {
    const action = new SpaceAction(i + 1);
    action.update(snapshot);
    return { status: action.status, label: action.label, tab: action.face.tab, project: action.face.project };
  });
}

// --mockups renders candidate key faces (tab number, project, session label)
// at the real 80x80 key size and upscaled, so a layout can be chosen before
// any of it is built. It writes only under docs/images/mockups/.
const MOCK_DIR = join(OUT_DIR, 'mockups');
const KEY = 80;

// Candidate project colours. None sits in the red, yellow or green hue ranges,
// which stay reserved for the state ring. Hue angles are in the comments.
const PALETTE = [
  '#3b82f6', // blue 217
  '#8b5cf6', // purple 258
  '#d946ef', // magenta 292
  '#06b6d4', // cyan 189
  '#e07b39', // orange 25
  '#f472b6', // pink 330
  '#14b8a6', // teal 173
];

// Mirrors labelText()'s ellipsis convention with a candidate's own budget.
function truncate(text, max) {
  const chars = Array.from(text);
  return chars.length > max ? chars.slice(0, max - 1).join('') + '…' : text;
}

// Splits a project name over two rows at a separator near the middle, or at
// the middle when it has none.
function twoRows(text) {
  const chars = Array.from(text);
  const middle = Math.ceil(chars.length / 2);
  let cut = -1;
  chars.forEach((c, i) => {
    if (/[-_ .]/.test(c) && (cut < 0 || Math.abs(i - middle) < Math.abs(cut - middle))) cut = i;
  });
  if (cut < 1 || cut >= chars.length - 1) return [chars.slice(0, middle).join(''), chars.slice(middle).join('')];
  return [chars.slice(0, cut + 1).join('').trim(), chars.slice(cut + 1).join('')];
}

const tint = (hex) => {
  const n = parseInt(hex.slice(1), 16);
  const mix = (v) => Math.round(v * 0.32);
  return `rgb(${mix(n >> 16)},${mix((n >> 8) & 255)},${mix(n & 255)})`;
};

const text = (value, x, y, size, fill, extra = '') =>
  `<text x="${x}" y="${y}" font-family="${FONT}" font-size="${size}" fill="${fill}" ${extra}>${escapeText(value)}</text>`;

// One candidate face in a 80x80 box. `text` picks the rendering approach:
// a = everything drawn in the image, b = tab and project in the image with the
// session label as service text under it, c = ring plus one service string.
function mockFace(spec, { layout = 'one', placement = 'text', approach = 'a', projectChars = 9, labelChars = 12, scale = 1 } = {}) {
  const { status, tab, project = '', label = '' } = spec;
  const color = spec.color ?? PALETTE[0];
  const s = scale;
  const parts = [];
  parts.push(`<rect width="${KEY * s}" height="${KEY * s}" fill="${placement === 'tint' && project ? tint(color) : FACE}"/>`);
  if (approach === 'c') {
    const px = Math.round(44 * s);
    parts.push(
      `<image x="${((KEY - 44) / 2) * s}" y="${4 * s}" width="${px}" height="${px}" href="${dataUri(ring(status, px))}"/>`,
      text(truncate(label || project || '', 10), (KEY / 2) * s, 66 * s, 11 * s, LABEL, 'text-anchor="middle"'),
    );
    return parts.join('');
  }
  const px = Math.round(26 * s);
  const ringImage = `<image x="${(KEY - 26 - 3) * s}" y="${3 * s}" width="${px}" height="${px}" href="${dataUri(ring(status, px))}"/>`;
  parts.push(ringImage);
  if (tab !== undefined) parts.push(text(String(tab), 5 * s, 29 * s, 30 * s, '#ffffff', 'font-weight="700"'));
  const rows = !project ? [] : layout === 'two' ? twoRows(project).map((r) => truncate(r, projectChars)) : [truncate(project, projectChars)];
  const rowSize = layout === 'two' ? 11 : 12;
  const rowGap = layout === 'two' ? 12 : 0;
  const top = 34;
  rows.forEach((row, i) => {
    const baseline = top + 11 + i * rowGap;
    if (placement === 'band') {
      parts.push(`<rect x="0" y="${(baseline - rowSize + 1) * s}" width="${KEY * s}" height="${(rowSize + 3) * s}" fill="${color}"/>`);
      parts.push(text(row, 4 * s, baseline * s, rowSize * s, '#ffffff', 'font-weight="700"'));
    } else {
      parts.push(text(row, 4 * s, baseline * s, rowSize * s, placement === 'text' ? color : '#ffffff', 'font-weight="700"'));
    }
  });
  const labelText2 = truncate(label, labelChars);
  if (approach === 'a') {
    const baseline = layout === 'two' ? 72 : 62;
    if (labelText2) parts.push(text(labelText2, 4 * s, baseline * s, (layout === 'two' ? 9 : 10) * s, LABEL));
  } else if (labelText2) {
    parts.push(text(labelText2, (KEY / 2) * s, 74 * s, 10 * s, LABEL, 'text-anchor="middle"'));
  }
  return parts.join('');
}

const svgDoc = (width, height, body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${body}
</svg>
`;

// Cells are laid out in a grid; each face is a self-contained 80x80 square with
// a caption of its own beneath it, so a sheet explains itself.
function sheet(cells, scale, columns) {
  const gap = 8 * scale;
  const captionHeight = 22 * scale;
  const cellW = KEY * scale + gap;
  const cellH = KEY * scale + captionHeight + gap;
  const rows = Math.ceil(cells.length / columns);
  const body = cells
    .map((cell, i) => {
      const x = gap + (i % columns) * cellW;
      const y = gap + Math.floor(i / columns) * cellH;
      const lines = cell.caption.map((line, n) => text(line, (KEY / 2) * scale, (KEY + 8 + n * 8) * scale, 6.5 * scale, '#c9c9cf', 'text-anchor="middle"')).join('');
      return `<g transform="translate(${x},${y})">${mockFace(cell.spec, { ...cell.options, scale })}${lines}</g>`;
    })
    .join('');
  const width = gap + columns * cellW;
  const height = gap + rows * cellH;
  return svgDoc(width, height, `<rect width="${width}" height="${height}" fill="#2a2b30"/>${body}`);
}

async function mockups() {
  const base = { status: 'working', tab: 3, project: 'herdr', label: 'fix-login', color: PALETTE[0] };
  const scenarios = {
    'scenario-short': { ...base },
    'scenario-long-project': { ...base, project: 'herdr-logi-plugin-fork' },
    'scenario-long-label': { ...base, label: 'refactor the authentication layer' },
    'scenario-empty': { status: 'none', tab: 7, project: '', label: '' },
    'scenario-offline': { status: 'offline', tab: 1, project: '', label: '' },
    'scenario-multi-project': { ...base, tab: 5, project: 'billing', label: 'add-tests', color: PALETTE[4], status: 'blocked' },
  };
  const multi = [
    ['herdr', 'fix-login', 'blocked'],
    ['billing', 'add-tests', 'working'],
    ['website', 'migrate-db', 'done'],
    ['ml-pipeline', 'review-pr', 'idle'],
    ['mobile-app', 'bench', 'working'],
    ['infra', 'rollout', 'unknown'],
  ].map(([project, label, status], i) => ({ status, tab: i + 1, project, label, color: PALETTE[i % PALETTE.length] }));

  const faces = {
    ...Object.fromEntries(Object.entries(scenarios).map(([name, spec]) => [name, [spec, {}]])),
    'color-text': [base, { placement: 'text' }],
    'color-band': [base, { placement: 'band' }],
    'color-tint': [base, { placement: 'tint' }],
    'text-a': [base, { approach: 'a' }],
    'text-b': [base, { approach: 'b' }],
    'text-c': [base, { approach: 'c' }],
    'layout-one-line': [{ ...base, project: 'herdr-logi' }, { layout: 'one' }],
    'layout-two-line': [{ ...base, project: 'herdr-logi' }, { layout: 'two' }],
  };

  mkdirSync(join(MOCK_DIR, 'faces', '4x'), { recursive: true });
  const written = [];
  const write = (svg, out) => {
    const file = rasterise(svg, out);
    written.push(file);
    if (!file.endsWith('.png')) process.exitCode = 1;
  };
  const toSvg = (spec, options, size) => svgDoc(size, size, mockFace(spec, { ...options, scale: size / KEY }));

  for (const [name, [spec, options]] of Object.entries(faces)) {
    write(toSvg(spec, options, KEY), join(MOCK_DIR, 'faces', `${name}.png`));
    write(toSvg(spec, options, KEY * 4), join(MOCK_DIR, 'faces', '4x', `${name}.png`));
  }

  const cap = (name, spec, options) => ({
    spec,
    options,
    caption: [name, `tab ${spec.tab} · ${options.projectChars ?? 9}/${options.labelChars ?? 12} chars`],
  });
  const sheets = {
    'sheet-layout': [
      cap('one-line project', { ...base, project: 'herdr-logi' }, { layout: 'one' }),
      cap('two-line project', { ...base, project: 'herdr-logi' }, { layout: 'two' }),
      cap('one-line long', { ...base, project: 'herdr-logi-plugin-fork' }, { layout: 'one' }),
      cap('two-line long', { ...base, project: 'herdr-logi-plugin-fork' }, { layout: 'two' }),
    ],
    'sheet-scenarios': [
      ...Object.entries(scenarios).map(([name, spec]) => cap(name.replace('scenario-', ''), spec, {})),
      ...multi.map((spec) => cap(`multi ${spec.tab}`, spec, { placement: 'band' })),
    ],
    'sheet-color': ['text', 'band', 'tint'].flatMap((placement) =>
      multi.slice(0, 4).map((spec) => cap(`${placement}`, spec, { placement })),
    ),
    'sheet-text': ['a', 'b', 'c'].flatMap((approach) => [
      cap(`approach ${approach}`, base, { approach }),
      cap(`${approach} long`, scenarios['scenario-long-label'], { approach }),
      cap(`${approach} empty`, scenarios['scenario-empty'], { approach }),
    ]),
  };
  for (const [name, cells] of Object.entries(sheets)) {
    const columns = name === 'sheet-layout' ? 4 : name === 'sheet-color' ? 4 : 3;
    write(sheet(cells, 1, columns), join(MOCK_DIR, `${name}.png`));
    write(sheet(cells, 4, columns), join(MOCK_DIR, `${name}-4x.png`));
  }

  writeFileSync(join(MOCK_DIR, 'palette.json'), JSON.stringify(PALETTE, null, 2) + '\n');
  for (const file of written) console.log(`wrote ${file.replace(`${root}/`, '')}`);
}

if (process.argv.includes('--mockups')) {
  await mockups();
  process.exit(process.exitCode ?? 0);
}

const live = process.argv.includes('--live');
const deck = live ? await liveDeck() : DEMO_DECK;

mkdirSync(OUT_DIR, { recursive: true });
for (const [name, svg] of [
  ['keypad', keypad(deck)],
  ['tile-states', strip(STATES)],
]) {
  const written = rasterise(svg, join(OUT_DIR, `${name}.png`));
  console.log(`wrote ${written.replace(`${root}/`, '')}`);
}
