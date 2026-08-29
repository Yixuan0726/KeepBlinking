/*
 * Render the reference-led FILTER static review only.
 *
 * This script deliberately writes only to Logs/CareStationFilterConceptV2.
 * It never reads or modifies the 22 current runtime sprites, Catalog, importer,
 * or any Unity scene/prefab. The three SVG inputs are manually authored paths.
 */

const fs = require('fs');
const path = require('path');

const nodeModules = process.env.CODEX_NODE_MODULES
  || path.join(process.env.USERPROFILE || '', '.cache', 'codex-runtimes', 'codex-primary-runtime', 'dependencies', 'node', 'node_modules');
if (!fs.existsSync(nodeModules)) {
  throw new Error('The bundled node_modules directory was not found. Set CODEX_NODE_MODULES explicitly.');
}
const sharp = require(path.join(nodeModules, 'sharp'));

const sourceDir = __dirname;
const projectRoot = path.resolve(sourceDir, '../../../../..');
const outputDir = path.join(projectRoot, 'Logs', 'CareStationFilterConceptV2');
const levels = [1, 2, 3];

const PHONE_WIDTH = 1320;
const PHONE_HEIGHT = 2868;
const FILTER_SLOT = 284;
const FILTER_SLOT_LEFT = 215;
const FILTER_SLOT_TOP = 669;
const SIMULATED_SAFE_WIDTH = 1228;
const SIMULATED_SAFE_HEIGHT = 2653;

function sourcePath(level) {
  return path.join(sourceDir, `Filter_L${level}_Concept.svg`);
}

async function renderSvg(level, width = 1024, height = 1024) {
  return sharp(sourcePath(level), { density: 144 })
    .resize(width, height, { fit: 'contain' })
    .png()
    .toBuffer();
}

async function alphaBounds(pngBuffer) {
  const { data, info } = await sharp(pngBuffer)
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });
  let minX = info.width;
  let minY = info.height;
  let maxX = -1;
  let maxY = -1;
  for (let y = 0; y < info.height; y += 1) {
    for (let x = 0; x < info.width; x += 1) {
      if (data[(y * info.width + x) * 4 + 3] < 16) continue;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }
  if (maxX < 0) return { x: 0, y: 0, width: 0, height: 0 };
  return {
    x: minX,
    y: minY,
    width: maxX - minX + 1,
    height: maxY - minY + 1,
  };
}

function comparisonBackground() {
  return Buffer.from(`
  <svg xmlns="http://www.w3.org/2000/svg" width="3200" height="1400" viewBox="0 0 3200 1400">
    <rect width="3200" height="1400" fill="#F1E5CE"/>
    <path d="M0 1128 C520 1104 977 1119 1490 1127 C2101 1137 2614 1113 3200 1125 L3200 1400 L0 1400 Z" fill="#E4D2B3"/>
    <path d="M126 179 C532 151 1052 163 1486 169 C1991 177 2510 153 3064 177" fill="none" stroke="#D2B78B" stroke-width="6" stroke-linecap="round"/>
    <text x="1600" y="96" text-anchor="middle" fill="#34251F" font-family="Segoe Print, Comic Sans MS, sans-serif" font-size="54" font-weight="700">FILTER RESTORATION · STATIC VISUAL CHECK</text>
    <text x="1600" y="154" text-anchor="middle" fill="#6B5748" font-family="Segoe UI, sans-serif" font-size="27" letter-spacing="4">REFERENCE-LED HAND-AUTHORED PATH STUDY · NOT RUNTIME ART</text>

    <path d="M1018 624 C1068 607 1094 609 1134 623 M1111 603 L1144 624 L1114 647" fill="none" stroke="#6B5748" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M2043 624 C2093 607 2119 609 2159 623 M2136 603 L2169 624 L2139 647" fill="none" stroke="#6B5748" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>

    <path d="M172 1170 C373 1148 755 1151 934 1173 L925 1321 C721 1337 369 1337 179 1318 Z" fill="#55727A" stroke="#34251F" stroke-width="10" stroke-linejoin="round"/>
    <path d="M1196 1170 C1398 1148 1780 1151 1959 1173 L1950 1321 C1747 1337 1393 1337 1204 1318 Z" fill="#77724A" stroke="#34251F" stroke-width="10" stroke-linejoin="round"/>
    <path d="M2221 1170 C2424 1148 2805 1151 2984 1173 L2975 1321 C2771 1337 2418 1337 2229 1318 Z" fill="#A64F3D" stroke="#34251F" stroke-width="10" stroke-linejoin="round"/>
    <text x="550" y="1245" text-anchor="middle" fill="#F2F4EA" font-family="Segoe Print, Comic Sans MS, sans-serif" font-size="55" font-weight="700">FILTER LV.1</text>
    <text x="1575" y="1245" text-anchor="middle" fill="#F2F4EA" font-family="Segoe Print, Comic Sans MS, sans-serif" font-size="55" font-weight="700">FILTER LV.2</text>
    <text x="2600" y="1245" text-anchor="middle" fill="#F2F4EA" font-family="Segoe Print, Comic Sans MS, sans-serif" font-size="55" font-weight="700">FILTER LV.3</text>
    <text x="550" y="1290" text-anchor="middle" fill="#E8D8B6" font-family="Segoe UI, sans-serif" font-size="25" letter-spacing="2">PATCHED · MANUAL · ONE BED</text>
    <text x="1575" y="1290" text-anchor="middle" fill="#E8D8B6" font-family="Segoe UI, sans-serif" font-size="25" letter-spacing="2">RESTORED · STABLE · TWO BEDS</text>
    <text x="2600" y="1290" text-anchor="middle" fill="#E8D8B6" font-family="Segoe UI, sans-serif" font-size="25" letter-spacing="2">AUTOMATED · GAUGE + BRUSH · THREE BEDS</text>
  </svg>`);
}

function phonePanel(level) {
  const machineColor = level === 1 ? '#55727A' : level === 2 ? '#77724A' : '#A64F3D';
  return Buffer.from(`
  <svg xmlns="http://www.w3.org/2000/svg" width="${PHONE_WIDTH}" height="${PHONE_HEIGHT}" viewBox="0 0 ${PHONE_WIDTH} ${PHONE_HEIGHT}">
    <rect width="1320" height="2868" fill="#172321"/>
    <path d="M46 86 C283 67 1049 72 1274 92 L1272 2737 C1009 2757 290 2753 47 2736 Z" fill="#1B2927" stroke="#273B37" stroke-width="5"/>

    <!-- Runtime-safe-area top status band. -->
    <path d="M83 153 C305 136 1007 140 1238 155 L1231 337 C1001 351 301 351 87 334 Z" fill="#202928" stroke="#2E403D" stroke-width="5"/>
    <text x="138" y="227" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="38" font-weight="700">STATION 1</text>
    <path d="M507 182 C520 166 549 169 559 187 L557 245 C544 260 520 259 509 244 Z" fill="#9FCBB4"/>
    <text x="579" y="229" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="34" font-weight="700">36</text>
    <path d="M737 182 C750 166 779 169 789 187 L787 245 C774 260 750 259 739 244 Z" fill="#CBBF9B"/>
    <text x="809" y="229" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="34" font-weight="700">1</text>
    <text x="1031" y="204" fill="#CBBF9B" font-family="Segoe UI, sans-serif" font-size="24">STORAGE</text>
    <text x="1047" y="251" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="34" font-weight="700">37 / 48</text>

    <!-- Existing station proportions, intentionally subdued graybox context. -->
    <path d="M82 388 C346 364 982 367 1237 392 L1231 1858 C970 1876 344 1874 87 1855 Z" fill="#1E2D2A" stroke="#2B3C38" stroke-width="5"/>
    <path d="M188 524 C327 492 489 500 588 534 L570 1002 C428 1030 279 1017 178 982 Z" fill="#243532" opacity="0.6"/>
    <text x="257" y="1029" fill="#788B83" font-family="Segoe UI, sans-serif" font-size="26" letter-spacing="3">FILTER</text>
    <path d="M525 472 C639 448 782 455 861 488 L850 760 C743 779 614 772 532 746 Z" fill="#263633" stroke="#30443F" stroke-width="5"/>
    <path d="M581 514 C656 494 741 499 802 518 L798 690 C727 706 639 706 579 687 Z" fill="#2E403C"/>
    <text x="638" y="798" fill="#657A72" font-family="Segoe UI, sans-serif" font-size="25" letter-spacing="3">PRESS</text>
    <path d="M908 469 C980 445 1090 453 1154 489 L1149 799 C1071 821 978 815 915 788 Z" fill="#263633" stroke="#30443F" stroke-width="5"/>
    <path d="M939 583 C994 562 1081 568 1122 592 L1116 756 C1056 774 988 769 944 750 Z" fill="#48665D" opacity="0.56"/>
    <text x="989" y="846" fill="#657A72" font-family="Segoe UI, sans-serif" font-size="25" letter-spacing="3">TANK</text>

    <!-- Central Care Core remains secondary to the Filter for this scale review. -->
    <path d="M321 1118 C420 1005 853 994 997 1127 C882 1294 461 1314 321 1118 Z" fill="#314742" stroke="#486158" stroke-width="9"/>
    <path d="M443 1129 C551 1056 763 1050 876 1131 C774 1218 553 1222 443 1129 Z" fill="#56736A" opacity="0.55"/>
    <text x="523" y="1349" fill="#758A82" font-family="Segoe UI, sans-serif" font-size="27" letter-spacing="4">CARE CORE</text>

    <!-- Rail, storage and cart are low-contrast placeholders, not new art. -->
    <path d="M164 1471 C420 1418 871 1420 1141 1481" fill="none" stroke="#2E4540" stroke-width="15"/>
    <path d="M168 1517 C428 1468 875 1467 1142 1528" fill="none" stroke="#243A35" stroke-width="8"/>
    <path d="M134 1542 C244 1507 381 1512 464 1549 L456 1742 C340 1762 233 1754 145 1724 Z" fill="#2C403B" stroke="#3A514A" stroke-width="6"/>
    <path d="M176 1607 L420 1587 M177 1666 L420 1647" fill="none" stroke="#657A72" stroke-width="7" opacity="0.5"/>
    <text x="202" y="1792" fill="#657A72" font-family="Segoe UI, sans-serif" font-size="24" letter-spacing="3">STORAGE</text>
    <path d="M904 1556 C981 1539 1083 1546 1148 1572 L1138 1709 C1055 1725 970 1720 910 1698 Z" fill="#304640" stroke="#3D554E" stroke-width="6"/>
    <path d="M934 1710 C946 1688 979 1689 989 1712 C996 1734 972 1750 952 1741 C936 1735 929 1723 934 1710 Z M1073 1710 C1085 1688 1118 1689 1128 1712 C1135 1734 1111 1750 1091 1741 C1075 1735 1068 1723 1073 1710 Z" fill="#536A62"/>
    <text x="981" y="1792" fill="#657A72" font-family="Segoe UI, sans-serif" font-size="24" letter-spacing="3">CART</text>

    <!-- Routine and navigation retain the current hierarchy for genuine scale judgment. -->
    <path d="M93 1923 C350 1898 966 1901 1225 1927 L1218 2394 C957 2412 352 2411 98 2390 Z" fill="#202928" stroke="#2D403B" stroke-width="6"/>
    <text x="660" y="1999" text-anchor="middle" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="38" font-weight="700">TODAY'S EYE CARE</text>
    <path d="M175 2080 C267 2059 373 2064 450 2085 L445 2228 C353 2246 258 2241 183 2220 Z M502 2080 C594 2059 700 2064 777 2085 L772 2228 C680 2246 585 2241 510 2220 Z M829 2080 C921 2059 1027 2064 1104 2085 L1099 2228 C1007 2246 912 2241 837 2220 Z" fill="#2A3A37"/>
    <path d="M208 2266 C420 2248 901 2251 1112 2270 L1107 2357 C897 2372 422 2370 213 2355 Z" fill="#56756A"/>
    <text x="660" y="2325" text-anchor="middle" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="32" font-weight="700">START CARE</text>

    <path d="M91 2461 C411 2436 906 2438 1227 2464 L1220 2731 C908 2747 405 2746 96 2728 Z" fill="#192523" stroke="#2D403B" stroke-width="5"/>
    <path d="M117 2501 C237 2484 355 2488 443 2511 L439 2695 C326 2710 219 2707 126 2688 Z" fill="#46675D"/>
    <text x="277" y="2633" text-anchor="middle" fill="#F2F4EA" font-family="Segoe UI, sans-serif" font-size="26" font-weight="700">STATION</text>
    <text x="659" y="2633" text-anchor="middle" fill="#788B83" font-family="Segoe UI, sans-serif" font-size="26" font-weight="700">UPGRADES</text>
    <text x="1041" y="2633" text-anchor="middle" fill="#788B83" font-family="Segoe UI, sans-serif" font-size="26" font-weight="700">REPORTS</text>

    <path d="M100 400 C266 381 469 386 602 415 L595 468 C452 446 273 444 107 465 Z" fill="${machineColor}" opacity="0.23"/>
    <text x="119" y="442" fill="#F2F4EA" font-family="Segoe Print, Comic Sans MS, sans-serif" font-size="30" font-weight="700">FILTER LEVEL ${level}</text>
  </svg>`);
}

async function buildComparison(rendered) {
  const composites = [];
  const positions = [100, 1125, 2150];
  for (let index = 0; index < rendered.length; index += 1) {
    composites.push({
      input: await sharp(rendered[index]).resize(900, 900, { fit: 'contain' }).png().toBuffer(),
      left: positions[index],
      top: 205,
    });
  }
  await sharp(comparisonBackground())
    .composite(composites)
    .png()
    .toFile(path.join(outputDir, 'Filter_L1_L2_L3_Comparison_v2.png'));
}

async function buildPhoneTriptych(rendered) {
  const canvas = sharp({
    create: {
      width: PHONE_WIDTH * 3,
      height: PHONE_HEIGHT,
      channels: 4,
      background: '#101A18',
    },
  });
  const composites = [];
  for (let index = 0; index < rendered.length; index += 1) {
    const panelOffset = index * PHONE_WIDTH;
    composites.push({ input: phonePanel(index + 1), left: panelOffset, top: 0 });
    composites.push({
      input: await sharp(rendered[index]).resize(FILTER_SLOT, FILTER_SLOT, { fit: 'contain' }).png().toBuffer(),
      left: panelOffset + FILTER_SLOT_LEFT,
      top: FILTER_SLOT_TOP,
    });
  }
  const separator = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${PHONE_WIDTH * 3}" height="${PHONE_HEIGHT}"><path d="M1320 0 L1320 2868 M2640 0 L2640 2868" stroke="#D3BE93" stroke-width="8" opacity="0.7"/></svg>`);
  composites.push({ input: separator, left: 0, top: 0 });
  await canvas
    .composite(composites)
    .png()
    .toFile(path.join(outputDir, 'Filter_Station_Mobile_Triptych_v2.png'));
}

async function main() {
  fs.mkdirSync(outputDir, { recursive: true });
  const rendered = [];
  const metrics = [];
  for (const level of levels) {
    const png = await renderSvg(level);
    rendered.push(png);
    const bounds = await alphaBounds(png);
    const scale = FILTER_SLOT / 1024;
    metrics.push({
      level,
      sourceCanvas: { width: 1024, height: 1024 },
      alphaBounds: bounds,
      sourceOccupancyPercent: {
        width: Number((bounds.width / 10.24).toFixed(1)),
        height: Number((bounds.height / 10.24).toFixed(1)),
      },
      phonePreview: {
        screen: { width: PHONE_WIDTH, height: PHONE_HEIGHT },
        simulatedSafeArea: { width: SIMULATED_SAFE_WIDTH, height: SIMULATED_SAFE_HEIGHT },
        artSlot: { width: FILTER_SLOT, height: FILTER_SLOT, left: FILTER_SLOT_LEFT, top: FILTER_SLOT_TOP },
        visibleBounds: {
          width: Math.round(bounds.width * scale),
          height: Math.round(bounds.height * scale),
        },
        visibleHeightOfScreenPercent: Number(((bounds.height * scale / PHONE_HEIGHT) * 100).toFixed(1)),
        visibleHeightOfSafeAreaPercent: Number(((bounds.height * scale / SIMULATED_SAFE_HEIGHT) * 100).toFixed(1)),
      },
    });
  }
  await buildComparison(rendered);
  await buildPhoneTriptych(rendered);
  fs.writeFileSync(
    path.join(outputDir, 'Filter_ConceptV2_Metrics.json'),
    `${JSON.stringify({ generatedUtc: new Date().toISOString(), metrics }, null, 2)}\n`,
    'utf8',
  );
  process.stdout.write(`${JSON.stringify(metrics, null, 2)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});
