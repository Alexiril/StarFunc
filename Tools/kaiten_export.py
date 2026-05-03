#!/usr/bin/env python3
"""
kaiten_export.py — Parses one or more task .md files and produces a Kaiten universal
import bundle in kaiten_export/ at the project root.  Each input file becomes its own
board (3 columns: To Do / In Progress / Done) with its own Phase custom-field.

Usage:
    python Tools/kaiten_export.py                                  # default: Docs/Tasks.md
    python Tools/kaiten_export.py Docs/Tasks.md "Docs/Backend tasks.md"

Output (kaiten_export/):
    meta-data.json, spaces.json, boards.json, columns.json,
    users.json, custom-fields.json, cards.json, comments.json
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------------------
# Configuration — edit these before running
# ---------------------------------------------------------------------------

OWNER_EMAIL = "alekcey_kirilov@mail.ru"
OWNER_FULL_NAME = "Alexey Kirilov"
OWNER_USERNAME = "alekcey_kirilov"

SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
OUTPUT_DIR = PROJECT_ROOT / "kaiten_export"

DEFAULT_INPUT_FILES = [
    PROJECT_ROOT / "Docs" / "Tasks.md",
]

# Kaiten colour indices cycled across Phase options
PHASE_COLORS = [8, 9, 13, 17, 3, 7, 11, 15]

# Column type constants (Kaiten spec: 1=Todo, 2=InProgress, 3=Done)
COL_TYPE_TODO = 1
COL_TYPE_INPROG = 2
COL_TYPE_DONE = 3

NOW = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")

# Shared non-board entity IDs (allocated first by IdGen)
_STATIC_USER_ID = "10001"
_STATIC_SPACE_ID = "starfunc"  # spaces allow string IDs per the Kaiten example

# ---------------------------------------------------------------------------
# ID generator
# ---------------------------------------------------------------------------


class IdGen:
    """Thread-unsafe but simple sequential string-ID allocator."""

    def __init__(self, start: int = 10002) -> None:
        self._n = start

    def next(self) -> str:
        v = self._n
        self._n += 1
        return str(v)


# ---------------------------------------------------------------------------
# Per-board context
# ---------------------------------------------------------------------------


@dataclass
class BoardCtx:
    board_id: str
    col_backlog_id: str
    col_todo_id: str
    col_inprog_id: str
    col_done_id: str
    phase_field_id: str
    phase_opt_ids: dict  # phase_idx -> option_id
    board_title: str

    @property
    def status_to_col_id(self) -> dict:
        return {
            "done": self.col_done_id,
            "partial": self.col_inprog_id,
            "todo": self.col_todo_id,
            "backlog": self.col_backlog_id,
        }


def alloc_board_ctx(idgen, phases, board_title):
    """Allocate all IDs needed for one board and return its context."""
    board_id = idgen.next()
    col_backlog_id = idgen.next()
    col_todo_id = idgen.next()
    col_inprog_id = idgen.next()
    col_done_id = idgen.next()
    phase_field_id = idgen.next()
    phase_opt_ids = {phase.phase_idx: idgen.next() for phase in phases}
    return BoardCtx(
        board_id=board_id,
        col_backlog_id=col_backlog_id,
        col_todo_id=col_todo_id,
        col_inprog_id=col_inprog_id,
        col_done_id=col_done_id,
        phase_field_id=phase_field_id,
        phase_opt_ids=phase_opt_ids,
        board_title=board_title,
    )


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------


@dataclass
class TaskData:
    task_id_str: str  # "1.3a" or "S1.3a"
    phase_num: str  # "1" or "S1"
    phase_name: str  # "Ядро и прототип"
    title: str  # cleaned (no ~~)
    body_md: str  # full raw markdown body of the task
    is_done: bool
    dep_ids: list = field(default_factory=list)  # ["0.1", "1.2"]
    explicit_status: str | None = None  # "done" | "partial" | None (overrides phase logic)
    # Filled during build phase:
    internal_id: str = ""
    resolved_dep_ids: list = field(default_factory=list)
    status: str = "todo"  # "done" | "partial" | "todo" | "backlog"


@dataclass
class PhaseData:
    phase_num: str  # "0", "S0", etc.
    phase_idx: int  # sequential 0-based index for ID/colour math
    phase_name: str
    is_done: bool
    tasks: list = field(default_factory=list)


# ---------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------

# Phase numbers may have an optional letter prefix, e.g. S0, S1 (Backend tasks.md).
PHASE_RE = re.compile(
    r"^## (?:~~)?Фаза ([A-ZА-Я]?\d+) — ([^~\n]+?)(?:~~)?(?:\s*\(Done\)|\s*\(~\))?\s*$",
    re.MULTILINE,
)

# Task ID may have a letter prefix (S) and/or letter suffix (a-z)
TASK_RE = re.compile(
    r"^### (?:~~)?Задача ([A-ZА-Я]?\d+\.\d+[a-z]?) — ([^~\n]+?)(?:~~)?(?:\s*\((?:Done|In progress)\))?\s*$",
    re.MULTILINE,
)

# Dependency task IDs (handles S0.1 and 1.3a style)
DEP_RE = re.compile(r"\b([A-ZА-Я]?\d+\.\d+[a-z]?)\b")


def _detect_done(raw_line):
    return "~~" in raw_line or "(Done)" in raw_line


def _detect_explicit_status(raw_line):
    """Return 'done', 'partial', or None based on explicit markers in the header line."""
    if "(Done)" in raw_line or "~~" in raw_line:
        return "done"
    if "(In progress)" in raw_line:
        return "partial"
    return None


def _extract_deps(body):
    m = re.search(r"\*\*Зависимости:\*\*\s*(.+?)(?=\n\*\*|\n---|\Z)", body, re.DOTALL)
    if not m:
        return []
    dep_text = m.group(1)
    if "нет" in dep_text.lower():
        return []
    return DEP_RE.findall(dep_text)


def parse_tasks_md(path):
    """Parse a tasks .md file and return a list of PhaseData with nested TaskData."""
    text = path.read_text(encoding="utf-8")
    phases = []

    phase_matches = list(PHASE_RE.finditer(text))
    if not phase_matches:
        print(f"Warning: no phase headers found in {path.name}", file=sys.stderr)

    for i, pm in enumerate(phase_matches):
        raw_line = pm.group(0)
        phase_num = pm.group(1)
        phase_name = pm.group(2).strip()
        phase_is_done = _detect_done(raw_line)

        block_start = pm.end()
        block_end = phase_matches[i + 1].start() if i + 1 < len(phase_matches) else len(text)
        phase_block = text[block_start:block_end]

        phase = PhaseData(
            phase_num=phase_num,
            phase_idx=i,
            phase_name=phase_name,
            is_done=phase_is_done,
        )

        task_matches = list(TASK_RE.finditer(phase_block))
        for j, tm in enumerate(task_matches):
            raw_task_line = tm.group(0)
            task_id_str = tm.group(1)
            title = tm.group(2).strip()
            explicit_status = _detect_explicit_status(raw_task_line)
            # Phase-level done still forces unmarked tasks to done
            if phase_is_done and explicit_status is None:
                explicit_status = "done"

            t_start = tm.end()
            t_end = task_matches[j + 1].start() if j + 1 < len(task_matches) else len(phase_block)
            body_md = phase_block[t_start:t_end].strip()

            phase.tasks.append(
                TaskData(
                    task_id_str=task_id_str,
                    phase_num=phase_num,
                    phase_name=phase_name,
                    title=title,
                    body_md=body_md,
                    is_done=explicit_status == "done",
                    dep_ids=_extract_deps(body_md),
                    explicit_status=explicit_status,
                )
            )

        phases.append(phase)

    return phases


# ---------------------------------------------------------------------------
# Status & ID assignment
# ---------------------------------------------------------------------------


def assign_statuses(phases):
    """
    - done phase                       -> all unmarked tasks "done"
    - active phase (first non-done phase that has at least one (In progress) task,
      or the first non-done phase as fallback) -> unmarked tasks "todo" (To Do)
    - phases after the active phase    -> unmarked tasks "backlog" (Backlog)
    Per-task explicit markers always override the phase default.
    """
    # Locate the active phase index
    active_idx = None
    for i, phase in enumerate(phases):
        if not phase.is_done and any(t.explicit_status == "partial" for t in phase.tasks):
            active_idx = i
            break
    # Fallback: no in-progress tasks anywhere → first non-done phase is active
    if active_idx is None:
        for i, phase in enumerate(phases):
            if not phase.is_done:
                active_idx = i
                break

    for i, phase in enumerate(phases):
        if phase.is_done:
            phase_default = "done"
        elif i == active_idx:
            phase_default = "todo"
        else:
            phase_default = "backlog"

        for task in phase.tasks:
            task.status = task.explicit_status if task.explicit_status is not None else phase_default
            task.is_done = task.status == "done"


def assign_ids(phases, idgen):
    """Assign globally-unique IDs to tasks using the shared generator."""
    id_map = {}
    for phase in phases:
        for task in phase.tasks:
            task.internal_id = idgen.next()
            id_map[task.task_id_str] = task.internal_id
    return id_map


def resolve_deps(phases, id_map):
    for phase in phases:
        for task in phase.tasks:
            task.resolved_dep_ids = [id_map[d] for d in task.dep_ids if d in id_map]


# ---------------------------------------------------------------------------
# JSON entity builders
# ---------------------------------------------------------------------------


def build_users():
    return [
        {
            "id": _STATIC_USER_ID,
            "email": OWNER_EMAIL,
            "full_name": OWNER_FULL_NAME,
            "username": OWNER_USERNAME,
        }
    ]


def build_spaces():
    return [
        {
            "id": _STATIC_SPACE_ID,
            "title": "StarFunc",
            "created": NOW,
        }
    ]


def build_board_entry(ctx):
    return {
        "id": ctx.board_id,
        "title": ctx.board_title,
        "created": NOW,
        "author_id": _STATIC_USER_ID,
        "space_id": _STATIC_SPACE_ID,
    }


def build_column_entries(ctx):
    return [
        {
            "id": ctx.col_backlog_id,
            "title": "Бэклог",
            "created": NOW,
            "updated": NOW,
            "sort_order": 1,
            "type": COL_TYPE_TODO,
            "board_id": ctx.board_id,
        },
        {
            "id": ctx.col_todo_id,
            "title": "К выполнению",
            "created": NOW,
            "updated": NOW,
            "sort_order": 2,
            "type": COL_TYPE_TODO,
            "board_id": ctx.board_id,
        },
        {
            "id": ctx.col_inprog_id,
            "title": "В работе",
            "created": NOW,
            "updated": NOW,
            "sort_order": 3,
            "type": COL_TYPE_INPROG,
            "board_id": ctx.board_id,
        },
        {
            "id": ctx.col_done_id,
            "title": "Готово",
            "created": NOW,
            "updated": NOW,
            "sort_order": 4,
            "type": COL_TYPE_DONE,
            "board_id": ctx.board_id,
        },
    ]


def build_custom_field_entry(phases, ctx):
    options = [
        {
            "id": ctx.phase_opt_ids[phase.phase_idx],
            "value": f"{phase.phase_num} — {phase.phase_name}",
            "color": PHASE_COLORS[phase.phase_idx % len(PHASE_COLORS)],
            "sort_order": phase.phase_idx,
        }
        for phase in phases
    ]
    return {
        "id": ctx.phase_field_id,
        "type": "select",
        "name": "Фаза",
        "options": options,
    }


def build_card_entries(phases, ctx):
    cards = []
    col_map = ctx.status_to_col_id
    for phase in phases:
        for task in phase.tasks:
            cards.append(
                {
                    "id": task.internal_id,
                    "owner_id": _STATIC_USER_ID,
                    "responsible_id": _STATIC_USER_ID,
                    "column_id": col_map[task.status],
                    "title": f"{task.task_id_str} — {task.title}",
                    "description": f"## Задача {task.task_id_str} — {task.title}\n\n{task.body_md}",
                    "description_type": "markdown",
                    "tags": [{"name": task.task_id_str}, {"name": f"Фаза {phase.phase_num}"}],
                    "history": [],
                    "created": NOW,
                    "links": [],
                    "completed": task.is_done,
                    "completed_by": _STATIC_USER_ID if task.is_done else None,
                    "completed_at": NOW if task.is_done else None,
                    "properties": [
                        {
                            "id": ctx.phase_field_id,
                            "value": [ctx.phase_opt_ids[phase.phase_idx]],
                        }
                    ],
                    "blocked_by_card_ids": task.resolved_dep_ids,
                    "blocks_card_ids": [],
                    "child_card_ids": [],
                }
            )
    return cards


# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------

ENTITY_FILES = {
    "users": "users.json",
    "spaces": "spaces.json",
    "boards": "boards.json",
    "columns": "columns.json",
    "custom_fields": "custom-fields.json",
    "cards": "cards.json",
    "comments": "comments.json",
}


def write_output(output_dir, entities):
    output_dir.mkdir(parents=True, exist_ok=True)

    for key, filename in ENTITY_FILES.items():
        (output_dir / filename).write_text(
            json.dumps(entities.get(key, []), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )

    meta = {
        "entities": list(ENTITY_FILES.keys()),
        "entities_paths_map": {k: [f"./{v}"] for k, v in ENTITY_FILES.items()},
    }
    (output_dir / "meta-data.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def _board_title(path):
    return path.stem  # e.g. "Tasks" or "Backend tasks"


def main():
    raw_args = sys.argv[1:]
    input_files = [Path(a) for a in raw_args] if raw_args else DEFAULT_INPUT_FILES

    missing = [f for f in input_files if not f.exists()]
    if missing:
        for f in missing:
            print(f"Error: {f} not found", file=sys.stderr)
        sys.exit(1)

    # Shared ID generator — user/space IDs are static, board IDs onwards are dynamic
    idgen = IdGen(start=10002)

    all_boards = []
    all_columns = []
    all_custom_fields = []
    all_cards = []
    # Global dep map spans all files so cross-file deps resolve correctly
    global_id_map = {}

    # Pass 1: parse + assign statuses + assign IDs (no dep resolution yet)
    file_data = []
    for path in input_files:
        print(f"Parsing {path} ...")
        phases = parse_tasks_md(path)
        total = sum(len(p.tasks) for p in phases)
        print(f"  Found {len(phases)} phases, {total} tasks")

        assign_statuses(phases)
        local_id_map = assign_ids(phases, idgen)
        global_id_map.update(local_id_map)

        ctx = alloc_board_ctx(idgen, phases, _board_title(path))
        file_data.append((path, phases, ctx))

    # Pass 2: resolve deps (global_id_map is now fully populated) and build entities
    for _path, phases, ctx in file_data:
        resolve_deps(phases, global_id_map)
        all_boards.append(build_board_entry(ctx))
        all_columns.extend(build_column_entries(ctx))
        all_custom_fields.append(build_custom_field_entry(phases, ctx))
        all_cards.extend(build_card_entries(phases, ctx))

    entities = {
        "users": build_users(),
        "spaces": build_spaces(),
        "boards": all_boards,
        "columns": all_columns,
        "custom_fields": all_custom_fields,
        "cards": all_cards,
        "comments": [
            {
                "id": "10001",
                "card_id": all_cards[0]["id"],
                "text": "Импорт выполнен успешно.",
                "author_id": _STATIC_USER_ID,
                "author_name": OWNER_FULL_NAME,
                "created": NOW,
            }
        ],
    }

    write_output(OUTPUT_DIR, entities)

    col_done_ids = {ctx.col_done_id for _, _, ctx in file_data}
    col_inprog_ids = {ctx.col_inprog_id for _, _, ctx in file_data}
    col_todo_ids = {ctx.col_todo_id for _, _, ctx in file_data}
    col_backlog_ids = {ctx.col_backlog_id for _, _, ctx in file_data}
    done_n = sum(1 for c in all_cards if c["column_id"] in col_done_ids)
    inprog_n = sum(1 for c in all_cards if c["column_id"] in col_inprog_ids)
    todo_n = sum(1 for c in all_cards if c["column_id"] in col_todo_ids)
    backlog_n = sum(1 for c in all_cards if c["column_id"] in col_backlog_ids)

    print(f"\nOutput -> {OUTPUT_DIR}/")
    print(f"  Boards      : {len(all_boards)}")
    print(f"  Cards total : {len(all_cards)}")
    print(f"  Done        : {done_n}")
    print(f"  In Progress : {inprog_n}")
    print(f"  To Do       : {todo_n}")
    print(f"  Backlog     : {backlog_n}")
    print("\nZip kaiten_export/ and upload to the Kaiten import tool.")


if __name__ == "__main__":
    main()
