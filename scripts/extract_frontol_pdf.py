from __future__ import annotations

import argparse
from pathlib import Path

from pypdf import PdfReader


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("pdf", type=Path)
    parser.add_argument("terms", nargs="+")
    parser.add_argument("--context", type=int, default=900)
    args = parser.parse_args()

    reader = PdfReader(args.pdf)
    lowered_terms = tuple(term.casefold() for term in args.terms)
    print(f"pages={len(reader.pages)}")

    for page_number, page in enumerate(reader.pages, start=1):
        text = page.extract_text() or ""
        lowered_text = text.casefold()
        hits = [term for term, lowered in zip(args.terms, lowered_terms) if lowered in lowered_text]
        if not hits:
            continue

        first_position = min(lowered_text.find(term.casefold()) for term in hits)
        start = max(0, first_position - args.context)
        end = min(len(text), first_position + args.context)
        excerpt = text[start:end].replace("\x00", "")
        print(f"\n=== page {page_number}; hits: {', '.join(hits)} ===")
        print(excerpt)


if __name__ == "__main__":
    main()
