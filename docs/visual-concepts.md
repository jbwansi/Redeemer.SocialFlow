# Concepts visuels — cohérence thématique

Le parcours commun des visuels prépare désormais un concept avant toute génération.
Le moteur, le modèle configuré, les candidats, leur sélection humaine et les mécanismes
de publication existants sont conservés. Aucune migration n'est nécessaire.

## Parcours et garanties

1. Enregistrer le titre, le contenu et le brief de la publication.
2. Choisir/renseigner l'identité graphique puis « Préparer le concept (sans coût) ».
3. Relire le sujet, le message enregistré, la scène indicative, le brief, le style et
   les exclusions. Cocher la validation humaine.
4. « Générer un visuel » déclenche alors l'appel payant. Le résultat reste un candidat.

`POST /api/posts/{id}/workspace-visuals/concept` est protégé par WorkspaceEdit,
l'authentification et le CSRF existants. Le serveur charge la publication dans le
workspace actif. Un titre et un contenu comportant du texte alphanumérique sont
obligatoires ; un brief seul ne suffit pas. Le brief transmis doit correspondre au
brief enregistré. Un brief absent reçoit une orientation neutre fondée sur le sujet.
Le titre et le contenu libres du navigateur ne sont jamais utilisés.

La préparation ne résout pas IVisualGenerator, n'appelle aucun fournisseur et n'écrit
aucune donnée. Le concept est déterministe : il ne prétend pas comprendre ou résumer
sémantiquement le texte. L'utilisateur vérifie le message complet affiché.

Le jeton Data Protection expire après 30 minutes et est lié à l'utilisateur. Il
protège une empreinte SHA-256 de la recette versionnée, du workspace, de la publication,
de sa date de modification, de son titre/contenu, du brief, de la plateforme et de
l'identité effective. `/generate` refuse un jeton absent/altéré/expiré ou une empreinte
périmée (409), avant de résoudre le fournisseur. Le service relit encore le contexte
juste avant son appel. Le parcours historique `/visuals` suit la même préparation.

Priorité du prompt : sécurité, sujet/message enregistrés, brief complémentaire,
puis style et plateforme. Aucun référentiel terminologique n'est simulé : une future
version devra intégrer les termes validés et leur version dans le contexte et l'empreinte.

## Limites

- La garde de contexte est structurelle, pas une preuve automatique de pertinence.
- La scène proposée est une orientation déterministe ; une contradiction sémantique
  complexe nécessite la revue humaine. Le prompt demande d'ignorer un brief contradictoire.
- Un autre éditeur peut modifier la publication pendant l'appel fournisseur : aucune
  transaction n'est conservée pendant cet appel. Le résultat n'est jamais auto-sélectionné.
- Le jeton n'est pas un mécanisme anti-rejeu à usage unique. L'interface retire sa
  validation après un lancement, sans retry automatique ; le serveur conserve les
  limites de concurrence existantes. Les garanties anti-doublon de publication restent inchangées.
- Les anciens visuels et leurs métadonnées ne sont pas modifiés.

## Vérification locale sans dépense

Avec Owner/Editor, ouvrir un brouillon enregistré puis préparer le concept. Observer
uniquement la requête `/concept`. Ne pas cliquer sur Générer lors de cette vérification.
Modifier le contenu localement : la préparation/génération se désactive jusqu'à
l'enregistrement. Modifier la publication dans un deuxième onglet après préparation :
le contrôle automatisé équivalent vérifie un 409 avant toute résolution du fournisseur.
Viewer peut consulter les visuels mais les POST de préparation/génération sont refusés.
Les tests utilisent des fournisseurs simulés et des bases temporaires isolées.

## Fichiers concernés par cette phase

- API : `Controllers/VisualsController.cs`, `Development/Visuals/VisualEndpointsConvention.cs`,
  `Development/Visuals/VisualApiExceptionFilter.cs`.
- Application : `AI/GenerateVisualRequest.cs`, `Visuals/Contracts.cs`,
  `Visuals/PostVisualService.cs`, nouveau `Visuals/VisualConcept.cs`.
- Infrastructure : `AI/OpenAIVisualGenerator.cs`, `Visuals/SqlitePostVisualStore.cs`.
- Web : `src/api/visuals.ts`, `src/components/PostEditor.tsx`,
  `src/components/PostVisuals.tsx`, `server/visualGateway.ts`.
- Tests unitaires : nouveau `Application/VisualConceptTests.cs`,
  `Infrastructure/OpenAIVisualGeneratorTests.cs`.
- Tests d'intégration : nouveau `Api/WorkspaceVisualConceptTests.cs`,
  `Api/WorkspaceVisualAvailabilityTests.cs`, `Visuals/PostVisualIntegrationTests.cs`,
  `Visuals/VisualApiTests.cs`, `Visuals/VisualIdentityLibraryTests.cs`,
  `BusinessApiFactory.cs` (clés Data Protection éphémères, isolées des clés du développeur).
- Tests Web : `src/components/PostVisuals.test.tsx`,
  `src/components/VisualIdentityLibrary.test.tsx`, `src/components/WorkspaceVisualAvailability.test.tsx`.
- Documentation : ce document et `visual-generation.md`.

## Validation automatisée du 22 novembre 2026

- Tests unitaires filtrés `FullyQualifiedName~Visual` : 95 réussis.
- Tests d'intégration filtrés
  `FullyQualifiedName~Visual|FullyQualifiedName~ConceptRejects|FullyQualifiedName~LinkedInImagePublicationTests|FullyQualifiedName~FacebookImage` :
  61 réussis. Incluent aperçu sans résolution du fournisseur, données serveur,
  jetons absents/altérés/expirés, contexte obsolète, identité modifiée, rôles et isolation.
- `npm test` : 234 tests réussis dans 26 fichiers.
- Build API avec `dotnet build Redeemer.SocialFlow.Api/Redeemer.SocialFlow.Api.csproj --no-restore -m:1` : réussi.
- `npm run build` : TypeScript et Vite réussis.
- `git diff --check` : réussi.

Une exécution supplémentaire élargie à toute la classe WorkspaceIsolationTests a
été interrompue ; elle n'est pas comptée comme validée. Le filtre ciblé ci-dessus a
ensuite été exécuté jusqu'au bout avec succès. Aucun appel IA ou publication réelle.
Les migrations des fixtures s'appliquent uniquement à leurs bases temporaires ;
aucune migration nouvelle ni opération sur la base de l'application.
