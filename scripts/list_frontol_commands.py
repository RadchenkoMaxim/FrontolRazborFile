from __future__ import annotations

import argparse
import re
from collections import defaultdict
from pathlib import Path

from pypdf import PdfReader


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("pdf", type=Path)
    parser.add_argument("--first-page", type=int, default=190)
    parser.add_argument("--last-page", type=int, default=272)
    args = parser.parse_args()

    reader = PdfReader(args.pdf)
    commands: dict[str, set[int]] = defaultdict(set)
    command_pattern = re.compile(r"\$\$\$([A-Z0-9]+)")

    for page_number in range(args.first_page, args.last_page + 1):
        text = reader.pages[page_number - 1].extract_text() or ""
        for command in command_pattern.findall(text):
            commands[command].add(page_number)

    for command, pages in sorted(commands.items()):
        print(f"{command}: {', '.join(map(str, sorted(pages)))}")


if __name__ == "__main__":
    main()
