# /// script
# requires-python = ">=3.11"
# dependencies = [
#   "openai>=1.99",
#   "pillow>=11",
#   "pillow-heif>=1.0",
#   "jsonschema>=4.23",
#   "python-dotenv>=1.0",
# ]
# ///
"""Identify electronic parts in photos with a vision model.

Prototype of the step the capture API will run: images in, structured JSON out.
Works with any OpenAI-compatible endpoint; the default setup is Azure OpenAI's
v1 API (base URL from `terraform output openai_base_url`).

    uv run vision/identify.py photo.jpg [more.jpg ...] [--save results/]

Configuration (environment or vision/.env):
    SHELFIE_AI_BASE_URL   e.g. https://oai-shelfie-abc123.openai.azure.com/openai/v1/
    SHELFIE_AI_API_KEY    API key
    SHELFIE_AI_MODEL      deployment / model name, e.g. gpt-5-mini
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import os
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
MAX_EDGE_PX = 1600  # enough to read chip markings, keeps token cost down
REASONING_EFFORTS = ("minimal", "low", "medium", "high")


def load_config(args: argparse.Namespace) -> tuple[str, str, str]:
    try:
        from dotenv import load_dotenv

        load_dotenv(HERE / ".env")
    except ImportError:
        pass

    base_url = args.base_url or os.environ.get("SHELFIE_AI_BASE_URL")
    api_key = os.environ.get("SHELFIE_AI_API_KEY")
    model = args.model or os.environ.get("SHELFIE_AI_MODEL", "gpt-5-mini")

    missing = [n for n, v in (("SHELFIE_AI_BASE_URL", base_url), ("SHELFIE_AI_API_KEY", api_key)) if not v]
    if missing:
        sys.exit(f"Missing configuration: {', '.join(missing)} (set them in the environment or {HERE / '.env'})")
    return base_url, api_key, model


def image_to_data_url(path: Path) -> str:
    """Load any common format (incl. iPhone HEIC), fix rotation, downscale, re-encode as JPEG."""
    from PIL import Image, ImageOps

    try:
        import pillow_heif

        pillow_heif.register_heif_opener()
    except ImportError:
        pass

    with Image.open(path) as img:
        img = ImageOps.exif_transpose(img).convert("RGB")
        img.thumbnail((MAX_EDGE_PX, MAX_EDGE_PX))
        buf = io.BytesIO()
        img.save(buf, format="JPEG", quality=85)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def identify(images: list[Path], base_url: str, api_key: str, model: str, effort: str, hint: str | None) -> tuple[dict, dict]:
    from openai import OpenAI

    schema = json.loads((HERE / "schema.json").read_text())
    system_prompt = (HERE / "prompt.md").read_text()

    user_content: list[dict] = [
        {"type": "text", "text": hint or "Identify the electronic parts in these images."}
    ]
    for path in images:
        user_content.append({"type": "image_url", "image_url": {"url": image_to_data_url(path), "detail": "high"}})

    client = OpenAI(base_url=base_url, api_key=api_key)
    started = time.monotonic()
    response = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_content},
        ],
        response_format={
            "type": "json_schema",
            "json_schema": {"name": "shelfie_identification", "strict": True, "schema": schema},
        },
        reasoning_effort=effort,
        max_completion_tokens=6000,
    )
    elapsed = time.monotonic() - started

    choice = response.choices[0]
    if getattr(choice.message, "refusal", None):
        sys.exit(f"Model refused: {choice.message.refusal}")
    if choice.finish_reason == "length":
        sys.exit("Model ran out of tokens before finishing; try --effort low or fewer images.")

    result = json.loads(choice.message.content)
    validate(result, schema)

    usage = response.usage
    meta = {
        "model": response.model,
        "seconds": round(elapsed, 1),
        "input_tokens": getattr(usage, "prompt_tokens", None),
        "output_tokens": getattr(usage, "completion_tokens", None),
    }
    return result, meta


def validate(result: dict, schema: dict) -> None:
    import jsonschema

    jsonschema.validate(result, schema)
    for item in result["items"]:
        if not 0.0 <= item["confidence"] <= 1.0:
            raise jsonschema.ValidationError(f"confidence out of range: {item['confidence']}")


def print_summary(result: dict, meta: dict) -> None:
    print(f"\nSource: {result['source_type']}   ({meta['model']}, {meta['seconds']} s, "
          f"{meta['input_tokens']} in / {meta['output_tokens']} out tokens)\n")
    for i, item in enumerate(result["items"], 1):
        mpn = f"  MPN {item['mpn']}" if item["mpn"] else ""
        qty = f"  ×{item['quantity_estimate']}" if item["quantity_estimate"] else ""
        print(f"{i}. {item['name']}{mpn}{qty}   [{item['confidence']:.0%}]")
        print(f"   {' › '.join(item['category_path'])}" + (f"   package {item['package']}" if item["package"] else ""))
        if item["key_parameters"]:
            params = ", ".join(f"{p['name']} {p['value']}{(' ' + p['unit']) if p['unit'] else ''}" for p in item["key_parameters"])
            print(f"   {params}")
        if item["visible_markings"]:
            print(f"   markings: {' | '.join(item['visible_markings'])}")
        print(f"   search: {', '.join(item['search_terms'])}")
        if item["notes"]:
            print(f"   note: {item['notes']}")
        print()
    if not result["items"]:
        print("No parts identified.\n")
    if result["needs_better_photo"]:
        print(f"📷 Better photo needed: {result['photo_advice']}\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("images", nargs="+", type=Path, help="one or more photos of the same subject")
    parser.add_argument("--hint", help="optional context, e.g. 'bag from my last AliExpress order'")
    parser.add_argument("--model", help="override SHELFIE_AI_MODEL")
    parser.add_argument("--base-url", help="override SHELFIE_AI_BASE_URL")
    parser.add_argument("--effort", choices=REASONING_EFFORTS, default="low", help="reasoning effort (default: low)")
    parser.add_argument("--save", type=Path, help="folder to save the JSON result in")
    parser.add_argument("--json", action="store_true", help="print raw JSON instead of a summary")
    args = parser.parse_args()

    for path in args.images:
        if not path.is_file():
            sys.exit(f"Not a file: {path}")

    base_url, api_key, model = load_config(args)
    result, meta = identify(args.images, base_url, api_key, model, args.effort, args.hint)

    if args.json:
        print(json.dumps({"meta": meta, "result": result}, indent=2, ensure_ascii=False))
    else:
        print_summary(result, meta)

    if args.save:
        args.save.mkdir(parents=True, exist_ok=True)
        out = args.save / f"{args.images[0].stem}.json"
        out.write_text(json.dumps({"images": [str(p) for p in args.images], "meta": meta, "result": result},
                                  indent=2, ensure_ascii=False))
        print(f"Saved {out}")


if __name__ == "__main__":
    main()
