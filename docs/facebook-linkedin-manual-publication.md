# Envoi manuel Approved : Facebook et LinkedIn

## Diagnostic

Avant ce correctif, le service workspace, le domaine et l'interface partagée
exigeaient tous `Scheduled` arrivé à échéance, y compris pour LinkedIn.
Le message de planification ne venait donc pas d'une contrainte Meta.
Les deux autres blocages sont indépendants : le drapeau serveur Facebook et
l'absence de connexion autorisée à publier. Une Page connectée `Unverified`
n'est pas une destination autorisée.

## Comportement

- `Approved` : destination explicite, aperçu protégé pendant cinq minutes,
  confirmation puis opération durable ; aucune date ou autorisation automatique
  n'est inventée.
- `Scheduled` futur : attente de l'échéance, y compris pour l'envoi manuel.
- `Scheduled` à échéance : même envoi manuel ; le traitement automatique
  Facebook conserve son consentement explicite et son worker existant.
- Succès : `Published`, identifiant externe enregistré. Échec certain : `Failed`.
  Incertitude : opération `Indeterminate`, aucun renvoi automatique ; un post
  initialement approuvé reste `Approved` mais ne peut plus être envoyé à nouveau.
- Une seule opération par post, même avec deux confirmations distinctes concurrentes.
- Les endpoints locaux historiques `/api/dev/...` conservent leurs règles.

`PublishApproved` est un choix interne du service workspace pour l'envoi manuel,
pas un paramètre libre du frontend. Le store le refuse sans preuve workspace ou
avec un plan automatique. Les transactions, instantanés texte/image, adaptateurs,
chiffrement et confirmations existants sont réutilisés. Aucune migration nécessaire.
Les permissions sont contrôlées avant l'aperçu, au moment de la demande et de la
prise en charge ; la validation en ligne du jeton PAGE précède l'envoi Meta.

L'API ajoute `manualBlockReason`, compatible avec les champs existants. Le frontend
sépare l'état éditorial, le blocage serveur, les capacités de la destination et les
diagnostics automatiques. Les Pages non vérifiées sont expliquées mais exclues du
sélecteur. Un état explicite `Unverified` ou `Denied` bloque même avec un ancien
booléen `true` ; la compatibilité existante des données sans état explicite est préservée.

## Fichiers de ce correctif

- `Api/Publishing/WorkspacePublicationService.cs` et
  `Api/Development/LinkedIn/LocalPublicationService.cs` (contrat de réponse).
- `Application/Publishing/PublishScheduledPostRequest.cs`.
- `Domain/Entities/PublicationOperation.cs` et `SocialPost.cs`.
- `Infrastructure/Publishing/SqlitePublicationOperationStore.cs`.
- `Web/src/api/linkedin.ts` et `Web/src/components/PlatformPublication.tsx`.
- Tests : `UnitTests/Domain/PublicationOperationTests.cs`,
  `IntegrationTests/Publishing/PublicationOperationTests.cs`,
  `IntegrationTests/Api/WorkspaceFacebookPublicationTests.cs`,
  `IntegrationTests/Api/WorkspaceLinkedInPublicationTests.cs`,
  `Web/src/components/WorkspaceFacebookPublication.test.tsx` et
  `WorkspaceLinkedInPublication.test.tsx`.
- Documentation : ce rapport et `facebook-workspace-publication.md`.

Les chemins abrégés ci-dessus utilisent les projets `Redeemer.SocialFlow.*`.
Les modifications des interventions précédentes ont été préservées.

## Validation du 8 novembre 2026

- Tests unitaires complets : **731 réussis**.
- Intégration ciblée : **140 réussis**, filtre
  `FullyQualifiedName~WorkspaceFacebook|FullyQualifiedName~WorkspaceLinkedIn|FullyQualifiedName~Publishing|FullyQualifiedName~LinkedInPublication`.
- React complet : **199 réussis**, 23 fichiers.
- `npm.cmd run build` : TypeScript et Vite réussis.
- `git diff --check` : réussi.

Les nouvelles couvertures portent sur Approved texte/image, résultats confirmés,
échec certain et incertitude, confirmations concurrentes, absence de planification
artificielle, refus sans preuve workspace, interdiction du mode immédiat au worker,
configuration désactivée, Unverified/Denied, révocation et replanification après
aperçu. Les tests LinkedIn et les tests planifiés existants restent verts.
Les appels externes sont simulés. Le worker n'a été exercé que par les fixtures
existantes en mémoire. Aucun serveur applicatif ou worker réel n'a été démarré.

Un premier lancement parallèle des compilations .NET a rencontré un verrou de
DLL ; le relancement séquentiel a réussi. Vite a nécessité l'exécution hors sandbox
pour accéder à ses répertoires parents. L'avertissement React de clé dupliquée
du test existant `WorkspaceShell` persiste sans échec.

## Vérification locale sans publication réelle

1. Conserver les drapeaux de publication et du worker désactivés. Ouvrir un post
   Facebook `Approved` dans un espace autorisé : le blocage serveur est explicite,
   sans obligation de planifier le post.
2. Vérifier la liste des destinations. Une Page `Unverified` ne doit pas être
   sélectionnable ; `PageTaskEvidenceUnavailable` doit être expliqué. Contrôler
   aussi le mode lecture seule d'un Viewer.
3. Pour tester l'aperçu et la confirmation sans Meta, utiliser les tests automatisés
   ci-dessus avec leurs transports simulés et leurs réglages uniquement en mémoire.
4. Contrôler un post `Scheduled` futur : l'envoi manuel reste bloqué jusqu'à
   l'échéance. Les tests couvrent le passage à échéance et l'autorisation automatique.

## Conditions restantes pour un envoi réel

Une activation serveur explicite sera nécessaire ultérieurement, ainsi qu'une
destination autorisée, un utilisateur habilité, un post approuvé (ou planifié dû),
un jeton PAGE validé pour la bonne application et Page, les scopes nécessaires et
les capacités de publication établies par le parcours Meta existant. Pour un envoi
automatique, il faut en plus une autorisation de plan et un worker actif.

Le correctif n'écrit aucune autorisation ou preuve Meta. Il ne transforme donc pas
Redeemer Holding de `Unverified` en `Verified` : si sa preuve reste indisponible,
l'envoi reste bloqué. Aucune nouvelle vérification Meta réelle n'a été réalisée.
Aucun drapeau réel n'a été activé, aucun envoi, commit, push ou déploiement effectué.
