# Prototype verification

Windows host, 2 November 2026. Mod sources compiled by the actual runtime mod pipeline.

| Check | Result |
|---|---|
| NPC mod unit contracts | 4 passed |
| Existing dialog engine mod tests | 35 passed |
| Existing RPG mod tests | 78 passed |
| Core conditions and scenario xUnit tests | 75 passed |
| Concurrent ambient integration assertions | 19 passed |
| Resource atlas | 0 errors, 0 warnings |
| Locale atlas | 0 errors; translation-gap warnings remain |

Integration covers concurrent runs, live role contexts, speech cue delivery and deduplication, timed conditions, walking, NPC facing, modal pause, live locale change, addressed block patches, ordinary interaction, scene cleanup and the actual JRP company.

Accepted screenshots in `media/` come from job `npc_accept_20261102T134130_22f5b5`. Raw screenshots, logs, rejected iterations and source records are outside repositories in `/workspace/art_sources/cthangover/npc_barks_base/documentation/20261102/`.

The initial fixture switched scenes before the game shell was ready; a later demo check failed to finish its ordinary introduction. Those driver preconditions were corrected. A subsequent input probe missed a same-frame key press/release; the final driver holds E across frames and retains both interaction assertions. An outdated NuGet restore graph initially prevented xUnit from starting; restoring dependencies resolved it without project-file edits. Failed job logs remain in `jobs/done/`.

No GPU inference or second watcher was used. No commits were created.


## In-game authoring and debugging

Accepted job: `barks_tools_accept_20261102T133009_dc3c90`, exit 0, 2 November 2026. Logs: `gen/npc_barks_tools/acceptance/`; complete host log: `jobs/done/barks_tools_accept_20261102T133009_dc3c90.log`.

| Check | Result |
|---|---|
| NPC unit contracts, including transactional authoring and empty selection | 5 passed |
| Actual Studio/editor background GUI | 30 passed |
| Existing Studio modal GUI, with an empty background catalog | 86 passed |
| Existing editor ordinary F5 GUI | 12 passed |
| Original NPC concurrent integration | 19 passed |
| Core conditions, metadata, replacements and event bus xUnit | 66 passed |
| Resource atlas | 0 errors, 0 warnings |
| Locale atlas | 0 errors; 3049 project translation warnings remain |

Earlier verification also passed the existing dialog (35) and RPG (78) mod contracts. The GUI tests exercise actual buttons, source editing, breakpoint gutters and F5. They cover isolated pauses/clocks, speech-triggered responses, world pause, transactional Save, live catalog reload while old executions keep their program, restart with the new source, preview without saving, stop, returning to the same unsaved editor buffer, and observer cleanup.

The first GUI run exposed an incorrect `SaveActive` return value; the file rejection assertion was retained and the implementation fixed. Final regression found a null scenario ID in the new breakpoint API when the ordinary game has no background scenarios; the API guard was fixed without changing the existing Studio assertions. A dynamic locale-key prefix was replaced by explicit keys. A verification job using the wrong unit-runner flag timed out (124); the corrected command completed successfully. Failure logs remain in `jobs/done/`.

The current authoring screenshots in `media/` are actual accepted game captures. Raw captures, intermediate versions, logs and capture notes are archived outside repositories in `/workspace/art_sources/cthangover/npc_barks_base/documentation/20261102_tools/`.
