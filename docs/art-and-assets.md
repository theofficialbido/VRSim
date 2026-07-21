# Art and assets

**The 3D art is not in this repository yet.** If you clone this and open
`Main Scene`, the rig will be missing. That is expected, not a broken checkout.

## Why

The rig models total roughly 700 MB, and `Rig(Met2ata3).glb` alone is 149 MB —
past GitHub's hard 100 MB per-file limit, which rejects a push outright rather
than warning about it.

The models are also being re-cut: split into subsystems, given simplified
collision proxies, and decimated for Quest. Committing 700 MB of art that is
about to be replaced would burn the repository's size budget on a version with
no future.

## What you can work on today

Everything except the rig visuals:

- The whole `Assets/Scripts/` tree — networking, state, presentation, interaction
- `Assets/Scenes/Connection Test.unity`, which is **self-contained** and
  references no art. It builds and runs on a headset as-is, so the engine link
  can be developed and tested against the mock engine with no models at all.
- `tools/mock_engine/` and the protocol in `docs/protocol.md`

## Getting the art

Ask Abdul for the current drop. Copy it into `Assets/_Prefabs/Environments/`
preserving folder structure, and let Unity import.

## When the art is committed

It must be committed **from Abdul's machine**, not re-added from a copy
elsewhere.

Unity identifies every asset by a GUID stored in its `.meta` file, and the scene
refers to meshes by those GUIDs — roughly 2,600 references. The `.meta` files
still exist on Abdul's disk, so committing from there preserves the GUIDs and
the scene links up. Re-importing the models on a different machine generates
fresh GUIDs, and every one of those references breaks.

At that point the size problem still has to be solved. Realistic options:

- **Git LFS** — works, but GitHub's free tier is 1 GB storage and 1 GB/month
  bandwidth. Roughly 700 MB of art nearly fills storage, and each teammate's
  clone spends about that much bandwidth, so a paid data pack becomes necessary
  quickly.
- **Azure DevOps Repos** — free unlimited LFS, otherwise the same Git workflow.
- **Unity Version Control** — built for large binary game assets and integrates
  with the Editor.

The decimation work should cut the total substantially, so it is worth deciding
after that lands rather than before.

## Excluded paths

See `.gitignore`. Currently:

```
Assets/_Prefabs/Environments/**/*.glb
Assets/_Prefabs/Environments/3d/
Assets/_Prefabs/Environments/textures/
Assets/Source Files/Models/Backgrounds/
```
