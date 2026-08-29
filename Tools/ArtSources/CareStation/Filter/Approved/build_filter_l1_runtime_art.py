#!/usr/bin/env python3
"""Build the approved FILTER Level 1 runtime layers from the approved master.

This is a deterministic semantic extraction/matting pipeline.  It never invents
another device silhouette and it never reads the earlier procedural Filter art.
All authored masks are in the coordinate system of the approved 1254 px master.
The exported mobile-runtime sprites share one 560 x 840 canvas and therefore
one Unity bottom-centre pivot.  This is intentionally a 2x asset for the final
roughly 280 x 420 physical-pixel presentation; it is not a thumbnail of the
paper-backed master.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[5]
SOURCE = Path(__file__).with_name("Filter_Level1_Final_Reference.png")
OUT = ROOT / "Assets" / "KeepBlinking" / "Art" / "CareStation" / "Filter"
QA = ROOT / "Logs" / "CareStationFilterL1"
OUTPUT_WIDTH = 560
OUTPUT_HEIGHT = 840
# A shared portrait crop removes the unused paper while preserving every part
# of the approved silhouette and the asymmetrical floor-mounted supports.
SOURCE_CROP = (209, 0, 1045, 1254)


def mask_image() -> Image.Image:
    return Image.new("L", SOURCE_IMAGE.size, 0)


def polygon(mask: Image.Image, points, fill=255):
    ImageDraw.Draw(mask).polygon(points, fill=fill)


def ellipse(mask: Image.Image, box, fill=255):
    ImageDraw.Draw(mask).ellipse(box, fill=fill)


def line(mask: Image.Image, points, width, fill=255):
    ImageDraw.Draw(mask).line(points, fill=fill, width=width, joint="curve")


def subtract(mask: Image.Image, cut: Image.Image) -> Image.Image:
    a = np.asarray(mask, dtype=np.int16)
    b = np.asarray(cut, dtype=np.int16)
    return Image.fromarray(np.clip(a - b, 0, 255).astype(np.uint8), "L")


def union(*masks: Image.Image) -> Image.Image:
    result = np.zeros((SOURCE_IMAGE.height, SOURCE_IMAGE.width), dtype=np.uint8)
    for mask in masks:
        result = np.maximum(result, np.asarray(mask, dtype=np.uint8))
    return Image.fromarray(result, "L")


def smoothstep(edge0, edge1, value):
    t = np.clip((value - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def background_field(height: int, width: int) -> np.ndarray:
    # Measured from blank areas of the approved master.  The paper is subtly
    # brighter at the top, so a local vertical field is safer than one key.
    top = np.array([244.0, 210.0, 181.0], dtype=np.float32)
    bottom = np.array([242.0, 208.5, 179.5], dtype=np.float32)
    y = np.linspace(0.0, 1.0, height, dtype=np.float32)[:, None, None]
    field = top[None, None, :] * (1.0 - y) + bottom[None, None, :] * y
    return np.broadcast_to(field, (height, width, 3)).copy()


def source_separation() -> np.ndarray:
    delta = SOURCE_RGB.astype(np.float32) - BACKGROUND
    distance = np.sqrt(np.sum(delta * delta, axis=2))
    # Blank paper lives below roughly 8 RGB-distance units.  The 16..32 ramp
    # deliberately discards the paper-coloured antialias fringe; a new clean
    # one-pixel antialias is produced by the final mobile-runtime resize.
    return smoothstep(16.0, 32.0, distance)


def decontaminate(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Remove the warm paper contribution from partially covered edge pixels."""
    a = np.clip(alpha[..., None], 0.0, 1.0)
    recovered = (rgb.astype(np.float32) - (1.0 - a) * BACKGROUND) / np.maximum(a, 0.08)
    recovered = np.clip(recovered, 0.0, 255.0)
    # Interior colour is already authoritative; only decontaminate soft edges.
    return np.where(a < 0.985, recovered, rgb.astype(np.float32))


def extrude_transparent_rgb(rgb: np.ndarray, alpha: np.ndarray, pixels=5) -> np.ndarray:
    """Bleed foreground RGB under transparent pixels for bilinear sampling."""
    out = rgb.copy()
    valid = alpha > 0.01
    for _ in range(pixels):
        sums = np.zeros_like(out, dtype=np.float32)
        counts = np.zeros(valid.shape, dtype=np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1),
                       (-1, -1), (-1, 1), (1, -1), (1, 1)):
            shifted_valid = np.roll(np.roll(valid, dy, axis=0), dx, axis=1)
            shifted_rgb = np.roll(np.roll(out, dy, axis=0), dx, axis=1)
            sums += shifted_rgb * shifted_valid[..., None]
            counts += shifted_valid
        grow = (~valid) & (counts > 0)
        out[grow] = sums[grow] / counts[grow, None]
        valid[grow] = True
    out[~valid] = 0.0
    return out


def pull_clean_edge_colours(rgb: np.ndarray, alpha: np.ndarray, pixels=6) -> np.ndarray:
    """Replace matted soft-edge RGB with nearby authoritative foreground RGB."""
    out = rgb.astype(np.float32).copy()
    valid = alpha >= 0.94
    wanted = (alpha > 0.001) & (~valid)
    for _ in range(pixels):
        sums = np.zeros_like(out, dtype=np.float32)
        counts = np.zeros(valid.shape, dtype=np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1),
                       (-1, -1), (-1, 1), (1, -1), (1, 1)):
            shifted_valid = np.roll(np.roll(valid, dy, axis=0), dx, axis=1)
            shifted_rgb = np.roll(np.roll(out, dy, axis=0), dx, axis=1)
            sums += shifted_rgb * shifted_valid[..., None]
            counts += shifted_valid
        fill = wanted & (~valid) & (counts > 0)
        out[fill] = sums[fill] / counts[fill, None]
        valid[fill] = True
    return out


def resize_straight_rgba(rgb: np.ndarray, alpha: np.ndarray) -> Image.Image:
    # Resize premultiplied colour, then return to straight alpha.  This avoids
    # dark/cream fringes during the portrait runtime reduction.
    x0, y0, x1, y1 = SOURCE_CROP
    a = np.clip(alpha[y0:y1, x0:x1], 0.0, 1.0)
    rgb = rgb[y0:y1, x0:x1]
    premul = np.clip(rgb.astype(np.float32) * a[..., None], 0.0, 255.0)

    # Pillow's 8-bit Lanczos path can ring each premultiplied colour channel
    # differently.  Dividing that result by a tiny alpha produces coloured
    # stripes.  Resize all four planes in floating-point mode instead.
    p_channels = []
    for channel in range(3):
        plane = Image.fromarray(premul[:, :, channel], "F").resize(
            (OUTPUT_WIDTH, OUTPUT_HEIGHT), Image.Resampling.LANCZOS
        )
        p_channels.append(np.asarray(plane, dtype=np.float32))
    p = np.stack(p_channels, axis=2)
    alpha_plane = Image.fromarray(a.astype(np.float32), "F").resize(
        (OUTPUT_WIDTH, OUTPUT_HEIGHT), Image.Resampling.LANCZOS
    )
    ao = np.clip(np.asarray(alpha_plane, dtype=np.float32), 0.0, 1.0)
    # Sub-2% coverage is resampling ringing, not useful visual information.
    ao[ao < 0.02] = 0.0
    p = np.clip(p, 0.0, 255.0 * ao[..., None])
    straight = np.zeros_like(p)
    nonzero = ao > 0.0
    straight[nonzero] = p[nonzero] / ao[nonzero, None]
    straight = np.clip(straight, 0.0, 255.0)
    straight = extrude_transparent_rgb(straight, ao, pixels=5)
    alpha_u8 = np.round(ao * 255.0).astype(np.uint8)
    rgba = np.dstack((straight.astype(np.uint8), alpha_u8))
    return Image.fromarray(rgba, "RGBA")


def clean_for_mobile(image: Image.Image, strength: float) -> Image.Image:
    """Suppress sub-display texture while retaining the approved large marks.

    The cleanup works after the authoritative matte and portrait crop.  A
    median colour pass removes one- and two-pixel scratches, while a restrained
    unsharp pass keeps the dark-brown outer contour readable after Unity's mip
    selection.  Alpha is never blurred into the background.
    """
    strength = float(np.clip(strength, 0.0, 1.0))
    rgba = np.asarray(image, dtype=np.uint8).copy()
    alpha = rgba[:, :, 3].copy()
    rgb_image = Image.fromarray(rgba[:, :, :3], "RGB")
    median_size = 5 if strength >= 0.55 else 3
    smoothed = np.asarray(rgb_image.filter(ImageFilter.MedianFilter(median_size)), dtype=np.float32)
    original = rgba[:, :, :3].astype(np.float32)
    mixed = original * (1.0 - strength * 0.62) + smoothed * (strength * 0.62)

    # Palette cleanup removes tiny near-identical texture colours without
    # flattening the approved hand-painted planes into geometric gradients.
    palette_source = Image.fromarray(np.clip(mixed, 0, 255).astype(np.uint8), "RGB")
    quantized = np.asarray(palette_source.quantize(
        colors=72 if strength >= 0.55 else 112,
        method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE).convert("RGB"), dtype=np.float32)
    mixed = mixed * 0.72 + quantized * 0.28

    # Remove coloured extraction debris only on antialiased edge pixels.
    partial = (alpha > 0) & (alpha < 245)
    purple = ((mixed[:, :, 2] - mixed[:, :, 0] > 16) &
              (mixed[:, :, 2] - mixed[:, :, 1] > 10))
    mixed[partial & purple] = smoothed[partial & purple]
    alpha[alpha < 14] = 0
    alpha[alpha > 246] = 255

    cleaned = Image.fromarray(np.dstack((np.clip(mixed, 0, 255).astype(np.uint8), alpha)), "RGBA")
    sharpened = cleaned.filter(ImageFilter.UnsharpMask(radius=0.8, percent=58, threshold=5))
    sharpened.putalpha(Image.fromarray(alpha, "L"))
    return sharpened


def make_source_layer(mask: Image.Image, *, paper_reject=True, opacity=1.0,
                      unmatte=False, cleanup=0.0) -> Image.Image:
    semantic = np.asarray(mask, dtype=np.float32) / 255.0
    if paper_reject:
        semantic *= SEPARATION
        # The flattened master has a bright peach contact fringe immediately
        # outside dark ink (for example RGB 255/235/212).  It is much brighter
        # than the approved brass and less neutral than glass highlights.  Only
        # reject this hue within the outer 10 px of the authored semantic mask.
        interior = np.asarray(mask.filter(ImageFilter.MinFilter(21)), dtype=np.float32) / 255.0
        edge_band = (np.asarray(mask, dtype=np.uint8) > 0) & (interior < 0.90)
        src = SOURCE_RGB.astype(np.int16)
        warm_paper = ((src[:, :, 0] > 205) & (src[:, :, 1] > 175) &
                      (src[:, :, 2] > 145) &
                      (src[:, :, 0] - src[:, :, 2] > 8) &
                      (src[:, :, 1] - src[:, :, 2] > 4))
        semantic[edge_band & warm_paper] = 0.0
    semantic = np.clip(semantic * opacity, 0.0, 1.0)
    rgb = SOURCE_RGB.astype(np.float32)
    if unmatte:
        rgb = decontaminate(rgb, semantic)
    else:
        # Opaque painted parts already contain authoritative colour.  Pull the
        # nearest clean foreground colour into their soft edge instead of
        # algebraically unmatting tiny alpha values (which can create rainbow
        # clipping on dark backgrounds).
        rgb = pull_clean_edge_colours(rgb, semantic)
    result = resize_straight_rgba(rgb, semantic)
    return clean_for_mobile(result, cleanup) if cleanup > 0.0 else result


def save(name: str, image: Image.Image):
    path = OUT / name
    image.save(path, format="PNG", optimize=False, compress_level=9)
    return path


def base_mask() -> Image.Image:
    m = mask_image()
    # Inlet and battered blue-grey upper cover.
    ellipse(m, (544, 18, 697, 81))
    polygon(m, [(548, 48), (550, 99), (570, 118), (668, 120),
                (692, 100), (692, 46)])
    polygon(m, [(397, 90), (461, 68), (547, 64), (548, 105),
                (574, 130), (674, 133), (709, 109), (713, 67),
                (790, 79), (845, 106), (861, 139), (860, 196),
                (838, 218), (785, 231), (462, 234), (402, 218),
                (374, 197), (375, 139)])
    # Mismatched upper supports.
    polygon(m, [(389, 199), (451, 210), (451, 468), (438, 540),
                (416, 575), (372, 565), (385, 468)])
    polygon(m, [(791, 199), (848, 205), (850, 468), (869, 561),
                (825, 583), (797, 548), (783, 470)])
    # Load-bearing crossbeam.
    polygon(m, [(329, 559), (874, 557), (900, 569), (908, 588),
                (906, 647), (880, 660), (336, 661), (318, 646),
                (321, 580)])
    # Left repaired wooden post and direct-to-ground foot plate.
    polygon(m, [(334, 643), (431, 646), (424, 843), (414, 1127),
                (399, 1155), (294, 1153), (305, 842), (319, 682)])
    polygon(m, [(283, 1128), (427, 1129), (439, 1201), (411, 1215),
                (257, 1208), (260, 1172)])
    # Required diagonal brace, patched metal post, and right foot plate.
    polygon(m, [(644, 640), (685, 638), (821, 773), (806, 827),
                (775, 807), (630, 671)])
    polygon(m, [(806, 641), (888, 645), (913, 836), (899, 1128),
                (884, 1155), (822, 1150), (812, 933), (804, 834)])
    polygon(m, [(806, 1135), (933, 1136), (951, 1207), (927, 1217),
                (791, 1211), (789, 1170)])
    # The taped glass crack is an essential part of the approved silhouette.
    polygon(m, [(435, 270), (502, 241), (552, 262), (564, 311),
                (516, 344), (459, 330), (424, 305)])
    line(m, [(445, 217), (448, 253), (466, 277)], 4)
    line(m, [(486, 330), (503, 366), (527, 389)], 3)
    # Glass edge/highlight strokes only; never retain the paper-filled pane.
    line(m, [(444, 228), (442, 451)], 4)
    line(m, [(781, 227), (780, 459)], 4)
    line(m, [(459, 362), (469, 354), (495, 350)], 3)
    # Badge is a separately addressable sprite.
    badge_cut = mask_image()
    ellipse(badge_cut, (562, 584, 660, 686))
    # The master is flattened over paper and carries a 2-3 px warm contact
    # shadow around the silhouette.  Contract only the structural matte so the
    # original dark ink remains while the paper halo is excluded.
    structural = subtract(m, badge_cut).filter(ImageFilter.MinFilter(7))
    glass_details = mask_image()
    line(glass_details, [(445, 217), (448, 253), (466, 277)], 3)
    line(glass_details, [(486, 330), (503, 366), (527, 389)], 3)
    line(glass_details, [(444, 228), (442, 451)], 3)
    line(glass_details, [(781, 227), (780, 459)], 3)
    line(glass_details, [(459, 362), (469, 354), (495, 350)], 3)
    return union(structural, glass_details)


def raw_liquid_mask() -> Image.Image:
    m = mask_image()
    ellipse(m, (441, 321, 781, 399))
    polygon(m, [(442, 360), (780, 360), (779, 471), (765, 485),
                (461, 485), (442, 469)])
    return m.filter(ImageFilter.GaussianBlur(0.8))


def particles_mask() -> Image.Image:
    m = mask_image()
    for box in ((486, 429, 495, 439), (612, 407, 620, 416),
                (681, 424, 690, 434), (751, 430, 760, 441),
                (487, 456, 495, 465), (618, 454, 627, 464),
                (707, 462, 715, 470)):
        ellipse(m, box, fill=220)
    return m.filter(ImageFilter.GaussianBlur(0.7))


def cartridge_mask() -> Image.Image:
    m = mask_image()
    ellipse(m, (440, 460, 782, 536))
    polygon(m, [(441, 479), (781, 479), (777, 510), (748, 529),
                (480, 529), (446, 511)])
    return m.filter(ImageFilter.GaussianBlur(0.45))


def funnel_pipe_mask() -> Image.Image:
    m = mask_image()
    polygon(m, [(451, 499), (778, 499), (754, 535), (650, 584),
                (631, 607), (591, 607), (574, 582), (474, 537)])
    polygon(m, [(592, 603), (629, 603), (624, 767), (599, 769)])
    return m.filter(ImageFilter.GaussianBlur(0.45))


def badge_mask() -> Image.Image:
    m = mask_image()
    ellipse(m, (563, 584, 660, 686))
    return m.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.4))


def bottle_outer_and_inner():
    outer = mask_image()
    polygon(outer, [(569, 887), (651, 887), (653, 927), (670, 945),
                    (686, 980), (688, 1138), (671, 1172), (647, 1187),
                    (572, 1187), (548, 1173), (538, 1140), (538, 981),
                    (552, 946), (568, 929)])
    inner = mask_image()
    polygon(inner, [(579, 922), (641, 922), (643, 945), (657, 958),
                    (670, 988), (670, 1134), (654, 1157), (568, 1157),
                    (554, 1137), (554, 988), (565, 958), (578, 945)])
    return outer, inner


def bottle_mask() -> Image.Image:
    outer, inner = bottle_outer_and_inner()
    ring = subtract(outer, inner).filter(ImageFilter.MinFilter(5))
    # Preserve the approved glass highlights without baking the warm paper.
    highlights = mask_image()
    line(highlights, [(574, 953), (561, 989), (561, 1103), (574, 1143)], 7, 185)
    line(highlights, [(650, 952), (664, 989), (664, 1116)], 4, 130)
    ellipse(highlights, (568, 889, 653, 937), 210)
    return union(ring, highlights).filter(ImageFilter.GaussianBlur(0.55))


def bottle_fill_mask() -> Image.Image:
    m = mask_image()
    ellipse(m, (557, 998, 669, 1038))
    polygon(m, [(557, 1017), (669, 1017), (668, 1139), (654, 1160),
                (573, 1160), (558, 1138)])
    return m.filter(ImageFilter.GaussianBlur(0.7))


def flow_base_mask() -> Image.Image:
    # Select the exact approved mint stream, then include its pale inner core.
    arr = SOURCE_RGB.astype(np.int16)
    green = ((arr[:, :, 1] - arr[:, :, 0] > 5) &
             (arr[:, :, 1] - arr[:, :, 2] > 4) &
             (arr[:, :, 1] > 118))
    roi = np.zeros(green.shape, dtype=bool)
    roi[756:1062, 603:617] = True
    seed = Image.fromarray((green & roi).astype(np.uint8) * 255, "L")
    # A one-pixel dilation captures the white highlight that belongs to the flow,
    # but the narrow semantic ROI prevents paper from entering the mask.
    expanded = seed.filter(ImageFilter.MaxFilter(3))
    narrow = mask_image()
    # Keep the stream fine inside the bottle as well as in open air.  A broad
    # chroma selection would incorrectly turn the bottle fill into a teal bar.
    line(narrow, [(609, 760), (610, 911), (610, 1058)], 6)
    return Image.fromarray(np.minimum(np.asarray(expanded), np.asarray(narrow)), "L")


def flow_frame(base: Image.Image, index: int) -> Image.Image:
    a = np.asarray(base, dtype=np.float32) / 255.0
    y = np.arange(a.shape[0], dtype=np.float32)[:, None]
    # Low-amplitude travelling opacity variation; the stream stays continuous.
    modulation = 0.88 + 0.12 * np.sin((y / 23.0) + index * 1.15)
    a = np.clip(a * modulation, 0.0, 1.0)
    rgb = SOURCE_RGB.astype(np.float32)
    return resize_straight_rgba(decontaminate(rgb, a), a)


def alpha_bbox(image: Image.Image):
    alpha = np.asarray(image.getchannel("A"))
    ys, xs = np.where(alpha > 2)
    if len(xs) == 0:
        return None
    return [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]


def composite(names, background=(18, 43, 42, 255)) -> Image.Image:
    canvas = Image.new("RGBA", (OUTPUT_WIDTH, OUTPUT_HEIGHT), background)
    for name in names:
        layer = Image.open(OUT / name).convert("RGBA")
        canvas.alpha_composite(layer)
    return canvas


def crop_fill(image: Image.Image, fraction: float) -> Image.Image:
    result = image.copy()
    a = np.asarray(result.getchannel("A")).copy()
    ys, _ = np.where(a > 2)
    if len(ys):
        top, bottom = ys.min(), ys.max()
        cut = int(bottom - (bottom - top + 1) * np.clip(fraction, 0.0, 1.0))
        a[:cut, :] = 0
    result.putalpha(Image.fromarray(a, "L"))
    return result


def qa_sheet(layers: dict[str, Image.Image]):
    idle_order = ["FilterL1_RawLiquid.png", "FilterL1_RawParticles.png",
                  "FilterL1_FilterCartridge.png", "FilterL1_FunnelAndPipe.png",
                  "FilterL1_Base.png", "FilterL1_Badge.png", "FilterL1_Bottle.png"]
    filtering_order = ["FilterL1_RawLiquid.png", "FilterL1_RawParticles.png",
                       "FilterL1_FilterCartridge.png", "FilterL1_FunnelAndPipe.png",
                       "FilterL1_Base.png", "FilterL1_Badge.png"]
    idle = composite(idle_order)
    filtering = composite(filtering_order)
    filtering.alpha_composite(crop_fill(layers["FilterL1_BottleFill.png"], 0.55))
    filtering.alpha_composite(layers["FilterL1_CleanFlow_02.png"])
    filtering.alpha_composite(layers["FilterL1_Bottle.png"])
    complete = composite(filtering_order)
    complete.alpha_composite(layers["FilterL1_BottleFill.png"])
    complete.alpha_composite(layers["FilterL1_Bottle.png"])
    idle.save(QA / "FilterL1_QA_Idle_DarkTeal.png")
    filtering.save(QA / "FilterL1_QA_Filtering_DarkTeal.png")
    complete.save(QA / "FilterL1_QA_BottleComplete_DarkTeal.png")

    # One review image, large enough to inspect at a glance.
    margin, label_h = 36, 70
    sheet = Image.new("RGBA", (OUTPUT_WIDTH * 3 + margin * 4,
                                OUTPUT_HEIGHT + label_h + margin * 2),
                      (13, 31, 31, 255))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=28)
    for i, (label, state) in enumerate((("IDLE", idle), ("FILTERING", filtering),
                                        ("BOTTLE COMPLETE", complete))):
        x = margin + i * (OUTPUT_WIDTH + margin)
        sheet.alpha_composite(state, (x, margin + label_h))
        draw.text((x + 12, margin + 15), label, fill=(232, 216, 182, 255), font=font)
    sheet.save(QA / "FilterL1_QA_StateComparison.png")

    # Transparent composite on a checkerboard for alpha inspection.
    transparent = Image.new("RGBA", (OUTPUT_WIDTH, OUTPUT_HEIGHT), (0, 0, 0, 0))
    for name in idle_order:
        transparent.alpha_composite(layers[name])
    checker = Image.new("RGBA", (OUTPUT_WIDTH, OUTPUT_HEIGHT), (48, 62, 62, 255))
    d = ImageDraw.Draw(checker)
    tile = 32
    for yy in range(0, OUTPUT_HEIGHT, tile):
        for xx in range(0, OUTPUT_WIDTH, tile):
            if ((xx // tile) + (yy // tile)) % 2:
                d.rectangle((xx, yy, xx + tile - 1, yy + tile - 1), fill=(82, 96, 92, 255))
    checker.alpha_composite(transparent)
    checker.save(QA / "FilterL1_QA_TransparentComposite.png")


def metrics(layers: dict[str, Image.Image], source_sha: str):
    report = {
        "source": str(SOURCE.relative_to(ROOT)),
        "source_sha256": source_sha,
        "source_size": list(SOURCE_IMAGE.size),
        "output_size": [OUTPUT_WIDTH, OUTPUT_HEIGHT],
        "source_crop": list(SOURCE_CROP),
        "intended_runtime_display": [280, 420],
        "pivot": "Bottom Center (shared full canvas)",
        "layers": {},
    }
    bg = np.array([243.0, 209.0, 180.0])
    for name, image in layers.items():
        arr = np.asarray(image)
        alpha = arr[:, :, 3]
        edge = (alpha > 0) & (alpha < 250)
        rgb = arr[:, :, :3].astype(np.float32)
        warm = np.sqrt(np.sum((rgb - bg) ** 2, axis=2)) < 13.0
        edge_count = int(edge.sum())
        warm_edge_count = int((edge & warm).sum())
        report["layers"][name] = {
            "alpha_bbox": alpha_bbox(image),
            "nonzero_alpha_pixels": int((alpha > 0).sum()),
            "partial_alpha_pixels": edge_count,
            "warm_paper_like_partial_edge_pixels": warm_edge_count,
            "warm_edge_ratio": round(warm_edge_count / max(1, edge_count), 6),
        }
    (QA / "FilterL1_QA_Metrics.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    return report


def main():
    if not SOURCE.exists():
        raise FileNotFoundError(f"Approved master missing: {SOURCE}")
    OUT.mkdir(parents=True, exist_ok=True)
    QA.mkdir(parents=True, exist_ok=True)

    layers = {
        "FilterL1_Base.png": make_source_layer(base_mask(), paper_reject=True, cleanup=0.72),
        "FilterL1_RawLiquid.png": make_source_layer(
            raw_liquid_mask(), paper_reject=False, opacity=0.72, unmatte=True, cleanup=0.15),
        "FilterL1_RawParticles.png": make_source_layer(
            particles_mask(), paper_reject=False, opacity=0.58, unmatte=True),
        "FilterL1_FilterCartridge.png": make_source_layer(
            cartridge_mask(), paper_reject=False, opacity=0.98, unmatte=True, cleanup=0.36),
        "FilterL1_FunnelAndPipe.png": make_source_layer(
            funnel_pipe_mask(), paper_reject=True, cleanup=0.52),
        "FilterL1_Bottle.png": make_source_layer(bottle_mask(), paper_reject=True, cleanup=0.42),
        "FilterL1_BottleFill.png": make_source_layer(
            bottle_fill_mask(), paper_reject=False, opacity=0.86, unmatte=True, cleanup=0.18),
        "FilterL1_Badge.png": make_source_layer(badge_mask(), paper_reject=True, cleanup=0.48),
    }
    flow = flow_base_mask()
    for index in range(4):
        layers[f"FilterL1_CleanFlow_{index + 1:02d}.png"] = flow_frame(flow, index)

    for name, image in layers.items():
        save(name, image)

    qa_sheet(layers)
    source_sha = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    report = metrics(layers, source_sha)
    print(f"Approved source SHA-256: {source_sha}")
    print(f"Generated {len(layers)} listed runtime PNGs in {OUT}")
    for name, data in report["layers"].items():
        print(f"{name}: bbox={data['alpha_bbox']} warm-edge={data['warm_edge_ratio']:.4%}")


if __name__ == "__main__":
    SOURCE_IMAGE = Image.open(SOURCE).convert("RGB")
    SOURCE_RGB = np.asarray(SOURCE_IMAGE, dtype=np.uint8)
    BACKGROUND = background_field(SOURCE_IMAGE.height, SOURCE_IMAGE.width)
    SEPARATION = source_separation()
    main()
