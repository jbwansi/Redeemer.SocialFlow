# Publication Facebook par espace de travail

## Analyse du pipeline existant

LinkedIn utilisait deux points d'entrée : `WorkspaceLinkedInPublicationController`
pour les connexions SaaS et `LinkedInPublicationService : LocalPublicationService`
pour l'ancien mode local. Tous deux utilisent `PublishScheduledPost`,
`SqlitePublicationOperationStore` et `LinkedInPostPublisher`.

Le worker existant est `LinkedInPublicationWorker`, avec un intervalle de 30 secondes.
Il appelait les services locaux LinkedIn et Facebook dans l'espace historique.
`Program.cs` désactive leurs anciens drapeaux `WorkerEnabled`. Le mode SaaS LinkedIn
était manuel : sa table `LinkedInAutomations` existait, sans parcours actif de
planification automatique par espace. Le frontend SaaS ne présentait que LinkedIn.

Facebook disposait déjà de `FacebookPostPublisher` pour le texte et l'image, mais
celui-ci ne lisait que `IFacebookConnectionStore`, l'ancien stockage local contenant
le jeton USER et ses Pages. Il ne pouvait donc pas publier avec une connexion SaaS
stockée dans `SocialConnections`. Ces différences expliquaient qu'un post SaaS
Facebook puisse être `Scheduled` sans être exécuté.

## Architecture finale et réutilisation

Le service `WorkspacePublicationService` extrait le parcours du contrôleur LinkedIn
et le partage avec Facebook. Les routes LinkedIn et leur protocole de confirmation
restent compatibles. La sélection est explicite, limitée à une destination par post,
comme le permet l'unique opération durable actuelle.

Le même `FacebookPostPublisher` accepte maintenant un fournisseur de jeton PAGE
validé. Son constructeur historique conserve le parcours local. Aucun autre moteur
de publication ou worker n'est créé.

La table et le type existants `LinkedInAutomations` / `LinkedInAutomation` sont
réutilisés pour les autorisations d'envoi automatique Facebook ; leur nom est conservé
pour la compatibilité de base de données. La plateforme est vérifiée via le post et
la connexion. Cette modification ne nécessite aucune migration supplémentaire.

| Fonction | Composants réutilisés |
|---|---|
| Confirmation manuelle | Protection des données, utilisateur, espace, contenu, visuel, révision de connexion, expiration à 5 minutes |
| Exécution | `PublishScheduledPost` et ses adaptateurs fournisseur |
| Anti-doublon | Transaction SQLite, opération unique par post, résultat durable consultable |
| Visuel | `PublicationVisualSnapshot`, `IVisualStorage`, contrôle du type, taille et SHA-256 |
| Suivi | `PublicationOperation`, identifiant externe, dates et état |
| Worker | `LinkedInPublicationWorker`, même intervalle, nouveau scope par espace actif |
| Interface | `PlatformPublication`, confirmation, suivi et actualisation existants |

## Endpoints

La planification éditoriale reste `POST /api/posts/{id}/schedule` ; le post doit être
approuvé et la date future. Dans un espace de travail, Facebook et LinkedIn permettent
désormais l'envoi manuel d'un post `Approved`, après choix explicite de la destination
et confirmation, ou d'un post `Scheduled` arrivé à échéance. Une échéance future
n'est jamais avancée silencieusement. Le mode local historique reste limité aux
posts planifiés à échéance.

Le chemin immédiat transmet `PublishApproved` au même `PublishScheduledPost`.
Le store refuse ce mode sans preuve de confirmation workspace ou avec une révision
de plan automatique. La transaction revalide le rôle, la connexion, sa révision,
les capacités et l'instantané avant de créer l'unique opération durable du post.
Aucune date `ScheduledAt` ni autorisation automatique n'est créée pour cet envoi.
La confirmation réussie passe directement `Approved` à `Published` ; un échec
certain passe à `Failed`. Un résultat indéterminé conserve `Approved` mais bloque
tout nouvel envoi par l'opération durable existante.

`manualBlockReason` distingue les blocages manuels (`publication_disabled`,
`publishing_permission_unverified`, `approval_required`, `schedule_missing`,
`scheduled_not_due`, `operation_exists`). `blockReason` conserve les diagnostics
existants, notamment ceux du traitement automatique. Un worker arrêté ne bloque
pas un envoi manuel autorisé. L'interface affiche les Pages exclues et explique
`Unverified — PageTaskEvidenceUnavailable`, sans les proposer comme destinations.

Les routes SaaS exigent une session, le rôle `WorkspaceApprove`, un espace actif
(`X-Workspace-Id`) et la protection CSRF pour les mutations :

| Méthode et route | Fonction |
|---|---|
| `GET /api/facebook/posts/{id}/publication` | État durable, éligibilité et diagnostic de blocage |
| `POST /api/facebook/posts/{id}/preview` | Aperçu ; corps `{ "connectionId": "…" }` |
| `POST /api/facebook/posts/{id}/publish` | Envoi confirmé ; corps `{ "confirmation": "…" }` |
| `POST /api/facebook/posts/{id}/automatic` | Autorisation explicite à échéance ; corps `{ "connectionId": "…" }` |
| `DELETE /api/facebook/posts/{id}/automatic` | Retrait de l'autorisation automatique avant prise en charge |

Les routes manuelles LinkedIn correspondantes restent sous `/api/linkedin/posts/{id}`.
Les anciens endpoints locaux `/api/dev/linkedin/posts/{id}` et
`/api/dev/facebook/posts/{id}` restent sur leurs services historiques.

## Permissions et appels Meta

Une connexion doit être une Page Facebook de cet espace, connectée, non expirée,
avec `PublishingPermissionGranted` déjà établi par le parcours d'autorisation.
Le correctif n'affecte jamais artificiellement cette propriété à `true`.

Le jeton PAGE est déchiffré uniquement côté serveur avec le contexte espace/connexion.
Les scopes chiffrés doivent contenir `pages_manage_posts` et `pages_read_engagement`.
Avant l'envoi, le fournisseur OAuth revalide `debug_token` : application, type PAGE,
validité, échéances et scopes. La lecture de la Page et `/me?fields=id` doivent
correspondre à la destination. Une échéance absente n'est acceptée qu'avec cette
validation en ligne. Aucun jeton USER n'est utilisé comme jeton PAGE.

Le texte utilise `POST /{page-id}/feed`. L'image utilise `POST /{page-id}/photos`
avec les octets validés, le texte et `published=true`, suivant l'adaptateur existant.
Le bearer et `appsecret_proof` utilisent le jeton PAGE. La version Graph et le secret
proviennent de la configuration OAuth effective, y compris ses références legacy
explicites. Les clients n'ont ni journalisation HTTP, ni redirection, ni retry automatique.
Les corps d'erreur Meta ne sont ni renvoyés au navigateur ni enregistrés dans l'opération.

Une réponse confirmée doit contenir un identifiant de publication de la Page attendue
(`id` pour texte, `post_id` pour photo). Une erreur Graph, même accompagnée d'un ID
dans une réponse 2xx, ne produit jamais de succès.

**Limite Meta :** une Page issue de la découverte complémentaire sans preuve de
`CREATE_CONTENT` reste non autorisée à publier. Les scopes et la possession d'un
jeton PAGE ne suffisent pas à lever ce blocage. Il faut une nouvelle autorisation
apportant une preuve exploitable par le parcours existant. Aucun `tasks` n'est demandé
sur le nœud Page. La documentation Meta en ligne a renvoyé HTTP 429 pendant cette
intervention ; les appels texte/image existants ont été conservés et testés avec des
réponses simulées, sans prétendre valider le compte Meta réel.

## Scheduled, erreurs et reprise

Le worker ne prend que les plans explicitement autorisés, arrivés à échéance et sans
opération existante. Il recontrôle l'espace, les droits actuels de l'auteur de
l'autorisation, le contenu, le visuel, l'horaire, la connexion et le jeton. Les
permissions et la révision du plan sont également contrôlées à la prise transactionnelle.

- Sans autorisation automatique, le post reste `Scheduled` avec le diagnostic
  `automatic_not_authorized`. Le worker ne récupère pas les anciennes publications
  silencieusement.
- Permissions non vérifiées : `publishing_permission_unverified` dans l'interface,
  aucun aperçu actionnable ni autorisation automatique.
- Échec de validation avant prise en charge : plan `Blocked`, diagnostic
  `authorization_or_validation_failed`, aucun appel de publication. Après correction,
  une réautorisation explicite permet la reprise.
- Succès confirmé : opération `Confirmed`, post `Published`, ID Meta enregistré.
- Rejet certain après prise en charge : opération et post `Failed`.
- Accusé perdu, réponse ambiguë ou erreur serveur : opération `Indeterminate` ; le
  post peut rester `Scheduled`, mais l'opération interdit tout nouvel envoi automatique.

Comme LinkedIn, une opération déjà prise en charge n'est jamais automatiquement
retentée, même après redémarrage ou soumission concurrente. L'identifiant d'opération
est une protection locale ; il n'est pas présenté comme une clé d'idempotence Meta.
Un résultat indéterminé exige une vérification manuelle avant toute décision éditoriale.

## Configuration et validation locale

Les nouveaux drapeaux sont désactivés par défaut. Aucun fichier de configuration
local contenant des secrets n'a été modifié pendant l'implémentation :

```json
{
  "SocialConnections": {
    "Publication": { "WorkerEnabled": true },
    "Providers": { "Facebook": { "PublicationEnabled": true } }
  }
}
```

Ils activent respectivement le worker commun pour les plans SaaS et la publication
Facebook SaaS. Les anciens drapeaux locaux restent désactivés par `Program.cs`.
Les migrations déjà présentes dans le dépôt, notamment la table des autorisations,
doivent être appliquées à l'environnement local avant utilisation.

Validation sans publication réelle :

1. Exécuter les suites ci-dessous : tous les appels Meta des nouveaux tests sont
   remplacés par un `HttpMessageHandler` simulé ; les bases et visuels sont temporaires.
2. Dans l'interface locale, conserver le worker désactivé pour l'inspection. Ouvrir
   un post Facebook approuvé puis planifié et vérifier le panneau de publication.
3. Avec une Page non vérifiée, vérifier le diagnostic et les boutons bloqués.
4. Avec une Page autorisée, sélectionner explicitement la destination et préparer
   l'aperçu. Ne pas confirmer l'envoi vers Meta pour cette validation.
5. Vérifier les réponses frontend : ni jeton PAGE, ni secret applicatif, ni chemin
   privé du visuel. Vérifier qu'un autre espace ou un lecteur ne peut agir.
6. Tester l'échéance, la concurrence, les erreurs et la reprise avec les tests simulés.
   L'autorisation automatique sur une vraie Page avec le worker actif provoquera
   réellement l'envoi à échéance ; elle n'a pas été activée pendant l'intervention.

Commandes :

```text
dotnet test Redeemer.SocialFlow.UnitTests --no-restore
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore
cd Redeemer.SocialFlow.Web
npm.cmd test
node node_modules/typescript/bin/tsc --noEmit
```

Résultats historiques du raccordement initial : **677 tests unitaires**, **578 tests d'intégration**
et **189 tests React dans 23 fichiers** réussis. Le contrôle TypeScript sans émission
et `git diff --check` passent. React signale une clé dupliquée dans le test existant
`WorkspaceShell`, sans échec. Les nouvelles couvertures incluent les quatre
combinaisons texte/image et manuel/automatique, les permissions, jetons, erreurs,
accusés perdus, soumissions concurrentes et la reprise explicite après prévalidation
bloquée. Les suites LinkedIn existantes font partie des passages complets.

## Fichiers de cette intervention

- API : `Controllers/WorkspaceLinkedInPublicationController.cs`,
  `Controllers/WorkspaceFacebookPublicationController.cs`,
  `Publishing/WorkspacePublicationService.cs`, `Publishing/WorkspacePublicationScheduling.cs`,
  `Development/LinkedIn/LinkedInPublicationWorker.cs`,
  `Development/LinkedIn/LocalPublicationService.cs`, `Program.cs`.
- Infrastructure : `Publishing/Facebook/FacebookPostPublisher.cs`,
  `Publishing/Facebook/IFacebookPublicationCredentialProvider.cs`,
  `Publishing/SqlitePublicationOperationStore.cs`, `Social/FacebookWorkspaceOAuthProvider.cs`.
- Frontend : `src/api/facebook.ts`, `src/api/linkedin.ts`,
  `src/components/PlatformPublication.tsx`, `src/components/PostDetails.tsx`.
- Tests : `IntegrationTests/Api/WorkspaceFacebookPublicationTests.cs`,
  `UnitTests/Infrastructure/FacebookPublisherTests.cs`,
  `Web/src/components/WorkspaceFacebookPublication.test.tsx`.
- Documentation : ce fichier. Les autres modifications déjà présentes dans l'arbre
  de travail ne font pas partie de cette intervention.
