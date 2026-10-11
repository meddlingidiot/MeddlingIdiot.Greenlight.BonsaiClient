# MeddlingIdiot.Greenlight.BonsaiClient

A bonsai tree on your desktop that grows with every passing build.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK. Unlike the other clients it remembers, so the tree is a record of your builds over time,
not just their current state:

- **Every passing build grows a leaf.** Once the pads already on the tree are mostly full, it
  grows a new branch. A fresh install starts as a sapling and grows into a full
  informal-upright bonsai over a few hundred green builds.
- **When a build breaks**, a handful of leaves flush red, tremble and drop into the pot and
  onto the desk.
- **While it stays broken, the tree wilts.** The leaves go yellow, then brown, the branches
  droop, and leaves keep falling. Left broken for a working day, the tree is brown and mostly
  bare. Once the build is fixed the tree recovers its colour over a couple of hours, but its
  lost leaves only come back as new builds pass.
- **A pull request waiting on you puts a caterpillar on a branch.** It inches along, eats at
  the end, turns round and comes back, and it gets fatter the longer the review waits. When
  you have dealt with it, it flies off as a butterfly.
- **While a build is running**, the breeze picks up and the bud of the next leaf glows on its
  branch.
- **With no Greenlight running**, the tree goes dormant: grey, still, and neither dying nor
  healing.

As with every Greenlight client, there is no Azure DevOps client, no GitHub client, no token
and no polling loop. Everything arrives through the SDK from the Greenlight already running on
the machine.

## Running it

```bash
dotnet run --project Greenlight.BonsaiClient
```

Windows only: the click-through overlay and the work-area maths are Win32.

## Where the tree lives

Two files in `%AppData%\Greenlight.Bonsai`:

- `bonsai.json` holds your settings: where the tree stands, its colours, how fast a broken
  build kills it (`DecayHours`, default 8) and how fast a fixed one heals (`RecoveryHours`,
  default 2). Open it from the tray with **Edit the settings…**.
- `tree.json` is the tree itself: its seed, how many passing builds it has seen, how many
  leaves it has and how healthy it is. Delete it to plant a new tree. A file that will not
  parse is renamed to `tree.json.unreadable` rather than overwritten.

The shape comes back from the seed and the leaves fill a fixed order of positions, so those
few numbers are the whole tree.

## Moving it about

Tray → **Move and resize it…**. Drag the tree, drag a corner, or scroll over it. The tick or
Enter keeps the change, and the cross or Escape puts it back.

## How it is put together

| File | What it is |
| --- | --- |
| `App.cs` | The Greenlight integration: snapshots go to the gardener, the colour goes to the scene. |
| `BonsaiTree.cs` | The tree's shape from a seed: trunk, branches, pads, and every leaf position in growth order. |
| `BonsaiLife.cs` | `Gardener`, which counts passing builds, sheds leaves on a break, and wilts or heals the tree over time. |
| `BonsaiScene.cs` | What is on screen this frame: sprouting, turning and falling leaves, caterpillars, butterflies, the breeze. |
| `BonsaiCanvas.cs` | The drawing. Every shape is code; there are no image files. |

`BonsaiTree`, `Gardener` and `BonsaiScene` have no Avalonia dependency, so a month of builds
can run in a unit test.
