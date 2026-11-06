# Controller models

Models of controllers for OpenXR runtimes that don't provide their own (the runtime's models are always preferred):
Meta's PC runtime (Quest and Rift over Link / Air Link) and Monado with HTC Vive wands. Each folder has the model
for each hand (`left.glb`, `right.glb`, or `none.glb` for both) and `profile.json`, which says which parts of the
model move with which buttons, sticks and triggers.

They are from the `@webxr-input-profiles/assets` package, version 1.0.20 (npm), unchanged:
`oculus-touch-v2`, `oculus-touch-v3`, `meta-quest-touch-plus`, `meta-quest-touch-pro`, `htc-vive`.
Licence: MIT (see `LICENSE.md`), copyright 2019 Amazon.

The package notes that some models portray registered trademarks of the device manufacturers, in order to depict
the physical devices accurately; the licence does not grant permission to use those trademarks in derivative works
or to endorse or promote products. Here they are only used to show players the controllers they are holding.
