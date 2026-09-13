# Tests matériels V1

Cette page décrit une procédure réutilisable de validation matérielle ; elle
ne constitue pas un relevé détaillé des résultats.

La V1 a déjà été physiquement validée sur un ASUS ROG Ally Z1 Extreme (RC71L),
avec Armoury Crate SE installé et actif. Ce modèle est le seul matériel
physiquement validé pour la V1.

Le libellé `REQUIRES REAL HARDWARE VALIDATION` indique que toute exécution
de cette procédure requiert un appareil réel ; il ne signifie pas que la
validation V1 reste à faire. Les tests logiciels V1 ne simulent aucun succès
ADL ou ACPI.

## Préparation

1. Préparer une version self-contained win-x64 : extraire l'archive de la
   GitHub Release V1 lorsqu'elle est disponible, ou produire un build local
   avec la commande de publication indiquée dans le README.
2. Copier les fichiers extraits ou le dossier de sortie du build local sur la
   ROG Ally.
3. Vérifier que `%LocalAppData%\AllyAutoTDP\` reçoit `config.json` et les logs.
4. Ne créer aucune tâche d'autostart pour ce test : elle est hors périmètre.
5. Conserver Armoury Crate SE actif pour les essais de coexistence.

L'application démarre dans le tray. Ouvrir la fenêtre avec un clic gauche sur
l'icône ; fermer avec `X` doit seulement la masquer.

## Configuration et profils

- vérifier la création de `config.json` dans LocalAppData ;
- vérifier les limites générales batterie 6–25 W et secteur 6–30 W ;
- vérifier qu'une valeur hors bornes est refusée par l'interface ;
- créer un profil depuis l'application détectée ;
- ajouter manuellement un EXE sans le lancer ;
- vérifier qu'un profil ne contient aucun champ TDP ;
- modifier puis supprimer un profil ;
- vérifier qu'une suppression du profil actif restaure avant suppression.

## Démarrage et arrêt AutoTDP

- lancer un jeu possédant un profil et vérifier l'état `Actif` ;
- vérifier FPS, TDP, cible et source dans la fenêtre, sans overlay ;
- désactiver `AutoTDP` et vérifier l'arrêt/restauration de la session ;
- réactiver puis vérifier que le prochain jeu profilé peut démarrer ;
- quitter par le menu tray et vérifier l'ordre d'arrêt complet ;
- provoquer un échec de restauration et vérifier que l'application reste
  ouverte dans l'état `Restauration` ou `Erreur`.

## Source d'alimentation

Sur batterie :

- vérifier la plage effective 6–25 W ;
- débrancher au-dessus de 25 W et vérifier le clamp immédiat à 25 W ;
- vérifier que la source affichée devient `Batterie`.

Sur secteur :

- vérifier la plage effective 6–30 W ;
- brancher le secteur et vérifier que le TDP courant n'est pas envoyé
  automatiquement à 30 W ;
- vérifier que l'algorithme peut ensuite augmenter le TDP selon les FPS.

## Armoury Crate et métriques

1. Tester Silent, Performance puis Turbo dans Armoury Crate.
2. Comparer les changements A0/A3/C1 avec les timestamps du log.
3. Vérifier StartFPS et StopFPS.
4. Vérifier le refresh de 2 secondes sans modifier son comportement.
5. Noter chaque variation externe tant que son origine n'est pas prouvée.
6. Tester lancement, fermeture, Alt-Tab vers une application inconnue et
   transition d'un jeu profilé vers un autre.

## Rapport

Consigner pour chaque essai :

- date et heure ;
- mode Armoury Crate ;
- source secteur/batterie ;
- profil et cible FPS ;
- plage générale active ;
- TDP courant et plafond effectif ;
- événements A0/A3/C1 et changements externes ;
- résultat de la restauration ;
- chemin et extrait du log sous LocalAppData.

La CI et les tests fakes ne remplacent pas la validation physique de la V1 indiquée ci-dessus.
