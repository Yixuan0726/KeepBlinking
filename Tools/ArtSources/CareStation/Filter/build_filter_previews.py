"""Build low-stimulation review GIFs from the authored FILTER sprite layers."""

from pathlib import Path
from math import sin, pi
from PIL import Image


PROJECT_ROOT = Path(__file__).resolve().parents[4]
ASSET_DIR = PROJECT_ROOT / "Assets" / "KeepBlinking" / "Art" / "CareStation" / "Filter"
PREVIEW_DIR = PROJECT_ROOT / "Logs" / "CareStationFilterPreviews"
BACKGROUND = (23, 35, 33, 255)


def layer(name: str) -> Image.Image:
    return Image.open(ASSET_DIR / f"{name}.png").convert("RGBA")


def shifted(source: Image.Image, y_offset: int) -> Image.Image:
    result = Image.new("RGBA", source.size, (0, 0, 0, 0))
    result.alpha_composite(source, (0, y_offset))
    return result


def frame(level: int, index: int) -> Image.Image:
    base = Image.new("RGBA", (1024, 1024), BACKGROUND)
    base.alpha_composite(layer(f"Filter_L{level}_Base"))
    base.alpha_composite(layer(f"Filter_L{level}_Flow_{index % 4 + 1:02d}"))
    if level <= 2:
        crank = layer(f"Filter_L{level}_Crank")
        pivot = (681, 629) if level == 1 else (678, 534)
        crank = crank.rotate(-index * 22.5, resample=Image.Resampling.BICUBIC, center=pivot)
        base.alpha_composite(crank)
    else:
        brush = layer("Filter_L3_Brush")
        brush_offset = round(sin(index / 12 * 2 * pi) * 9)
        base.alpha_composite(shifted(brush, brush_offset))
        needle = layer("Filter_L3_GaugeNeedle").rotate(
            sin(index / 12 * 2 * pi) * 7,
            resample=Image.Resampling.BICUBIC,
            center=(878, 286),
        )
        base.alpha_composite(needle)
    base.alpha_composite(layer(f"Filter_L{level}_Badge"))
    return base.resize((512, 512), Image.Resampling.LANCZOS)


def main() -> None:
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    for level in range(1, 4):
        frames = [frame(level, index) for index in range(12)]
        suffix = "Flow_Crank" if level <= 2 else "Flow_Brush"
        frames[0].save(
            PREVIEW_DIR / f"Filter_L{level}_{suffix}.gif",
            save_all=True,
            append_images=frames[1:],
            duration=140,
            loop=0,
            optimize=False,
            disposal=2,
        )
    print("Generated three FILTER animation review GIFs.")


if __name__ == "__main__":
    main()
