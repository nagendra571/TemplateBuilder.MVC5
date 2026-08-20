---
name: memory
description: Use when starting a session, before answering questions about past decisions or preferences, when the user says "remember", "save this", "don't forget", "what do you remember", when finishing significant work, or when ending a session. Also use before answering anything the user has explained before — read MEMORY.md first.
---

# Memory

## Overview

`MEMORY.md` in the repo root is the persistent memory layer. Every session starts cold; this file is what carries knowledge from one session to the next. Read it at session start, write to it as you learn.

## Workflow

1. **At session start:** read `MEMORY.md` before acting on tasks. It contains decisions, preferences, and gotchas from previous sessions.
2. **During the session:** when the user states a durable fact, preference, decision, or lesson learned, record it in `MEMORY.md`.
3. **Before finishing:** re-read `MEMORY.md` and add anything durable from this session that is missing.

## What to save

Save only durable, cross-session knowledge:

- Decisions and their rationale ("chose X over Y because Z")
- User preferences and conventions ("user prefers X", "always run Y before committing")
- Gotchas and lessons ("Z breaks under W", "the client's environment has X")
- Stable facts about the project/environment that took real effort to discover

Do NOT save: transient task status, one-off errors already fixed, things already documented in `CLAUDE.md`/`docs/`, or anything derivable from the code in seconds.

## Entry format

Append new entries at the top of the `## Memories` section, newest first:

```markdown
### YYYY-MM-DD: Short topic

- Fact one.
- Fact two.
```

## Update rules

- Append; never delete older entries. Correct wrong facts by adding a newer entry that supersedes them ("Correction: ...").
- Merge into an existing entry when a new fact belongs to the same topic.
- Keep each bullet to one or two sentences. If `MEMORY.md` grows past ~150 lines, merge and compress older entries instead of letting it balloon.
- Check `CLAUDE.md` and `docs/` before saving to avoid duplicating what is already documented.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Answering from scratch when the user asks about something covered in `MEMORY.md` | Read `MEMORY.md` first; cite it in the answer |
| Saving a whole session transcript | Save only the durable conclusions |
| Logging task progress in `MEMORY.md` | Task status belongs in `PROGRESS.md` |
| Ignoring memory because the answer "seems obvious" | "Obvious" conclusions are exactly what future sessions need |
