# Pending FreeSO engine patches

The `FreeSO` submodule still points at `alexjyong/FreeSO`. Engine changes for controller support are meant to
live in a FreeSO fork under your account. That fork doesn't exist yet, so the engine commits are kept here as
`git format-patch` files until it does.

| Patch | Base commit | Contents |
|---|---|---|
| `0001-Add-gamepad-input-layer-and-virtual-cursor-fallback.patch` | `7e76c19` (current submodule pin) | Controller support Phase 1: gamepad polling, action bindings, mouse/pad device switching, virtual cursor fallback, settings |

## Moving the patches into your fork

1. On GitHub, fork `alexjyong/FreeSO` to your account.
2. To let Claude push to the fork, install the Claude GitHub App on it.
3. Apply the patches and push a branch. Run these from the Simitone repo root:

```bash
git -C FreeSO remote add fork https://github.com/Sleepsong/FreeSO
```

```bash
git -C FreeSO checkout -b simitone/controller-phase1 7e76c19
```

```bash
git -C FreeSO am ../patches/FreeSO/0001-Add-gamepad-input-layer-and-virtual-cursor-fallback.patch
```

```bash
git -C FreeSO push -u fork simitone/controller-phase1
```

4. Point the submodule at the fork and record the new commit:

```bash
git config -f .gitmodules submodule.FreeSO.url https://github.com/Sleepsong/FreeSO
```

```bash
git submodule sync FreeSO
```

```bash
git add .gitmodules FreeSO && git commit -m "Point FreeSO submodule at fork with controller support"
```

5. Delete this `patches/FreeSO` directory once the submodule commit has been pushed.
