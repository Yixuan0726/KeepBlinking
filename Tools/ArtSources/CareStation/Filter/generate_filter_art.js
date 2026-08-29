/*
 * Care Station FILTER production-art generator.
 *
 * This is a deterministic, authored SVG pipeline. It does not read, crop,
 * trace, upscale, or otherwise transform the concept image. Every silhouette,
 * uneven contour, component, and animation frame below is explicitly drawn.
 *
 * Usage:
 *   set CODEX_NODE_MODULES=<bundled node_modules path>
 *   node generate_filter_art.js
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const nodeModules = process.env.CODEX_NODE_MODULES;
if (!nodeModules) throw new Error('CODEX_NODE_MODULES must point to a node_modules directory containing sharp.');
const sharp = require(path.join(nodeModules, 'sharp'));

const root = path.resolve(__dirname, '../../../..');
const sourceDir = __dirname;
const assetDir = path.join(root, 'Assets', 'KeepBlinking', 'Art', 'CareStation', 'Filter');
const catalogDir = path.join(root, 'Assets', 'KeepBlinking', 'Resources', 'CareStation', 'Filter');
const previewDir = path.join(root, 'Logs', 'CareStationFilterPreviews');

const C = {
  ink: '#34251F',
  warm: '#E8D8B6',
  blue: '#55727A',
  olive: '#77724A',
  brick: '#A64F3D',
  gold: '#C39A4A',
  mint: '#9BCDB6',
  metal: '#514A43',
  glass: '#B9D7C8',
  impurity: '#6B4B32',
  wood: '#866343',
  woodDark: '#664831',
};

const outline = `stroke="${C.ink}" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"`;
const inner = `stroke="${C.ink}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"`;

function svg(body) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">${body}</svg>`;
}

function scaledDocument(document, scale) {
  const openingEnd = document.indexOf('>') + 1;
  const transform = `<g transform="translate(512 1024) scale(${scale}) translate(-512 -1024)">`;
  return document.slice(0, openingEnd) + transform + document.slice(openingEnd, -6) + '</g></svg>';
}

function ellipse(cx, cy, rx, ry, fill, attrs = outline) {
  return `<ellipse cx="${cx}" cy="${cy}" rx="${rx}" ry="${ry}" fill="${fill}" ${attrs}/>`;
}

function rect(x, y, w, h, r, fill, attrs = outline) {
  return `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" ${attrs}/>`;
}

function pathShape(d, fill, attrs = outline) {
  return `<path d="${d}" fill="${fill}" ${attrs}/>`;
}

function line(d, color = C.ink, width = 7, extra = '') {
  return `<path d="${d}" fill="none" stroke="${color}" stroke-width="${width}" stroke-linecap="round" stroke-linejoin="round" ${extra}/>`;
}

function impurities(y, count, spread, seedOffset = 0) {
  let output = '';
  for (let i = 0; i < count; i++) {
    const offset = (i - (count - 1) / 2) * spread + ((i + seedOffset) % 2 === 0 ? -4 : 5);
    const radius = 11 + ((i + seedOffset) % 3) * 2;
    output += ellipse(512 + offset, y + ((i + seedOffset) % 2) * 3, radius, radius - 2, C.impurity, `stroke="${C.ink}" stroke-width="5"`);
  }
  return output;
}

function filterDisc(y, width, seedOffset = 0) {
  return [
    ellipse(512, y, width / 2, 34, C.warm),
    impurities(y - 9, Math.max(5, Math.round(width / 45)), 31, seedOffset),
    line(`M ${512 - width / 2 + 12} ${y + 3} C ${450 + seedOffset * 2} ${y + 18}, ${570 - seedOffset * 2} ${y + 18}, ${512 + width / 2 - 10} ${y + 1}`, C.ink, 6),
  ].join('');
}

function badge(level, y, frameColor) {
  const x = 512;
  const smallImpurities = [
    ellipse(x - 36, y - 22, 8, 7, C.impurity, `stroke="${C.ink}" stroke-width="4"`),
    ellipse(x - 8, y - 28, 9, 8, C.impurity, `stroke="${C.ink}" stroke-width="4"`),
    ellipse(x + 22, y - 21, 7, 7, C.impurity, `stroke="${C.ink}" stroke-width="4"`),
  ].join('');
  return [
    pathShape(`M ${x - 78} ${y - 75} Q ${x - 88} ${y - 48} ${x - 80} ${y + 52} Q ${x} ${y + 76} ${x + 79} ${y + 50} Q ${x + 88} ${y - 48} ${x + 76} ${y - 75} Z`, frameColor),
    pathShape(`M ${x - 61} ${y - 58} Q ${x} ${y - 68} ${x + 59} ${y - 55} L ${x + 55} ${y + 38} Q ${x} ${y + 52} ${x - 57} ${y + 36} Z`, C.warm, inner),
    smallImpurities,
    line(`M ${x - 47} ${y - 3} C ${x - 15} ${y + 5}, ${x + 18} ${y - 7}, ${x + 47} ${y + 1}`, C.ink, 7),
    line(`M ${x - 39} ${y + 21} C ${x - 23} ${y + 9}, ${x - 6} ${y + 33}, ${x + 10} ${y + 20} S ${x + 34} ${y + 29}, ${x + 45} ${y + 18}`, C.mint, 7),
    line(`M ${x - 34} ${y + 39} C ${x - 17} ${y + 27}, ${x + 2} ${y + 49}, ${x + 18} ${y + 37} S ${x + 34} ${y + 43}, ${x + 42} ${y + 34}`, C.mint, 6),
    `<metadata>Filter level ${level} function badge: impurities, filter layer, two mint flow ripples.</metadata>`,
  ].join('');
}

function level1Base() {
  return svg([
    // One deliberate, hard-edged cast shadow.
    pathShape('M 257 952 L 745 948 L 705 990 L 279 994 Z', C.metal, 'opacity="0.38"'),
    // Asymmetric wooden legs and salvaged cross brace.
    pathShape('M 278 716 L 367 721 L 350 974 L 244 980 Z', C.wood),
    pathShape('M 657 716 L 746 705 L 778 973 L 670 977 Z', C.woodDark),
    pathShape('M 238 813 L 774 798 L 764 891 L 250 905 Z', C.blue),
    line('M 270 835 L 730 822', C.ink, 6, 'stroke-dasharray="78 18 43 31"'),
    // Main glass chamber, intentionally a little crooked.
    pathShape('M 326 362 Q 508 337 694 367 L 680 730 Q 511 754 339 725 Z', C.glass, `${outline} fill-opacity="0.50"`),
    pathShape('M 300 341 Q 509 308 718 349 L 704 410 Q 511 386 312 414 Z', C.blue),
    pathShape('M 324 694 Q 510 723 685 696 L 702 750 Q 508 783 319 746 Z', C.metal),
    pathShape('M 282 730 Q 515 765 737 724 L 753 805 Q 506 844 270 809 Z', C.blue),
    // Top inlet with off-round lip.
    pathShape('M 442 332 L 452 275 Q 514 254 573 282 L 582 337 Z', C.metal),
    ellipse(512, 280, 66, 22, C.metal),
    // One filter, a safe patch, and restrained structural marks.
    filterDisc(566, 302, 1),
    pathShape('M 364 438 L 474 421 L 489 482 L 378 501 Z', C.warm, `stroke="${C.ink}" stroke-width="7" stroke-dasharray="24 8"`),
    line('M 349 397 L 356 691', C.ink, 6, 'stroke-dasharray="82 21 39 18"'),
    line('M 676 405 L 668 690', C.ink, 7, 'stroke-dasharray="51 17 87 24"'),
  ].join(''));
}

function level2Base() {
  return svg([
    pathShape('M 242 950 L 790 949 L 742 995 L 279 993 Z', C.metal, 'opacity="0.36"'),
    pathShape('M 246 760 Q 506 724 779 758 L 822 948 Q 514 988 205 948 Z', C.olive),
    pathShape('M 332 211 Q 510 181 696 221 L 696 752 Q 510 785 325 744 Z', C.glass, `${outline} fill-opacity="0.52"`),
    pathShape('M 284 198 Q 508 150 738 204 L 720 286 Q 510 252 302 286 Z', C.olive),
    pathShape('M 335 701 Q 506 737 696 702 L 722 780 Q 506 816 303 777 Z', C.olive),
    pathShape('M 443 193 L 450 130 Q 513 109 577 137 L 585 201 Z', C.metal),
    ellipse(515, 137, 70, 23, C.metal),
    filterDisc(419, 325, 2),
    filterDisc(590, 335, 4),
    line('M 354 272 L 352 700', C.ink, 7, 'stroke-dasharray="105 19 58 23"'),
    line('M 678 276 L 681 697', C.ink, 6, 'stroke-dasharray="66 16 118 25"'),
    pathShape('M 291 794 Q 510 818 748 790 L 765 909 Q 509 938 270 907 Z', C.olive),
  ].join(''));
}

function level3Base() {
  return svg([
    pathShape('M 160 955 L 845 952 L 790 999 L 207 997 Z', C.metal, 'opacity="0.34"'),
    pathShape('M 162 741 Q 510 696 852 744 L 892 956 Q 513 1005 128 953 Z', C.brick),
    pathShape('M 270 120 Q 509 73 753 128 L 747 750 Q 510 794 271 748 Z', C.glass, `${outline} fill-opacity="0.54"`),
    pathShape('M 216 101 Q 510 42 811 111 L 796 195 Q 512 145 233 192 Z', C.gold),
    // Brick-red cap without a crest or badge.
    pathShape('M 352 102 Q 367 30 503 24 Q 648 31 670 112 Z', C.brick),
    pathShape('M 459 40 L 466 8 Q 519 -2 573 17 L 577 48 Z', C.brick),
    pathShape('M 258 704 Q 510 759 760 705 L 800 790 Q 510 840 224 786 Z', C.brick),
    filterDisc(301, 390, 2),
    filterDisc(464, 402, 5),
    filterDisc(628, 397, 7),
    // The top rim is the single restrained dark-gold perimeter band.
    pathShape('M 146 874 Q 511 928 878 870 L 889 951 Q 508 1007 132 950 Z', C.brick),
    line('M 291 190 L 286 702', C.ink, 7, 'stroke-dasharray="116 22 61 20"'),
    line('M 731 190 L 735 699', C.ink, 6, 'stroke-dasharray="75 18 124 23"'),
    // Gauge body and automatic brush rail mounts live on the base.
    line('M 782 271 L 868 271', C.ink, 12),
    ellipse(901, 239, 76, 76, C.warm),
    ellipse(901, 239, 58, 58, C.warm, inner),
    line('M 852 370 L 852 650', C.gold, 14),
  ].join(''));
}

function crank(level) {
  const y = level === 1 ? 585 : 480;
  const x = level === 1 ? 700 : 696;
  const arm = level === 1 ? C.wood : C.metal;
  return svg([
    ellipse(x, y, 30, 33, C.metal),
    line(`M ${x + 14} ${y + 10} L ${x + 89} ${y + 75} L ${x + 138} ${y + 72}`, arm, 18),
    pathShape(`M ${x + 130} ${y + 50} Q ${x + 181} ${y + 42} ${x + 189} ${y + 73} Q ${x + 182} ${y + 101} ${x + 132} ${y + 92} Z`, level === 1 ? C.wood : C.metal),
  ].join(''));
}

function level3Brush() {
  return svg([
    line('M 850 391 L 850 628', C.gold, 12),
    pathShape('M 814 414 Q 850 393 886 418 L 877 512 Q 848 536 819 510 Z', C.gold),
    // Uneven bristles, kept broad and readable.
    line('M 817 454 L 793 438 M 817 472 L 787 469 M 818 490 L 793 508 M 881 449 L 903 432 M 880 470 L 909 466 M 879 492 L 901 511', C.ink, 7),
  ].join(''));
}

function level3Needle() {
  return svg([
    line('M 901 239 L 929 202', C.brick, 8),
    ellipse(901, 239, 9, 9, C.ink, `stroke="${C.ink}" stroke-width="3"`),
  ].join(''));
}

function flow(level, frame) {
  const phase = frame % 4;
  const wobble = [-7, 4, 8, -3][phase];
  const sway = [3, -5, 2, 6][phase];
  const mintStroke = `stroke="${C.mint}" stroke-linecap="round" stroke-linejoin="round"`;
  if (level === 1) {
    return svg([
      pathShape(`M 482 395 C ${478 + wobble} 451, ${507 + sway} 486, 494 534 C ${484 - sway} 584, ${531 + wobble} 627, 518 688 C 500 712, 480 713, 463 695 C 482 639, 463 593, 477 535 C 489 482, 465 444, 482 395 Z`, C.mint, `${mintStroke} stroke-width="5"`),
      line(`M 523 397 C ${538 + sway} 455, ${515 + wobble} 489, 532 537 C ${544 - sway} 584, ${521 + wobble} 628, 540 684`, '#C8E2D3', 7),
    ].join(''));
  }
  if (level === 2) {
    return svg([
      pathShape(`M 467 254 C ${465 + wobble} 315, ${489 + sway} 349, 477 393 C 463 442, 497 478, 482 544 C 471 595, 500 640, 487 706 L 537 705 C 548 643, 519 596, 535 542 C 548 489, 518 442, 539 390 C 551 345, 538 301, 544 254 Z`, C.mint, `${mintStroke} stroke-width="5"`),
      line(`M 505 258 C ${520 + sway} 321, ${497 + wobble} 352, 515 390 C ${529 - sway} 446, ${503 + wobble} 488, 520 541 C 536 599, 509 648, 523 699`, '#C8E2D3', 8),
    ].join(''));
  }
  return svg([
    pathShape(`M 455 153 C ${446 + wobble} 219, ${482 + sway} 254, 465 290 C 445 346, 486 391, 467 454 C 449 510, 488 551, 468 619 C 456 661, 482 700, 475 745 L 553 744 C 563 697, 536 659, 553 614 C 572 555, 532 510, 555 451 C 573 390, 535 345, 558 288 C 574 244, 553 207, 566 153 Z`, C.mint, `${mintStroke} stroke-width="5"`),
    line(`M 492 156 C ${512 + sway} 218, ${490 + wobble} 251, 510 291 C ${527 - sway} 348, ${499 + wobble} 390, 521 451 C 541 510, 506 553, 526 614 C 541 660, 516 699, 529 738`, '#C8E2D3', 9),
  ].join(''));
}

const assets = new Map();
// FILTER L1 is intentionally not emitted here. Its approved master and
// deterministic semantic extraction live under Approved/. Keeping this
// legacy L2/L3 generator from writing L1 prevents the retired procedural
// silhouette (including its crank) from replacing the approved runtime art.
assets.set('Filter_L2_Base', level2Base());
assets.set('Filter_L2_Crank', crank(2));
assets.set('Filter_L2_Badge', svg(badge(2, 846, C.olive)));
assets.set('Filter_L3_Base', level3Base());
assets.set('Filter_L3_Brush', level3Brush());
assets.set('Filter_L3_GaugeNeedle', level3Needle());
assets.set('Filter_L3_Badge', svg(badge(3, 835, C.gold)));
for (let level = 2; level <= 3; level++) {
  for (let frame = 0; frame < 4; frame++) {
    assets.set(`Filter_L${level}_Flow_${String(frame + 1).padStart(2, '0')}`, flow(level, frame));
  }
}
for (const [name, document] of [...assets.entries()]) {
  const level = Number(name.match(/Filter_L([123])_/)[1]);
  assets.set(name, scaledDocument(document, level === 1 ? 0.9 : level === 2 ? 0.9 : 0.94));
}

async function writeSvgAndPng(name, data) {
  const svgPath = path.join(sourceDir, `${name}.svg`);
  const pngPath = path.join(assetDir, `${name}.png`);
  fs.writeFileSync(svgPath, data, 'utf8');
  await sharp(Buffer.from(data)).png({compressionLevel: 9, palette: false}).toFile(pngPath);
  const metaPath = `${pngPath}.meta`;
  if (!fs.existsSync(metaPath)) fs.writeFileSync(metaPath, unityTextureMeta(name), 'utf8');
}

function unityTextureMeta(name) {
  const guid = spriteGuid(name);
  return `fileFormatVersion: 2
guid: ${guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 4
  spriteMeshType: 1
  alignment: 9
  spritePivot: {x: 0.5, y: 0}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: iPhone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
`;
}

function spriteGuid(name) {
  return crypto.createHash('md5').update(`KeepBlinking.Filter.${name}`).digest('hex');
}

function spriteReference(name) {
  return `{fileID: 21300000, guid: ${spriteGuid(name)}, type: 3}`;
}

function writeUnityCatalog() {
  fs.mkdirSync(catalogDir, {recursive: true});
  const folderMeta = `${catalogDir}.meta`;
  if (!fs.existsSync(folderMeta)) {
    fs.writeFileSync(folderMeta, `fileFormatVersion: 2
guid: 456a84d5fc4d465f8bb75a79a4bb25f8
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
`, 'utf8');
  }
  const levelBlock = level => `  - level: ${level}
    baseSprite: ${spriteReference(`Filter_L${level}_Base`)}
    flowFrames:
    - ${spriteReference(`Filter_L${level}_Flow_01`)}
    - ${spriteReference(`Filter_L${level}_Flow_02`)}
    - ${spriteReference(`Filter_L${level}_Flow_03`)}
    - ${spriteReference(`Filter_L${level}_Flow_04`)}
    crankSprite: ${level <= 2 ? spriteReference(`Filter_L${level}_Crank`) : '{fileID: 0}'}
    brushSprite: ${level === 3 ? spriteReference('Filter_L3_Brush') : '{fileID: 0}'}
    gaugeNeedleSprite: ${level === 3 ? spriteReference('Filter_L3_GaugeNeedle') : '{fileID: 0}'}
    badgeSprite: ${spriteReference(`Filter_L${level}_Badge`)}
    crankPivot: ${level === 1 ? '{x: 0.6652344, y: 0.38583985}' : level === 2 ? '{x: 0.6617187, y: 0.478125}' : '{x: 0, y: 0}'}
    gaugePivot: ${level === 3 ? '{x: 0.8570898, y: 0.7206055}' : '{x: 0, y: 0}'}
`;
  const catalog = `%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 6999122dfc3f49ce9f9d7f30f73e8da4, type: 3}
  m_Name: CareStationFilterArtCatalog
  m_EditorClassIdentifier:
  _levels:
${levelBlock(1)}${levelBlock(2)}${levelBlock(3)}`;
  fs.writeFileSync(path.join(catalogDir, 'CareStationFilterArtCatalog.asset'), catalog, 'utf8');
  const assetMeta = path.join(catalogDir, 'CareStationFilterArtCatalog.asset.meta');
  if (!fs.existsSync(assetMeta)) {
    fs.writeFileSync(assetMeta, `fileFormatVersion: 2
guid: bde1d34588854b2192563f37cd10d440
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
`, 'utf8');
  }
}

async function compositeLevel(level) {
  const names = [
    `Filter_L${level}_Base`,
    `Filter_L${level}_Flow_01`,
    ...(level <= 2 ? [`Filter_L${level}_Crank`] : ['Filter_L3_Brush', 'Filter_L3_GaugeNeedle']),
    `Filter_L${level}_Badge`,
  ];
  const composite = names.map(name => ({input: path.join(assetDir, `${name}.png`)}));
  await sharp({create: {width: 1024, height: 1024, channels: 4, background: {r: 0, g: 0, b: 0, alpha: 0}}})
    .composite(composite)
    .png({compressionLevel: 9})
    .toFile(path.join(previewDir, `Filter_L${level}_Transparent.png`));
}

async function buildReadabilitySheet() {
  const cards = [];
  for (let level = 1; level <= 3; level++) {
    const input = path.join(previewDir, `Filter_L${level}_Transparent.png`);
    const resized = await sharp(input).resize(256, 256).png().toBuffer();
    cards.push({input: resized, left: (level - 1) * 300 + 22, top: 30});
  }
  await sharp({create: {width: 900, height: 316, channels: 4, background: {r: 23, g: 35, b: 33, alpha: 1}}})
    .composite(cards)
    .png({compressionLevel: 9})
    .toFile(path.join(previewDir, 'Filter_25pct_Readability.png'));
}

async function main() {
  fs.mkdirSync(assetDir, {recursive: true});
  writeUnityCatalog();
  fs.mkdirSync(previewDir, {recursive: true});
  for (const [name, data] of assets) await writeSvgAndPng(name, data);
  for (let level = 1; level <= 3; level++) await compositeLevel(level);
  await buildReadabilitySheet();
  fs.writeFileSync(path.join(previewDir, 'asset-manifest.txt'), [...assets.keys()].join('\n') + '\n', 'utf8');
  process.stdout.write(`Generated ${assets.size} authored SVG/PNG layers.\n`);
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
