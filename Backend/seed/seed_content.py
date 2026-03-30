"""Seed script — populate content_versions with sectors, levels, balance, and shop catalog.

Usage:
    python -m seed.seed_content
"""

from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path
from typing import Any

from dotenv import load_dotenv
from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
BACKEND_DIR = Path(__file__).resolve().parent.parent
DATA_DIR = Path(__file__).resolve().parent / "data"

# Load .env from Backend/
load_dotenv(BACKEND_DIR / ".env")

DATABASE_URL: str = os.environ.get("DATABASE_URL", "")
if not DATABASE_URL:
    print("ERROR: DATABASE_URL is not set. Check your .env file.")
    sys.exit(1)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _load_json(path: Path) -> Any:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


async def _upsert_content(
    session: AsyncSession,
    content_type: str,
    content_id: str | None,
    data: dict[str, Any],
    version: int = 1,
) -> str:
    """Insert or skip if (content_type, content_id, version) already exists.

    Returns 'inserted' or 'skipped'.
    """
    stmt = (
        select(text("1"))
        .select_from(text("content_versions"))
        .where(
            text("content_type = :ct AND " "COALESCE(content_id, '') = COALESCE(:cid, '') AND " "version = :ver"),
        )
        .params(ct=content_type, cid=content_id, ver=version)
    )

    result = await session.execute(stmt)
    if result.scalar_one_or_none() is not None:
        return "skipped"

    await session.execute(
        text(
            "INSERT INTO content_versions "
            "(content_type, content_id, version, data, is_active) "
            "VALUES (:ct, :cid, :ver, :data, TRUE)"
        ),
        {"ct": content_type, "cid": content_id, "ver": version, "data": json.dumps(data)},
    )
    return "inserted"


# ---------------------------------------------------------------------------
# Seed functions
# ---------------------------------------------------------------------------


async def seed_sectors(session: AsyncSession) -> None:
    sectors: list[dict[str, Any]] = _load_json(DATA_DIR / "sectors.json")
    print(f"  Sectors: {len(sectors)} found")
    for sector in sectors:
        sid = sector["sectorId"]
        action = await _upsert_content(session, "sector", sid, sector)
        print(f"    {sid}: {action}")


async def seed_levels(session: AsyncSession) -> None:
    levels_dir = DATA_DIR / "levels"
    total = 0
    for sector_file in sorted(levels_dir.glob("sector_*.json")):
        levels: list[dict[str, Any]] = _load_json(sector_file)
        print(f"  Levels from {sector_file.name}: {len(levels)} found")
        for level in levels:
            lid = level["levelId"]
            action = await _upsert_content(session, "level", lid, level)
            if action == "inserted":
                total += 1
            print(f"    {lid}: {action}")
    print(f"  Total levels inserted: {total}")


async def seed_balance(session: AsyncSession) -> None:
    balance: dict[str, Any] = _load_json(DATA_DIR / "balance.json")
    action = await _upsert_content(session, "balance", None, balance)
    print(f"  Balance config: {action}")


async def seed_shop_catalog(session: AsyncSession) -> None:
    items: list[dict[str, Any]] = _load_json(DATA_DIR / "shop_catalog.json")
    print(f"  Shop catalog: {len(items)} items")
    catalog_data = {"items": items}
    action = await _upsert_content(session, "shop_catalog", None, catalog_data)
    print(f"  Shop catalog: {action}")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


async def main() -> None:
    print(f"Connecting to database …")
    engine = create_async_engine(DATABASE_URL, echo=False)
    session_factory = async_sessionmaker(engine, expire_on_commit=False)

    async with session_factory() as session:
        async with session.begin():
            print("\n[1/4] Seeding sectors …")
            await seed_sectors(session)

            print("\n[2/4] Seeding levels …")
            await seed_levels(session)

            print("\n[3/4] Seeding balance …")
            await seed_balance(session)

            print("\n[4/4] Seeding shop catalog …")
            await seed_shop_catalog(session)

        # commit happens automatically when `session.begin()` context exits

    await engine.dispose()
    print("\nSeed complete ✓")


if __name__ == "__main__":
    asyncio.run(main())
