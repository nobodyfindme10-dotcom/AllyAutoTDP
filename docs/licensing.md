# Licensing and provenance V1

## AllyAutoTDP

AllyAutoTDP V1 is licensed under the GNU General Public License version 3,
only (`GPL-3.0-only`).

Copyright © 2026 RM

The complete, unmodified GPLv3 license text is in the repository root
`LICENSE`. This document records provenance; it does not replace that text.

## G-Helper provenance

Some technical portions present in V1 were adapted or derived from
mechanisms studied in G-Helper during development. The upstream project is
available at <https://github.com/seerge/g-helper>; the reference commit was
`682f87f2c277049fb56232d31d7c95144e1cf73e` on the `main` branch.

The adapted or derived technical areas are:

- ASUS ACPI / ATKACPI interface;
- AMD ADL and adapter handling;
- FrameMetrics lifecycle;
- PMLog/ASIC power reading;
- AutoTDP control logic;
- ASUS A0/A3/C1 power-limit writes;
- ASUS mode reapplication;
- AC/battery power-source detection.

G-Helper is identified upstream as GPLv3. Its upstream materials do not
explicitly state the exact `GPL-3.0-only` or `GPL-3.0-or-later` variant, so
AllyAutoTDP documentation does not assert either variant for G-Helper.

AllyAutoTDP is not presented as a wholesale copy of G-Helper: no complete
G-Helper file or full checkout is redistributed in this source repository.
The adaptations are documented here and in `THIRD_PARTY_NOTICES.md`.

## Other source-level components

The principal .NET packages identified for the source project are:

- `System.Management` `8.0.0`, MIT, .NET Foundation / Contributors;
- `System.CodeDom` `8.0.0`, MIT, .NET Foundation / Contributors.

Their exact package notices remain subject to the official notices shipped
with the package and any future binary distribution.

## Source repository and binary release

This pass prepares the public source repository. It does not finalize the
notices for a self-contained binary distribution. The future V1 release must
include the exact applicable notices for .NET 8.0.30, the WindowsDesktop
runtime, the Windows SDK, `D3DCompiler_47_cor3.dll`, and the redistributed
System.Management and System.CodeDom components.
