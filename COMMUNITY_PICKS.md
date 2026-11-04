# Community picks

Branch `claude/community-picks` (engine: Sleepsong/FreeSO `simitone/community-picks`), on `simitone-forked` (engine 7e76c19). These are fixes from open pull requests on the community fork (alexjyong/Simitone and alexjyong/FreeSO, by sithtoast), cherry-picked with their authors kept. None of them were merged upstream when this branch was made (2 November 2026).

**Update, 4 November:** the community fork has since merged FreeSO#8, FreeSO#12 and Simitone#34 (with #31) into its own `simitone-forked`, and the fork's latest `simitone-forked` is merged into this branch (engine: alexjyong/FreeSO d8a3c175). Those three now come from upstream; the rest below are still only here. The merge also brought the community fork's neighbor faces in dialogs, test mode, dialog icons, custom animations and .hsm fix. `UIIconCache` keeps both this branch's drawn catalog icon and upstream's neighbor face.

## Engine (FreeSO)

| PR | What it fixes |
|---|---|
| [alexjyong/FreeSO#8](https://github.com/alexjyong/FreeSO/pull/8) (5 commits) | SimAntics exceptions on lots with Sims: person data past entry 88, slots a Sim doesn't have, saved frames in missing routines or past a routine's end, frames saved with an older version of a routine (the Newbies walking off their own lot), scripts asking about a Sim who has left. Also one-shot sounds that looped (the paper carrier whistling) |
| [alexjyong/FreeSO#9](https://github.com/alexjyong/FreeSO/pull/9) | Cars stuck honking on hillside lots, such as the Goths' 5 Sim Lane: the lot edge counted as sloped |
| [alexjyong/FreeSO#12](https://github.com/alexjyong/FreeSO/pull/12) | Expansion pack objects (version 138b) had their last fields cut off, so community lot Buy mode had nothing in its categories |
| [alexjyong/FreeSO#13](https://github.com/alexjyong/FreeSO/pull/13) | Houses load with their own roof pitch instead of 0.66 |

## Simitone

| PR | What it fixes |
|---|---|
| [alexjyong/Simitone#46](https://github.com/alexjyong/Simitone/pull/46) | Objects with no catalog picture (Makin' Magic's mini golf pieces, some downloads) get a drawn icon |
| [alexjyong/Simitone#42](https://github.com/alexjyong/Simitone/pull/42) | Text entry dialogs (naming a pet or a baby) show their text box, focused |
| [alexjyong/Simitone#35](https://github.com/alexjyong/Simitone/pull/35) | Deleting a family detaches its members, so a new family doesn't pick them up. The CAS debug log writes are removed |
| [alexjyong/Simitone#34](https://github.com/alexjyong/Simitone/pull/34) | The Simitone side of FreeSO#8: automatic fast-forward while the whole family is away, as in TS1, and a crash on the neighbourhood screen with `-nosound`. Its submodule commits are left out: this branch points at its own engine branch |
| [alexjyong/Simitone#41](https://github.com/alexjyong/Simitone/pull/41) | Only a submodule bump for FreeSO#9, which is in the engine branch above |

Also here:

- **The Linux CI fix** from the controller branch: the renamed gdk-pixbuf package, and the runner pinned to Ubuntu 24.04. `simitone-forked` can't build on Linux without it.
- **The engine URL:** `.gitmodules` points at Sleepsong/FreeSO, where the engine commits are.

## Merging into the controller branch later

The controller branch's engine already fixes some of the same exceptions as FreeSO#8, in its own way:

| Problem | Controller engine | FreeSO#8 |
|---|---|---|
| Person data past entry 88 | Keeps the full array when a Sim inherits neighbourhood data | Copies into the existing array |
| Slots a Sim doesn't have | Bounds check in `VMAvatar.GetSlot` | The same |
| Broken saved frames | Restarts them in `VMThread` when the lot loads, and notes them in `simantics-errors.log` | Resets them in `VMTS1ActivatorNew` when the house is converted |

Merging means keeping one version of each. FreeSO#8's check for frames saved with an older version of a routine has no counterpart there, so it should come across.

`TS1CASScreen` will conflict too: the controller branch puts the debug log behind `-logs`, and #35 removes it.
