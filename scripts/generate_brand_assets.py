from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "FrontolFileAnalyzer" / "Assets"
SIZE = 1024


def scale(value: int) -> int:
    return value * 2


def main() -> None:
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw.rounded_rectangle((32, 32, 992, 992), radius=144, fill="#142B42")
    draw.rounded_rectangle((56, 56, 968, 968), radius=120, outline="#244B6B", width=16)

    rows = (284, 512, 740)
    for y in rows:
        draw.line((184, y, 420, y), fill="#F5F7FA", width=56)
        draw.line((604, y, 840, y), fill="#F5F7FA", width=56)

    draw.ellipse((468, 336, 556, 424), fill="#3FA9F5")
    draw.rounded_rectangle((458, 448, 556, 540), radius=12, fill="#3FA9F5")
    draw.polygon(((556, 502), (556, 586), (466, 660), (438, 612), (486, 568), (500, 524)), fill="#3FA9F5")

    png_path = ASSETS / "app-logo.png"
    ico_path = ASSETS / "app.ico"
    image.save(png_path, optimize=True)
    image.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(f"Wrote {png_path}")
    print(f"Wrote {ico_path}")


if __name__ == "__main__":
    main()
