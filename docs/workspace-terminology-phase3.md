# Phase 3 — Référentiel terminologique par workspace

## Diagnostic et architecture

Les points d'entrée existants permettaient de réutiliser la résolution serveur du workspace, le profil éditorial, les identités graphiques approuvées, le pipeline commun de brouillons et les concepts visuels protégés des Phases 1 et 2. Les instructions au modèle seules ne garantissaient pas une graphie. La Phase 3 ajoute un contrôle déterministe commun, sans second générateur et sans règle spécifique à une marque.

Le catalogue est chargé côté serveur. Les données de terminologie fournies par un appelant ne remplacent pas ce catalogue. Seuls les termes validés sont applicables. Les correspondances sont recherchées dans les entrées éditoriales et les références de connaissance sélectionnées, puis contrôlées dans le titre, contenu, appel à l'action et brief retournés. Les références originales ne sont jamais réécrites. La normalisation concerne uniquement les nouveaux brouillons et les valeurs transitoires du concept ; aucun contenu historique n'est réécrit.

## Schéma et sécurité

Migration `20261122000600_AddWorkspaceTerminology`, après `20261121233151_AddWorkspaceIdentities` :

- `WorkspaceTerms` : graphie exacte, clé de recherche distincte, alias JSON, catégorie, langue, contexte, note, source, statut, révision, acteurs et dates de création/modification/vérification.
- `WorkspaceTermRevisions` : instantané immuable pour chaque révision ; clé étrangère composite workspace/terme, suppression restreinte, unicité workspace/terme/révision.
- `AiGenerations.TerminologyEvidenceJson` nullable : catalogue utilisé, termes/révisions et avertissements. Les audits historiques restent sans preuve terminologique.
- Unicité workspace/clé canonique normalisée/langue/contexte. La révision participe au contrôle de concurrence. Les écritures et leur historique sont transactionnels.

Filtres EF et garde d'écriture workspace existants, authentification et CSRF conservés. Les routes de lecture utilisent `WorkspaceMember`, les mutations `WorkspaceManageSocial` (Owner/Admin). Editor et Viewer consultent seulement le registre. Le serveur résout l'acteur depuis Identity ; aucune attribution navigateur n'est acceptée. Une ressource d'un autre workspace retourne 404.

La création est toujours proposée. Une validation explicite exige une source non vide et porte sur une révision. Toute modification remet le terme au statut proposé et efface sa vérification courante, tout en conservant l'historique. Le retrait conserve également l'historique. Aucun seed, aucune reconnaissance officielle automatique, aucune approbation rétroactive.

## Validation déterministe et générations

`TerminologyValidator` distingue les indices du texte original et sa représentation NFC de recherche. Il respecte les frontières Unicode, accents, casse et symboles ®/™. La graphie canonique enregistrée est conservée exactement. Les variantes imbriquées privilégient la correspondance complète la plus longue ; les correspondances ambiguës ou chevauchantes sont conservées avec avertissement. Un contexte d'utilisation spécifique exige une revue humaine au lieu d'une déduction automatique.

Les URL, destinations de liens Markdown, citations délimitées, blocs de citation et blocs de code reconnus sont protégés. Les termes inconnus restent inchangés et non vérifiés. Aucun terme absent n'est inséré par le normaliseur. Les instructions générales de non-invention et de non-affiliation restent appliquées au fournisseur.

Les concepts contiennent les preuves et avertissements. L'empreinte inclut celle du catalogue validé complet : modifier, valider ou retirer une règle applicable au catalogue invalide les anciens concepts avant l'appel d'image. La recette est versionnée ; les jetons des anciennes recettes doivent être renouvelés. Les champs d'une identité graphique approuvée sont contrôlés en lecture seule, jamais substitués. Les métadonnées d'image conservent la preuve employée. La confirmation humaine du concept reste obligatoire.

## Interface et endpoints

Le référentiel est dans l'écran existant des identités du workspace, également disponible lorsque les visuels sont désactivés. Il affiche les statuts, révisions et dates de vérification. Une modification non enregistrée bloque la validation de l'ancienne révision.

- `GET /api/workspace-terminology`
- `POST /api/workspace-terminology`
- `PUT /api/workspace-terminology/{id}`
- `POST /api/workspace-terminology/{id}/validate` ou `/retire`, corps `{ "revision": n }`
- `GET /api/posts/{postId}/terminology-check` : contrôle gratuit et sans écriture de la publication enregistrée.

Le bouton de contrôle dans l'éditeur nécessite d'enregistrer les modifications en cours. Les avertissements apparaissent également dans le résultat de génération et la préparation du concept, avant la confirmation payante.

## Fichiers de Phase 3

Nouveaux fichiers principaux :

- `Redeemer.SocialFlow.Domain/Entities/WorkspaceTerm.cs`
- `Redeemer.SocialFlow.Application/Terminology/Contracts.cs`, `TerminologyValidator.cs`
- `Redeemer.SocialFlow.Infrastructure/Terminology/WorkspaceTerminologyService.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Configurations/WorkspaceTermConfiguration.cs`
- migration `20261122000600_AddWorkspaceTerminology.cs` et son Designer
- `Redeemer.SocialFlow.Api/Controllers/WorkspaceTerminologyController.cs`
- `Redeemer.SocialFlow.Web/src/components/WorkspaceTerminology.tsx`, `PostTerminologyCheck.tsx`
- tests `TerminologyValidatorTests.cs`, `WorkspaceTerminologyTests.cs`, `WorkspaceTerminologyMigrationTests.cs`, `WorkspaceTerminology.test.tsx`.

Intégrations modifiées : `AiGeneration`, `SocialFlowDbContext` et snapshot, injection Infrastructure ; contrats AI `GenerateContentRequest`, `ContentGenerationMetadata`, `GenerateVisualRequest`, `GeneratedVisual` ; `GenerateSocialPostDraft`, `GenerateGroundedContent`, `RedeemerEditorialPolicy`, générateurs OpenAI texte/image ; `VisualConcept`, `PostVisualService`, `VisualsController`, `VisualApiExceptionFilter` ; API frontend `visuals.ts`, `VisualIdentities`, `PostEditor`, `PostVisuals` et son test. Le test de génération API attend désormais le catalogue résolu serveur. Le test historique `PublishingCapabilityMigrationTests` initialise son ancien schéma par SQL paramétré au lieu d'utiliser les colonnes du modèle EF actuel.

Les changements locaux préexistants des Phases 1 et 2 ont été conservés ; toute la liste de `git status` ne correspond donc pas à la seule Phase 3.

## Vérifications automatisées

- 198 tests unitaires ciblés : terminologie, concepts, visuels et génération de brouillons.
- 112 tests d'intégration ciblés : droits Owner/Admin/Editor/Viewer, séparation de deux workspaces, générations simulées éditoriales et documentaires, concepts obsolètes, historique conservé et migration temporaire.
- Vérification complémentaire publication durable/LinkedIn/Facebook : 122 tests réussis au premier passage ; le test historique de migration restant a été corrigé puis relancé isolément avec succès (1/1). Les ensembles peuvent se recouper ; ces nombres ne constituent pas un total de tests distincts.
- 244 tests frontend, 28 fichiers : réussis.
- Build API (`dotnet build Redeemer.SocialFlow.Api --no-restore -m:1`) : réussi, zéro erreur/avertissement.
- Build frontend (`npm run build`) : TypeScript et Vite réussis.
- Contrôle `git -c core.safecrlf=false diff --check` : réussi.

La migration a été exécutée uniquement par les tests sur SQLite temporaire, avec vérification de publications et audits préexistants et absence de changement de modèle non migré. Aucun appel fournisseur réel, aucune publication ni démarrage de l'API/worker connecté n'a été effectué.

Commandes de tests utilisées depuis la racine :

```powershell
dotnet test Redeemer.SocialFlow.UnitTests --no-restore -m:1 --filter "FullyQualifiedName~Terminology|FullyQualifiedName~Visual|FullyQualifiedName~GenerateSocialPostDraft|FullyQualifiedName~OpenAIContent" -v minimal
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~Terminology_|FullyQualifiedName~WorkspaceIdentity_|FullyQualifiedName~Visual|FullyQualifiedName~ConceptRejects|FullyQualifiedName~GenerateDraft|FullyQualifiedName~DraftGeneration|FullyQualifiedName~WorkspaceAiDraft|FullyQualifiedName~LinkedInImagePublicationTests|FullyQualifiedName~FacebookImage" --blame-hang-timeout 60s -v minimal
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~Publishing|FullyQualifiedName~WorkspaceLinkedIn|FullyQualifiedName~WorkspaceFacebook" --blame-hang-timeout 60s -v minimal
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore -m:1 --filter "FullyQualifiedName~PublishingCapabilityMigrationTests" -v minimal
npm --prefix Redeemer.SocialFlow.Web test
npm --prefix Redeemer.SocialFlow.Web run build
```

L'outil EF 10.0.11 signale un décalage mineur avec le runtime 10.0.12 lors de la génération SQL ; la commande réussit et le SQL est strictement additif. VSTest signale aussi un répertoire d'artefacts absent et utilise son répertoire temporaire privé ; cela n'empêche pas les tests de réussir.

## Vérification manuelle sans appel payant

1. Sur une copie locale de test migrée, ouvrir deux workspaces avec des membres de rôles différents.
2. Comme Owner/Admin, créer un terme avec variantes et source : constater le statut proposé, puis valider explicitement. Modifier le terme : constater le retour au statut proposé. Vérifier que Editor/Viewer ne peuvent pas modifier, y compris par requête API.
3. Employer des graphies différentes dans les deux espaces. Vérifier l'absence de partage du registre et le refus des identifiants de l'autre espace.
4. Dans une publication enregistrée, utiliser une variante, une URL contenant la variante et une citation. Cliquer sur le contrôle gratuit ; vérifier les avertissements sans réécriture du post.
5. Préparer un concept sans générer d'image. Vérifier ses graphies et avertissements, puis modifier/retirer une règle validée. L'ancien concept doit être refusé ; renouveler l'aperçu. Ne pas cliquer sur la génération payante lors de cette vérification.
6. Vérifier les publications et visuels historiques en lecture seule. Les tests automatisés couvrent les appels fournisseurs et la sélection avec des doublures.

## Commandes EF Core pour l'utilisateur (non exécutées sur la base de travail)

Depuis la racine du dépôt, PowerShell, avec les SDK/outils du projet installés. Arrêter les processus écrivant dans SQLite avant une sauvegarde. Sauvegarder la base par un mécanisme SQLite cohérent (ou copie hors ligne après arrêt propre, y compris les fichiers WAL/SHM s'ils subsistent). Identifier explicitement la base Development : la factory design-time utilise sinon un chemin relatif, qui ne garantit pas la bonne base.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet build Redeemer.SocialFlow.Api -m:1

# Inspection SQL uniquement : ce script ne migre aucune base.
dotnet ef migrations script 20261121233151_AddWorkspaceIdentities 20261122000600_AddWorkspaceTerminology --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --context SocialFlowDbContext --no-build

# Renseigner un chemin absolu vérifié vers une COPIE DE TEST, jamais la production.
$terminologyTestDb = 'C:\CHEMIN_A_REMPLACER\socialflow-phase3-test.db'
dotnet ef database update 20261122000600_AddWorkspaceTerminology --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --context SocialFlowDbContext --connection "Data Source=$terminologyTestDb" --no-build
```

Après validation de la copie et sauvegarde vérifiée, l'utilisateur peut exécuter la même commande `database update` avec le chemin absolu de sa base Development choisie. Cette action n'a pas été effectuée ici. Si la base est antérieure à la Phase 2, EF applique aussi les migrations intermédiaires ; les inspecter avant exécution. Aucun changement d'environnement n'autorise implicitement une base de production.

Retour arrière : privilégier le retour à la version applicative précédente en laissant les ajouts inutilisés, après test de compatibilité, ou restaurer une sauvegarde cohérente application/base. **Ne pas exécuter `Down` sur des données à conserver** : il supprime les nouvelles tables et preuves. Aucun retour arrière destructif n'a été exécuté.

## Limites explicites

- « Validé » signifie validé par un administrateur de cet espace sur la base d'une source renseignée. Aucune vérification externe ou conclusion juridique automatique ; aucune affiliation/certification déduite.
- Langue et contexte sont conservés, sans traduction ni identification sémantique automatique. Un contexte renseigné bloque les substitutions automatiques ; des alias de langues différentes doivent être évalués humainement. Les collisions ne sont jamais résolues par intuition.
- Un extrait copié sans délimiteur n'est pas identifiable comme citation ; les références documentaires structurées restent intactes. Les protections syntaxiques ne remplacent pas une révision humaine du sens.
- Limite explicite de 100 termes validés, 10 alias par terme, 200 caractères par appellation/alias ; pas de troncature silencieuse du catalogue.
- L'audit des révisions est persisté mais aucune nouvelle interface dédiée de navigation de tout l'historique n'est ajoutée.
- Le catalogue d'un brouillon est celui capturé avant l'appel fournisseur. Les changements simultanés restent traçables par version ; ils ne réécrivent pas le résultat. Le contrôle des concepts a lieu avant l'appel image, sans annuler un appel déjà engagé.
- Les pixels d'une image IA ne garantissent pas une graphie exacte. La composition textuelle déterministe ultérieure est recommandée mais non implémentée dans cette phase. Aucun logo fourni n'est modifié.
