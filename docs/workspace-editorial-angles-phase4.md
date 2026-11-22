# Phase 4 — Angles éditoriaux par workspace

## Diagnostic complémentaire : section absente

Le composant `WorkspaceEditorialAngles` est bien importé dans `src/pages/VisualIdentities.tsx`. `App` utilise cette page pour Identités visuelles. Dans le parcours authentifié, `WorkspaceShell` monte `App` avec le workspace actif, `localAdministration=false` et `canManageSocial` pour Owner/Admin. Editor/Viewer voient le composant en lecture seule. Le catalogue vide ne masque pas la section ; il permet l'import explicite aux administrateurs.

**Défaut reproduit et corrigé :** les retours anticipés de `VisualIdentities` sur l'attente ou l'erreur de `GET /api/posts/workspace-visuals/status` empêchaient le montage de toutes les sections éditoriales. Trois tests ont échoué avant modification (attente, HTTP 403, HTTP 500). La page monte maintenant les sections workspace indépendamment de ce statut ; seul le rendu de la bibliothèque graphique reste conditionné. Aucun contrôle d'accès n'a changé.

Autre condition volontaire : le parcours « Administration locale · Données historiques » monte la même page avec `workspace=false`. Les angles y sont absents car ils nécessitent un espace actif. Aucun angle global ni contournement n'a été ajouté.

Le composant utilise le client authentifié commun pour `GET /api/workspace-editorial-angles`, avec cookies same-origin, en-tête `X-Workspace-Id` et gestion existante des sessions. Une erreur est affichée dans la section. La migration n'est pas requise pour afficher son titre ; elle est nécessaire pour lire/écrire le catalogue. Avec le backend Phase 4 mais sans table, la requête SQLite échouerait et le filtre existant produirait HTTP 500, titre générique `Visual operation failed`. Un ancien backend sans route pourrait produire 404 ou une réponse d'autorisation selon son routage. Aucune de ces réponses n'a été observée sur la session utilisateur ici.

Vite et la version compilée utilisent le même composant sans condition `DEV` pour les angles. Le bundle présent avant le correctif contenait déjà `Angles éditoriaux de cet espace`. En Development, l'API sert `Redeemer.SocialFlow.Web/dist`, tandis que Vite sert les sources. Le build a été renouvelé avec succès après correction.

Limite du diagnostic actif : aucun processus en écoute sur les ports habituels 5173 et 65474 lors du contrôle. Aucun serveur n'a été démarré, aucune session utilisateur inspectée et aucune base ouverte. Le défaut de rendu est démontré par les tests, mais son attribution à l'écran précis de l'utilisateur reste à confirmer ; le mode historique ou un ancien onglet/bundle restent des possibilités.

Changements de ce correctif uniquement :

- `Redeemer.SocialFlow.Web/src/pages/VisualIdentities.tsx` : suppression de la dépendance des sections éditoriales au statut graphique.
- `Redeemer.SocialFlow.Web/src/pages/VisualIdentities.test.tsx` : 8 tests de page (attente, erreurs, activé/désactivé, catalogue vide/en erreur, lecture seule et mode historique).
- Ce rapport.

Validation : 258 tests frontend réussis dans 30 fichiers ; build TypeScript/Vite réussi ; `git diff --check` réussi. Aucun backend modifié dans ce correctif ; les tests backend de la Phase 4 n'ont pas été relancés pour cette modification de rendu.

Vérification manuelle :

1. Depuis la racine, démarrer le frontend avec `npm --prefix Redeemer.SocialFlow.Web run dev` lorsque l'API locale est disponible selon le lanceur habituel. Aucun serveur n'a été démarré par le diagnostic.
2. Se connecter au parcours normal, ouvrir l'espace souhaité depuis Mes espaces et vérifier l'espace actif. Ne pas utiliser Administration locale pour ces fonctions.
3. Ouvrir Identités visuelles : la section Angles éditoriaux doit rester visible même si le statut graphique échoue. Owner/Admin peuvent gérer ; Editor/Viewer consultent.
4. Pour l'interface servie par l'API, reconstruire si nécessaire avec `npm --prefix Redeemer.SocialFlow.Web run build`, puis recharger complètement l'onglet. Vite doit utiliser le répertoire de ce dépôt.
5. Dans Réseau, examiner séparément `/api/posts/workspace-visuals/status` et `/api/workspace-editorial-angles` : URL, HTTP, présence de `X-Workspace-Id`, sans copier cookies ou tokens. Un catalogue vide doit répondre 200 avec `[]`. Un 500 sur le catalogue nécessite de vérifier le schéma et le backend actif ; ne pas déduire automatiquement une migration absente de tout 500.
6. Si la section entière reste absente malgré un statut graphique 200, vérifier d'abord le mode historique et le bundle servi. La migration ne corrige pas un composant absent de l'interface. Aucune migration automatique n'a été exécutée ; les commandes contrôlées figurent plus bas.

## Architecture et audit ciblé

Le parcours existant utilise `GenerateDraft` → `POST /api/posts/generate-draft` → `GenerateSocialPostDraft`. Ce dernier résout déjà le profil permanent et la terminologie côté serveur, puis appelle le générateur éditorial ou documentaire. Les deux chemins convergent vers la création du post et de son audit `AiGeneration` dans la même sauvegarde.

La Phase 4 réutilise ce parcours. Elle ajoute un identifiant optionnel `EditorialAngleId` au contrat API, un lecteur d'angle côté serveur et un instantané dans l'audit lié au post. Les valeurs d'angle arbitraires envoyées par le navigateur ne sont pas utilisées. Un appel interne ne peut pas remplacer l'angle résolu : le cas d'utilisation écrase toujours la propriété de consignes à partir de l'identifiant.

L'angle reste une intention de rédaction par génération. Il n'écrit jamais dans `WorkspaceEditorialProfile`, les identités graphiques ou le référentiel terminologique.

## Modèle et migration

Migration additive `20261122004120_AddWorkspaceEditorialAngles`, après `20261122000600_AddWorkspaceTerminology` :

- Nouvelle table `WorkspaceEditorialAngles` : Id, WorkspaceId, Revision, Name, Description, Objective, Instructions, CallToAction, Active, DisplayOrder, UpdatedBy et UpdatedAt.
- Clé étrangère vers Workspace avec suppression restreinte ; index WorkspaceId et index composite WorkspaceId/Active/DisplayOrder. Les noms ne sont pas des identifiants et ne portent pas de contrainte d'unicité.
- Revision et WorkspaceId participent au contrôle de concurrence EF ; les mises à jour exigent la révision lue.
- Nouvelle colonne nullable `AiGenerations.EditorialAngleJson`. Elle contient Id, WorkspaceId, révision et les consignes effectivement résolues avant l'appel fournisseur.
- Aucun seed de données existantes, aucune modification de publication historique, aucun changement de données dans la migration Up. Les audits historiques restent à null.

La migration a été testée uniquement sur SQLite temporaire. Le test vérifie les opérations additives, la conservation d'une publication et d'un audit préexistants, l'absence d'angles ajoutés automatiquement et l'absence de modification EF non migrée. La base de travail n'a pas été migrée.

## Catalogue initial et gestion

Les cinq propositions sont dans `EditorialAngles:InitialCatalog` de `appsettings.json`, pas dans les services : Leadership ; Expertise et pédagogie ; Inspiration et valeurs ; Engagement et interaction ; Promotion et activation. Elles peuvent être configurées avant import, selon les mécanismes de configuration habituels ASP.NET Core.

Un Owner/Admin peut importer ce catalogue dans un workspace vide par une action explicite dans l'écran Identités. L'import transactionnel copie les propositions en nouvelles lignes avec de nouveaux identifiants. Deux espaces ne partagent donc aucune ligne ni modification. L'import est refusé si l'espace contient déjà des angles, même inactifs. Le catalogue doit contenir entre 1 et 50 modèles ; aucune importation n'a lieu à la création du workspace ou pendant une génération.

Les membres autorisés peuvent également créer leurs propres angles, modifier les consignes, l'ordre et désactiver les angles. Le nom est limité à 200 caractères, les quatre champs textuels à 4000 chacun, l'ordre à 0–10000. Il n'existe pas de route de suppression ; utiliser la désactivation. Une référence introuvable (y compris une ancienne référence supprimée hors de ce parcours) est refusée.

## Permissions, routes et isolation

- `GET /api/workspace-editorial-angles[?activeOnly=true]` : `WorkspaceMember`.
- `POST /api/workspace-editorial-angles` : `WorkspaceManageSocial` (Owner/Admin).
- `PUT /api/workspace-editorial-angles/{id}` : même permission ; la révision attendue est obligatoire dans le corps.
- `POST /api/workspace-editorial-angles/initialize` : même permission ; aucun corps de consignes client.
- Génération : contrôle `WorkspaceEdit` existant, donc Owner/Admin/Editor ; Viewer reste en lecture seule.

Cookies Identity, CSRF, en-tête workspace, filtres EF et gardes d'écriture existants restent en place. Les requêtes de gestion inter-workspaces retournent 404. Une sélection de génération introuvable, inactive ou étrangère retourne 409 avec `EditorialAngleUnavailable`, sans appel fournisseur ni création du post. Les données ne révèlent pas si cet identifiant existe dans un autre espace.

## Génération, historique et concepts

Le formulaire charge uniquement les angles actifs de l'espace courant. « Sans angle » est toujours disponible et reste sélectionné initialement. Une description accompagne le choix. Une erreur de chargement du catalogue facultatif permet de continuer sans angle. Les anciens clients, sans nouvel identifiant, gardent leur fonctionnement.

Le générateur Responses reçoit l'angle comme données séparées, jamais concaténées aux instructions système. La hiérarchie explicite est : sécurité/autorisations ; sujet et objectif réel ; terminologie validée ; profil permanent ; angle ; plateforme. Une consigne contradictoire doit être ignorée et signalée dans Warnings. L'angle ne constitue aucune preuve factuelle et ne peut autoriser une invention.

Les parcours éditorial et documentaire reçoivent le même angle résolu, avec le même profil permanent et les règles terminologiques. Le contrôle déterministe terminologique après génération reste inchangé. SDK, modèle, schéma strict et tentatives de validation ne changent pas.

L'instantané est enregistré dans l'audit rattaché par SocialPostId et WorkspaceId et retourné dans les métadonnées de génération. Modifier ou désactiver ensuite un angle ne réécrit ni cet audit ni le post. Une modification pendant un appel déjà engagé ne change pas rétroactivement les consignes capturées ; une prochaine génération relit l'état courant.

Les images s'appuient toujours sur le contenu enregistré. Les concepts ne relisent pas les consignes vivantes d'un angle déjà utilisé. Une modification du catalogue d'angles seule ne périme donc pas un concept lié à un texte inchangé : elle ne change pas son contexte réel. Les empreintes du contenu, des identités et du catalogue terminologique, les jetons protégés et la confirmation humaine des Phases 1–3 restent applicables.

## Fichiers

Nouveaux :

- `Redeemer.SocialFlow.Domain/Entities/WorkspaceEditorialAngle.cs`
- `Redeemer.SocialFlow.Application/AI/EditorialAngle.cs`
- `Redeemer.SocialFlow.Infrastructure/Editorial/WorkspaceEditorialAngleService.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Configurations/WorkspaceEditorialAngleConfiguration.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Migrations/20261122004120_AddWorkspaceEditorialAngles.cs` et Designer
- `Redeemer.SocialFlow.Api/Controllers/WorkspaceEditorialAnglesController.cs`
- `Redeemer.SocialFlow.Web/src/components/WorkspaceEditorialAngles.tsx` et `.test.tsx`
- `Redeemer.SocialFlow.IntegrationTests/Api/WorkspaceEditorialAngleTests.cs`
- `Redeemer.SocialFlow.IntegrationTests/Visuals/WorkspaceEditorialAngleMigrationTests.cs`
- Ce rapport.

Modifiés :

- API : `Contracts/GenerateDraftRequest.cs`, `Controllers/PostsController.cs`, `Development/Visuals/VisualApiExceptionFilter.cs`, `appsettings.json`.
- Application : `AI/GenerateContentRequest.cs`, `AI/ContentGenerationMetadata.cs`, `SocialPosts/GenerateSocialPostDraft.cs`.
- Domain : `Entities/AiGeneration.cs`.
- Infrastructure : `DependencyInjection.cs`, `Persistence/SocialFlowDbContext.cs`, snapshot EF, `AI/OpenAIContentGenerator.cs`, `AI/RedeemerEditorialPolicy.cs`.
- Web : `src/api/posts.ts`, `src/components/GenerateDraft.tsx`, `src/pages/VisualIdentities.tsx`.
- Tests : `Redeemer.SocialFlow.UnitTests/Infrastructure/OpenAIContentGeneratorTests.cs`.

## Tests et builds

- Nouveaux tests d'intégration : 9 réussis, avec SQLite temporaire et fournisseurs simulés. Cinq choix, import explicite/deux espaces, droits, ID inconnu/inactif/étranger, concurrence par révision, instantané immuable après modification, profils et terminologie préservés dans les deux modes.
- Tests unitaires ciblés des Phases 1–4 : 199 réussis, dont séparation des consignes d'angle et de la politique fournisseur.
- Intégration de non-régression des Phases 1–3 et publication durable LinkedIn/Facebook : 224 tests réussis. Cette sélection inclut aussi le test de migration Phase 4 ; ne pas additionner les sélections comme des tests tous distincts.
- Frontend complet : 250 tests réussis dans 29 fichiers ; import explicite, lecture seule, désactivation, sélection et absence d'angle, panne du catalogue.
- Builds API et frontend : réussis.
- `git diff --check` : réussi. Le build API final ne comporte aucun avertissement ni erreur. VSTest utilise son répertoire temporaire privé faute de variable de répertoire d'artefacts, sans échec de test.

Commandes de vérification :

```powershell
dotnet test Redeemer.SocialFlow.UnitTests --no-restore -m:1 --filter "FullyQualifiedName~Terminology|FullyQualifiedName~Visual|FullyQualifiedName~GenerateSocialPostDraft|FullyQualifiedName~OpenAIContent" -v minimal
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~Angles_|FullyQualifiedName~WorkspaceEditorialAngleMigrationTests" --blame-hang-timeout 60s -v minimal
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~Terminology_|FullyQualifiedName~WorkspaceIdentity_|FullyQualifiedName~Visual|FullyQualifiedName~ConceptRejects|FullyQualifiedName~GenerateDraft|FullyQualifiedName~DraftGeneration|FullyQualifiedName~WorkspaceAiDraft|FullyQualifiedName~Publishing|FullyQualifiedName~WorkspaceLinkedIn|FullyQualifiedName~WorkspaceFacebook" --blame-hang-timeout 60s -v minimal
dotnet build Redeemer.SocialFlow.Api --no-restore -m:1
npm --prefix Redeemer.SocialFlow.Web test
npm --prefix Redeemer.SocialFlow.Web run build
git -c core.safecrlf=false diff --check
```

## Migration Development à exécuter soi-même

Arrêter les processus écrivant dans SQLite, sauvegarder la base de manière cohérente et vérifier la restauration sur une copie. Ne jamais recopier uniquement le fichier principal d'une base ouverte avec WAL actif. Identifier le chemin absolu de la base : la factory design-time utilise sinon un chemin relatif, qui peut désigner une autre base.

Depuis la racine du dépôt, PowerShell :

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet build Redeemer.SocialFlow.Api -m:1

# Inspection SQL, sans application à une base.
dotnet ef migrations script 20261122000600_AddWorkspaceTerminology 20261122004120_AddWorkspaceEditorialAngles --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --context SocialFlowDbContext --no-build

# Remplacer par un chemin absolu vérifié vers une COPIE LOCALE DE TEST.
$editorialAnglesDb = 'C:\CHEMIN_A_REMPLACER\socialflow-phase4-test.db'
dotnet ef database update 20261122004120_AddWorkspaceEditorialAngles --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --context SocialFlowDbContext --connection "Data Source=$editorialAnglesDb" --no-build
```

Après validation et sauvegarde, l'utilisateur peut réexécuter `database update` avec le chemin de sa base Development choisie. Cette opération n'a pas été exécutée sur la base de travail. Si la base est antérieure à la Phase 3, inspecter aussi les migrations intermédiaires qu'EF appliquera. Le script SQL a été généré et vérifié ; l'outil EF 10.0.11 signale seulement qu'il est plus ancien que le runtime 10.0.12.

Retour arrière : préférer une restauration cohérente application/base ou une version applicative compatible laissant les ajouts inutilisés. Le Down supprimerait la table d'angles et les instantanés : ne pas l'exécuter sur des données à conserver. Aucun Down n'a été exécuté.

## Validation locale sans coût

1. Sur une copie migrée, ouvrir Identités comme Owner/Admin. Vérifier que le workspace ne reçoit pas d'angles automatiquement.
2. Importer le catalogue, vérifier les cinq propositions, puis modifier la description, l'ordre et l'activation d'un angle. Créer aussi un angle personnalisé.
3. Ouvrir un second workspace : vérifier l'absence de ces lignes ; son propre import produit un catalogue indépendant.
4. Ouvrir le formulaire de génération comme Editor : vérifier « Sans angle », les seuls angles actifs et leurs descriptions. Ne pas soumettre une génération réelle ; les tests automatisés couvrent les appels simulés et leurs métadonnées.
5. Comme Viewer, vérifier la consultation et l'absence de mutations ; les tests API couvrent également le refus de génération.
6. Dans un formulaire déjà ouvert, désactiver ailleurs l'angle sélectionné. Les tests vérifient que la soumission renvoie 409 avant tout appel IA. Choisir sans angle ou rouvrir le formulaire pour actualiser la liste.
7. Vérifier en lecture seule les publications et visuels historiques. Aucun changement de permission Meta, de worker, de destination ou de pipeline de publication n'est nécessaire.

## Limites

La compatibilité sémantique de consignes libres avec des faits ou des règles permanentes ne peut pas être prouvée par un test de chaîne de caractères. La politique impose la priorité et les avertissements, tandis que la vérification terminologique reste déterministe ; une relecture humaine demeure nécessaire. Les tests vérifient les données et la hiérarchie transmises au fournisseur, sans prétendre garantir toutes les productions d'un modèle réel.

La bibliothèque conserve la révision courante et l'audit des dernières modifications ; les consignes utilisées sont conservées dans chaque génération, sans nouveau journal complet des révisions inutilisées. L'instantané est disponible dans l'audit et les métadonnées de génération, sans nouvelle vue d'historique dans la fiche publication. La suppression physique n'est pas proposée. Aucun appel IA réel, publication, déploiement ou commit automatique n'a été effectué.
