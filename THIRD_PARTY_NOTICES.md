# Third-party notices

This file documents the source-level provenance and principal third-party
components of AllyAutoTDP V1. It is not a substitute for the complete
license texts included with those components.

## AllyAutoTDP

Copyright © 2026 RM

AllyAutoTDP V1 is licensed under the GNU General Public License, version 3,
only (`GPL-3.0-only`). The complete license text is provided in `LICENSE`.

Some technical portions of AllyAutoTDP were adapted or derived from
mechanisms studied in G-Helper during development. The adaptations concern
the following areas:

- ASUS ACPI / ATKACPI interface;
- AMD ADL;
- FrameMetrics;
- PMLog/ASIC power reading;
- AutoTDP logic;
- ASUS A0/A3/C1 power-limit writes;
- ASUS mode reapplication;
- AC/battery power-source detection.

More specifically, the studied and adapted mechanisms include AMD adapter
selection, the FrameMetrics Start/Get/Stop lifecycle, PMLog ASIC power-sensor
selection, the +1 W / eight-valid-high-FPS-sample AutoTDP control rule, the
A0/A3/C1 write order, and performance-mode reapplication through RestoreMode.

AllyAutoTDP independently reimplements its minimal ADL P/Invoke layer, the
single-owner ADL session and cleanup, metric result types, dynamic AC/battery
limits, and the console-independent AutoTDP engine. G-Helper's FPS-error to
zero behavior, global application architecture, profiles, USB/barrel
handling, GUI, tray, and other unrelated features were not copied.

This notice records the technical provenance transparently. It does not
claim that a complete G-Helper file was copied into AllyAutoTDP, and it does
not assign a single global copyright holder to G-Helper-derived material.

## G-Helper

- Project: G-Helper
- Official repository: <https://github.com/seerge/g-helper>
- Reference branch: `main`
- Reference commit: `682f87f2c277049fb56232d31d7c95144e1cf73e`
- Upstream license observed: GNU General Public License version 3 (GPLv3)

The upstream materials consulted for AllyAutoTDP do not explicitly establish
whether G-Helper is licensed as `GPL-3.0-only` or `GPL-3.0-or-later`. This
notice therefore makes no such claim.

The complete G-Helper checkout is not redistributed in the AllyAutoTDP
source repository. The reference commit and official URL are recorded here
so that the provenance statement is identifiable and reproducible.

Any future direct source copying or adaptation must be identified in the
affected notices before distribution.

## System.Management

- Version: `8.0.0`
- License: MIT
- Attribution: .NET Foundation / Contributors, according to the official
  package notices.

## System.CodeDom

- Version: `8.0.0`
- License: MIT
- Attribution: .NET Foundation / Contributors, according to the official
  package notices.

The exact Microsoft/.NET/Windows SDK license and notice files for components
redistributed in a future self-contained .NET 8.0.30 binary build will be
included with the corresponding GitHub Release. They are intentionally not
copied from an installed SDK during this source-repository preparation pass.
