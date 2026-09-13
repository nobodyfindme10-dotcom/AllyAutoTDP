# Architecture AllyAutoTDP V1

## Séparation des responsabilités

La logique validée reste indépendante de l'hôte WinForms :

```text
Program
  → AllyApplicationContext
      ├── NotifyIcon + menu sombre
      ├── GlobalHotkeyHost (Ctrl+Alt+T)
      └── QuickPanelCoordinator
            └── une seule QuickPanelForm
                  ├── Contrôle
                  ├── Profils
                  └── Modifier le profil
                        ↓
                  AllyApplicationController
                    ├── ConfigurationStore / GameProfileService
                    ├── GameSessionManager
                    └── RealAutoTdpRuntime
                          ├── AMD ADL / FrameMetrics
                          ├── AutoTdpLoop
                          └── AllyPowerController / AsusModeReapply
```

`QuickPanelForm` est la seule interface utilisateur principale. Les trois
états sont des vues internes ; aucune navigation ne crée une autre fenêtre.
`MainForm`, `SettingsDialog` et les options UI de `RestoreMode` n'existent
plus dans cette architecture. `RestoreMode` reste présent dans les données
et le moteur, sans être exposé à l'utilisateur.

## Navigation et profils

La navbar contient exactement `Contrôle` et `Profils`. Elle est absente de la
vue `Modifier le profil`, dont le header fournit `Retour`.

Deux workflows métier distincts sont conservés :

- `Contrôle → Créer le profil` appelle
  `CreateProfileForCurrentApplication(targetFps)`, persiste immédiatement,
  puis reste dans `Contrôle`.
- `Profils → + Ajouter` ouvre directement le sélecteur `.exe`, appelle
  `ProfileWorkflow`/`AddManualProfile`, persiste, rafraîchit la liste et reste
  dans `Profils`.

Seul un clic volontaire sur une ligne de profil existante ouvre l'éditeur.
Les états affichés viennent de `GameProfile.Enabled` et du snapshot courant ;
aucun booléen UI ne simule l'état métier.

## Design system WinForms

Les valeurs visuelles principales du design system V1 sont :

| Élément | Valeur |
| --- | --- |
| backdrop | `#070A0B` |
| panneau | `#0D1213` |
| surface | `#151C1E` |
| surface active | `#20292B` |
| bordure | `#303B3D` |
| texte principal | `#F0F4F1` |
| texte secondaire | `#A9B4B2` |
| cyan réel/actif | `#68CDD0` |
| ambre consigne/action | `#F1B34B` |
| destructif | `#FF7065` |

`QuickPanelControls` dessine les surfaces, boutons, toggles, lignes de
profils et le stepper FPS. `QuickPanelIcons` dessine localement les icônes
vectorielles inspirées de Tabler ; aucune ressource CDN n'est requise.

Les polices préférées de l'interface sont Commissioner et Fragment Mono. Le
code les utilise lorsqu'elles sont installées et retombe sur `Segoe UI` et
`Consolas` pour rester autonome sur Windows/ROG Ally. Le runtime ne télécharge
aucune police.

La vue Contrôle expose uniquement jeu courant, AutoTDP, Target FPS, FPS réel,
puissance réelle et source/plage. La vue Profils expose le nom utilisateur
des jeux. L'éditeur expose nom, Target FPS, `Enabled`, Supprimer et
Enregistrer.

Les éléments de conception rejetés ne sont pas implémentés : Capteurs, troisième
onglet, instrument de mesure, température, ventilateur, batterie/autonomie,
configuration avancée, impacts ou puissances estimés, métriques inventées,
règles/rails/ticks de calibration, jauges FPS/TDP, menu `...`, chemins et
noms techniques d'exécutables dans l'interface.

## Géométrie et DPI

La géométrie suit quatre étapes séparées : sélection de `Screen`, calcul pur
par `QuickPanelLayout.Calculate`, application des `Bounds`, puis layout
interne. Le panneau utilise exclusivement `Screen.WorkingArea` :

```text
Left   = WorkingArea.Right - Width
Top    = WorkingArea.Top
Width  = largeur logique du panneau 364, adaptée au DPI
Height = WorkingArea.Height
```

Ainsi `Right == WorkingArea.Right` et `Bottom == WorkingArea.Bottom`, sans
offset ASUS ni hauteur hardcodée. `AutoSize` et le layout interne ne peuvent
pas agrandir la fenêtre au-delà de la zone utile.

Les tests vérifient les invariants avec origines non nulles, taskbar dans les
quatre directions, DPI différents, zone étroite et zone invalide. Ils ne
remplacent pas la validation physique sur une ROG Ally.

## Métier stable

`AutoTdpLoop` conserve son tick de 300 ms, son refresh de puissance à 2 s,
son hystérésis et ses écritures ASUS existantes. Les profils activés restent
la condition de démarrage AutoTDP. Les plages sont 6–25 W sur batterie et
6–30 W sur secteur. La restauration et toutes les données internes existantes
restent sous la responsabilité des composants métier.

## Tests et limites

Les tests logiciels utilisent des détecteurs, runtimes et contrôleurs factices.
Ils ne prétendent pas valider ADL, ACPI, Armoury Crate, le tactile ou le
positionnement physique. Ces points nécessitent une validation sur matériel
réel avec Armoury Crate actif.
