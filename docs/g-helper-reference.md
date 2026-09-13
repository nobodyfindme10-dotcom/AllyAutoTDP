# Référence G-Helper

## Référence upstream

- Projet : G-Helper
- URL officielle : <https://github.com/seerge/g-helper>
- Branche de référence : `main`
- Commit exact : `682f87f2c277049fb56232d31d7c95144e1cf73e`
- Licence upstream constatée : GNU GPL version 3 (GPLv3)

Les éléments upstream consultés ne déclarent pas explicitement la variante
SPDX `GPL-3.0-only` ou `GPL-3.0-or-later`. AllyAutoTDP ne tranche donc pas
cette variante pour G-Helper.

## Usage pendant le développement

G-Helper a été utilisé comme référence technique pendant le développement de
la V1. Certaines portions techniques d’AllyAutoTDP ont été adaptées ou
dérivées de mécanismes étudiés dans ce projet, notamment :

- l’interface ASUS ACPI / ATKACPI ;
- AMD ADL et la sélection d’adaptateur ;
- le cycle de vie FrameMetrics ;
- la lecture PMLog/ASIC power ;
- la logique de contrôle AutoTDP ;
- l’écriture des limites ASUS A0/A3/C1 ;
- la réapplication du mode ASUS ;
- la détection secteur/batterie.

Cette description concerne des mécanismes techniques et ne prétend pas
qu’un fichier G-Helper complet a été copié.

## Distribution

Le checkout complet de G-Helper n’est pas redistribué dans le dépôt source
AllyAutoTDP. Aucun chemin local vers un ancien checkout n’est requis ou
conservé dans cette documentation. La provenance et les licences sont
également récapitulées dans `THIRD_PARTY_NOTICES.md` et
`docs/licensing.md`.
