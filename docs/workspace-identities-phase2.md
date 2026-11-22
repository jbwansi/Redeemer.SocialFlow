# Phase 2 — Identités indépendantes par workspace

## Schéma et sécurité

Migration : `20261121233151_AddWorkspaceIdentities` (SocialFlowDbContext uniquement).
Son `Up` contient exclusivement deux créations de tables et trois créations d'index.
Aucun UPDATE, DELETE, seed, changement de colonne existante ou approbation rétroactive.

- `WorkspaceEditorialProfiles` : clé primaire et FK `WorkspaceId`, révision de concurrence,
  nom d'organisation (200 caractères), mission, valeurs, positionnement, publics,
  ton, thèmes, règles et formulations à éviter (4000 caractères par champ facultatif).
- `VisualIdentityApprovals` : identifiant, workspace, identité, révision approuvée,
  copie JSON des champs et références de logo, auteur authentifié, date UTC.
  Index unique `(WorkspaceId, IdentityId, Revision)`. FK composite vers l'identité du
  même workspace, suppressions restreintes. L'application interdit de modifier ou
  supprimer une preuve existante. Les modifications de bibliothèque restent possibles :
  elles produisent une nouvelle révision sans approbation.

Les deux tables utilisent les filtres et gardes d'écriture workspace existants.
Le profil est facultatif. Le profil courant est versionné ; il n'y a pas de journal
des anciennes versions éditoriales dans cette phase. Les approbations graphiques,
leurs copies et les fichiers de logos historiques sont conservés.

Les GET utilisent `WorkspaceMember`. Les modifications du profil, de la bibliothèque
workspace et les approbations utilisent la permission existante `WorkspaceManageSocial`
(Owner/Admin). Editor conserve la génération avec `WorkspaceEdit` ; Viewer consulte.
Aucun rôle n'a été élargi. Les protections Identity et CSRF sont inchangées.

## Fonctionnement

Les routes sont sous `/api/workspace-identities` : GET/PUT `editorial`, GET/POST `visual`,
PUT/DELETE `visual/{id}`, POST `visual/{id}/approve`, `/default`, `/logo`, et lecture du logo.
Les anciennes routes locales sont conservées et ne sont pas utilisées par le client workspace.

Le cas d'usage de génération de brouillon résout le profil serveur avant la bifurcation
éditorial/documentaire. Une identité fournie dans un contrat interne par un appelant
est remplacée par celle du workspace. Le fournisseur reçoit le profil comme données,
avec la politique générale neutre, les contraintes de non-invention et le schéma strict.
Sans profil, un avertissement déterministe indique l'absence de personnalisation.
Aucun appel IA supplémentaire n'est ajouté et le modèle configuré reste inchangé.

Pour les images, `identityMode` doit être explicitement `Approved` ou `Temporary` :

- `Approved` exige une identité active du workspace, sa révision exacte et une preuve
  d'approbation de cette révision. Tous les champs sont résolus côté serveur. Une
  substitution du nom, style, couleurs, slogan ou instructions du navigateur est refusée.
- `Temporary` exige un choix explicite, sans `LibraryId` ni révision. Aucun logo officiel
  n'est attaché. L'interface distingue ces paramètres des identités approuvées.

Une identité enregistrée ou active n'est pas forcément approuvée. Un changement de
style, de logo, d'activité ou de défaut incrémente la révision et nécessite une nouvelle
approbation. Désactiver puis réactiver ne restaure pas une approbation ancienne.

Le concept reste préparé sans coût. Son empreinte inclut les champs serveur, révision,
référence de logo et identifiant d'approbation. Le jeton est lié à l'utilisateur et à
son contexte, avec expiration à 30 minutes. La recette passe à
`editorial-concept-v2-workspace-identity` : les anciens concepts doivent être préparés
à nouveau. L'identité graphique est résolue à chaque préparation/envoi, puis la Phase 1
revérifie le contexte éditorial avant l'appel. Le sujet réel reste prioritaire.

Le profil éditorial sert aux nouveaux brouillons. Sa modification ne réécrit pas les
posts existants ; une image continue d'illustrer le contenu enregistré de son post.
Les logos ne sont ni régénérés, ni modifiés, ni envoyés comme images au fournisseur.
Les résultats restent des candidats à sélectionner manuellement.

## Migration : sauvegarde, déploiement préparé et retour arrière

Aucune migration n'a été appliquée à la base de travail ni à une base de production.
Les fixtures automatisées créent leurs propres bases SQLite temporaires. Le test
`WorkspaceIdentityMigrationTests` migre d'abord jusqu'à la version précédente,
conserve une photographie des identités, applique la nouvelle migration, vérifie leur
égalité, l'absence d'approbations/profils initiaux et l'absence d'écart du modèle EF.

Plan pour une intervention ultérieure explicitement autorisée :

1. Identifier le fichier SQLite SocialFlow effectif et arrêter proprement tous ses
   processus écrivains ; ne pas modifier les paramètres ni démarrer les workers.
2. Faire une sauvegarde SQLite cohérente (API de sauvegarde SQLite ou sauvegarde à froid
   après fermeture des connexions, en tenant compte du mode WAL). Ne pas copier seulement
   le fichier principal d'une base encore ouverte. Vérifier la sauvegarde sur une copie.
   Sauvegarder séparément les fichiers de visuels/logos et les configurations nécessaires,
   en conservant les secrets hors dépôt.
3. Vérifier le SQL de migration entre la version précédente et celle-ci ; il ne doit
   contenir que les nouvelles tables/index et l'enregistrement de migration.
4. Appliquer d'abord sur une COPIE de test avec une chaîne de connexion explicitement
   choisie. Vérifier `PRAGMA integrity_check`, `PRAGMA foreign_key_check`, les comptes
   de lignes historiques et la non-approbation des anciennes identités.
5. Après validation distincte, appliquer la même migration au déploiement autorisé,
   puis démarrer uniquement les services prévus. Aucune de ces actions n'est automatisée ici.

Retour arrière privilégié : revenir au binaire précédent en laissant les tables additives
en place ; elles n'altèrent pas les colonnes utilisées auparavant. Conserver/exporter les
nouvelles données. Le `Down` EF généré supprime les deux nouvelles tables : **ne pas
l'exécuter sur une base contenant des profils ou approbations à conserver**. Une restauration
de sauvegarde effacerait aussi les écritures postérieures : elle nécessite une décision
explicite et une sauvegarde préalable de l'état courant. Aucun `Down` exécuté pendant ce travail.

## Vérification manuelle dans deux workspaces

Sur une copie de test migrée, avec workers et publications désactivés :

1. Owner/Admin de A : ouvrir l'écran Identités existant, enregistrer « Organisation A »,
   ton formel et valeurs. Créer une identité bleue ; constater « enregistrée, non approuvée ».
2. Approuver explicitement sa révision enregistrée. Modifier ensuite le style : la nouvelle
   révision n'est plus approuvée. Rétablir/valider les paramètres puis approuver la nouvelle version.
3. Dans B, créer un profil au nom et ton différents et une palette différente. Vérifier
   l'absence des données de A dans les listes et les formulaires de B.
4. Dans un brouillon enregistré de chaque espace, choisir son identité approuvée et préparer
   le concept. Vérifier le nom, le statut et le style. Ne pas cliquer sur Générer pour une
   vérification sans coût. Un autre choix possible est une identité temporaire explicitement cochée.
5. Modifier une identité après préparation : l'ancien envoi est refusé avant le fournisseur
   (vérifié automatiquement avec un générateur simulé). Préparer et confirmer un nouveau concept.
6. Editor : consulter les identités et préparer un concept, sans pouvoir gérer/approuver.
   Viewer : consulter, sans pouvoir générer ou modifier. Vérifier aussi les refus API directs.
7. Espace sans profil : avertissement de politique neutre. Les tests de génération simulée
   vérifient le contexte reçu dans les deux modes et les couleurs envoyées pour chaque espace.

## Limites

La validation humaine reste indispensable pour les faits, la pertinence et les droits sur
les actifs importés. L'approbation est une décision interne, pas une preuve d'affiliation
ou de certification. Pas de référentiel terminologique Phase 3. Pas de retry automatique
payant ni changement du moteur de publication. Une modification concurrente pendant un
appel fournisseur déjà commencé ne peut pas annuler son coût ; aucun candidat n'est auto-sélectionné.

## Fichiers de la Phase 2

Nouveaux fichiers :

- Domain : `Entities/WorkspaceEditorialProfile.cs` (profil et preuve d'approbation).
- Application : `AI/EditorialIdentity.cs` (contrat et lecteur indépendant du fournisseur).
- Infrastructure : `Visuals/WorkspaceIdentityService.cs`,
  `Persistence/Configurations/WorkspaceIdentityConfiguration.cs`,
  `Persistence/Migrations/20261121233151_AddWorkspaceIdentities.cs` et son `.Designer.cs`.
- API : `Controllers/WorkspaceIdentitiesController.cs`.
- Web : `src/components/WorkspaceEditorialProfile.tsx`, `WorkspaceIdentityPicker.tsx`,
  `WorkspaceIdentities.test.tsx`.
- Tests d'intégration : `Api/WorkspaceIdentityTests.cs`, `Visuals/WorkspaceIdentityMigrationTests.cs`.
- Documentation : ce rapport.

Fichiers existants adaptés (les changements locaux de Phase 1 sont conservés) :

- Application : `AI/GenerateContentRequest.cs`, `AI/VisualIdentity.cs`, `AI/GeneratedVisual.cs`,
  `SocialPosts/GenerateSocialPostDraft.cs`, `Visuals/VisualConcept.cs` (recette du jeton).
- Infrastructure : `DependencyInjection.cs`, `Persistence/SocialFlowDbContext.cs`,
  `Persistence/Migrations/SocialFlowDbContextModelSnapshot.cs`, `AI/RedeemerEditorialPolicy.cs`,
  `AI/OpenAIContentGenerator.cs`.
- API : `Controllers/VisualsController.cs` (résolution approuvée et choix temporaire explicite).
- Web : `src/App.tsx`, `src/api/visualIdentities.ts`, `src/api/visuals.ts`,
  `src/components/GenerateDraft.tsx`, `PostVisuals.tsx`, `VisualIdentityLibrary.tsx`,
  `WorkspaceVisualAvailability.test.tsx`, `src/pages/VisualIdentities.tsx`.
- Tests unitaires : `Application/GenerateSocialPostDraftTests.cs`,
  `Infrastructure/OpenAIContentGeneratorTests.cs`.
- Tests d'intégration : `Api/GenerateDraftApiTests.cs`, `WorkspaceVisualAvailabilityTests.cs`,
  `WorkspaceVisualConceptTests.cs` (adaptation du choix temporaire explicite).

## Résultats du 22 novembre 2026

- 170 tests unitaires ciblés réussis : visuels, générateur Responses et cas d'usage de brouillons.
- 103 tests d'intégration ciblés réussis : Phase 1, profils et approbations, migration,
  diagnostics de génération, autorisations, isolation, publications avec image LinkedIn/Facebook simulées.
- 239 tests frontend réussis, dans 27 fichiers.
- Build API : réussi, zéro avertissement et zéro erreur.
- Build frontend TypeScript/Vite : réussi.
- `git diff --check` : réussi (Git annonce seulement des conversions LF/CRLF futures).

Commandes reproductibles depuis la racine :

```powershell
dotnet test Redeemer.SocialFlow.UnitTests --no-restore -m:1 --filter "FullyQualifiedName~Visual|FullyQualifiedName~OpenAIContentGenerator|FullyQualifiedName~GenerateSocialPostDraft"
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~WorkspaceIdentity_|FullyQualifiedName~Visual|FullyQualifiedName~ConceptRejects|FullyQualifiedName~LinkedInImagePublicationTests|FullyQualifiedName~FacebookImage|FullyQualifiedName~GenerateDraft|FullyQualifiedName~DraftGeneration|FullyQualifiedName~WorkspaceAiDraft" --blame-hang-timeout 60s
dotnet build Redeemer.SocialFlow.Api --no-restore -m:1
npm --prefix Redeemer.SocialFlow.Web test
npm --prefix Redeemer.SocialFlow.Web run build
git diff --check
```

Pour vérifier uniquement la migration sur la base temporaire créée par le test :

```powershell
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~WorkspaceIdentityMigrationTests"
```

Aucun appel IA payant, publication réelle, application de migration à la base de travail,
commit, push ou déploiement. Aucun worker connecté aux comptes de l'utilisateur démarré.
