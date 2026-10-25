# Publication manuelle LinkedIn depuis SocialConnection

## Correction de visibilité — 25 octobre 2026

Le diagnostic en lecture seule à 15:55 UTC a trouvé quatre posts LinkedIn Scheduled arrivés à échéance dans Redeemer Holding, sans opération durable. Ce workspace dispose d'un Owner actif et d'un profil Connected avec permission de publication et expiration future. Les posts n'ont pas de PostSocialDestination persistée ; cela ne bloque pas ce parcours manuel, qui exige un choix explicite dans la confirmation.

La cause matérielle identifiée est le bundle `dist/assets/index-BnEbnRlc.js`, daté de 15:36 locale : il ne contenait ni `/linkedin/posts/` ni le sélecteur de profil. Or Program.cs sert précisément Web/dist sur l'origine HTTPS de l'API. Modifier les sources React ou recharger le navigateur ne reconstruit pas ce bundle. `npm run build` a produit `index-De5PGkYh.js`, dont la présence du parcours workspace et des explications a été vérifiée. La page réelle n'a pas été inspectée dans Chrome ; cette conclusion repose sur le code de service statique et les fichiers compilés.

Conditions contrôlées : contexte SaaS avec workspace, plateforme LinkedIn, fiche Approved/Scheduled/Published/Failed et rôle éditorial ; réponse API sans opération préexistante, Scheduled avec échéance atteinte ; profil du workspace, Connected, Profile, permission accordée, expiration connue et future ; sélection explicite et confirmation avant envoi. Les contrôles serveur restent inchangés.

Dans le parcours workspace, le bouton n'est plus supprimé lorsque canPublish est faux : il est désactivé avec une raison. Viewer et absence de workspace affichent aussi une explication sans appel de publication. Les erreurs de chargement de profils ont leur propre état et ne sont plus effacées par une réponse réussie de l'état de publication.

Fichiers modifiés : `components/PlatformPublication.tsx`, `components/PostDetails.tsx`, `components/WorkspaceLinkedInPublication.test.tsx`, ce guide ; Web/dist régénéré (artefact local ignoré). Validation : **48 tests React réussis**, compilation TypeScript et Vite réussie. Aucune base réelle modifiée, aucun worker activé, aucun envoi réel.

Après modification du frontend, reconstruire Web/dist avec `npm run build` depuis Redeemer.SocialFlow.Web pour l'interface servie par l'API, puis recharger la page. Le serveur Vite et l'interface statique HTTPS sont deux modes distincts.

## Existant et incompatibilités

Le moteur `Infrastructure/Publishing/LinkedIn/LinkedInPostPublisher` envoie déjà le texte et, si présent, le visuel validé. Il vérifie la destination, utilise un seul essai HTTP, reconnaît les rejets explicites et conserve comme indéterminés les résultats ambigus. Il n'a pas été réimplémenté ni modifié.

`Application/Publishing/PublishScheduledPost` orchestre la prise en charge durable et la finalisation via `SqlitePublicationOperationStore`. Une seule opération est possible par post ; un résultat Failed ou Indeterminate ne déclenche pas de renvoi automatique. Ces mécanismes sont conservés.

L'ancien `LinkedInPublicationService` dérive de `LocalPublicationService` et lit `ILinkedInCredentialProvider`, fourni en administration locale par le magasin historique protégé par DPAPI. Les routes `/api/dev/linkedin/posts/{id}/publication`, `/preview` et `/publish` restent réservées à ce parcours historique. React utilisait `PlatformPublication` (également exporté comme LinkedInPublication), visible uniquement en administration locale Development, avec une destination globale implicite.

Les incompatibilités se limitaient donc au point d'entrée, au choix du destinataire et à la résolution des credentials. Aucun moteur supplémentaire n'était nécessaire.

## Adaptations

Le nouveau contrôleur `WorkspaceLinkedInPublicationController` expose :

- `GET /api/linkedin/posts/{id}/publication` : état durable et éligibilité.
- `POST /api/linkedin/posts/{id}/preview`, corps `{ "connectionId": "…" }` : contrôle du profil et confirmation protégée, sans aucun appel LinkedIn.
- `POST /api/linkedin/posts/{id}/publish`, corps `{ "confirmation": "…" }` : action manuelle explicite qui délègue au moteur existant.

Ces routes exigent une session utilisateur, le workspace validé par `X-Workspace-Id`, la permission éditoriale existante WorkspaceApprove et la protection CSRF pour les mutations. Owner, Admin et Editor possèdent cette permission ; Viewer ne peut ni préparer ni envoyer. La gestion des connexions sociales reste séparée, réservée à Owner/Admin.

Le profil doit appartenir au même workspace, être LinkedIn/Profile, connecté, non expiré et posséder le droit de publication. Le jeton est déchiffré exclusivement depuis cette SocialConnection et ses scopes chiffrés doivent confirmer `w_member_social`. Aucun accès au magasin historique, ni secours sur le jeton global, n'est utilisé dans ce parcours.

La confirmation protégée expire après cinq minutes et lie utilisateur, workspace, publication, connexion, révision, contenu, visuel et destination. La prise en charge durable revérifie dans sa transaction le rôle, l'espace actif, la connexion et le contenu confirmé. Les droits et le jeton sont également revérifiés avant l'appel du publisher. La destination persistée dans l'opération est l'URN du profil ; les secrets ne sont jamais persistés dans l'opération ni renvoyés au navigateur.

Le client HTTP nommé n'a ni gestionnaire de journalisation, ni redirection automatique, ni retry. Les résultats sont affichés par le composant historique : confirmé, échec certain ou indéterminé avec instruction de vérification manuelle.

## Workflow conservé

Le domaine exige **Approved → Scheduled → échéance atteinte → publication**. Un post Approved seul ne peut pas être envoyé directement. Sa fiche affiche désormais le sélecteur de profils et les instructions ; l'utilisateur conserve l'action Schedule existante. Aucune planification implicite n'a été introduite.

Le choix d'un profil est explicite, sans présélection automatique. Les profils sans droit ou expirés sont exclus. La sélection est liée à la confirmation d'envoi, pas enregistrée comme modification métier d'un post approuvé. Le modèle historique reste limité à une opération par publication : cette adaptation n'introduit pas de diffusion vers plusieurs comptes pour un même post.

Le bouton de confirmation est la seule action déclenchant l'envoi. Le rafraîchissement, la sélection, l'aperçu et l'annulation ne publient rien. Après une erreur réseau, l'interface consulte l'opération durable et ne répète jamais automatiquement le POST.

## Fichiers

- API : nouveau `Controllers/WorkspaceLinkedInPublicationController.cs` ; `Program.cs` (client HTTP dédié).
- Application : `Publishing/PublishScheduledPostRequest.cs` (preuve optionnelle pour le parcours workspace).
- Infrastructure : `Publishing/SqlitePublicationOperationStore.cs` (contrôles transactionnels supplémentaires uniquement avec cette preuve).
- React : `api/linkedin.ts`, `components/PlatformPublication.tsx`, `components/PostDetails.tsx`, `App.tsx`.
- Tests : nouveau `Api/WorkspaceLinkedInPublicationTests.cs`, fixture `Api/WorkspaceIsolationTests.cs`, nouveau `components/WorkspaceLinkedInPublication.test.tsx`.
- Documentation : ce guide.

Aucun changement de modèle EF ou migration ; aucune base réelle ni donnée historique modifiée. Workers toujours désactivés. Aucun déploiement ni envoi LinkedIn réel pendant l'implémentation. Facebook et Instagram ne sont pas activés par ce parcours.

## Validation automatisée

Tests exécutés avec client HTTP LinkedIn simulé et bases temporaires :

- 114 tests unitaires publication/publisher réussis.
- 57 tests d'intégration réussis : parcours workspace, publisher historique, visuels et opérations durables.
- 2 contrôles supplémentaires réussis : confirmation liée à l'utilisateur/espace, CSRF, rôle modifié, scopes chiffrés et publication modifiée ; soit 59 tests d'intégration vérifiés.
- 40 tests React historiques publication/workflow et 4 nouveaux tests React multi-tenant réussis.
- Vérification TypeScript réussie ; compilation API isolée réussie sans erreur ni avertissement.

Les tests vérifient le jeton issu du workspace transmis au moteur existant, les espaces étrangers, les droits insuffisants, les changements de connexion, les soumissions concurrentes et la conservation des résultats confirmés, refusés et indéterminés.

## Recette manuelle sans envoi

1. Arrêter/recompiler/redémarrer l'API et actualiser le frontend. La compilation de validation a été faite dans `artifacts/linkedin-manual-tests` pour ne pas interrompre l'API locale en cours.
2. Se connecter comme Owner, Admin ou Editor et sélectionner le workspace contenant le profil LinkedIn autorisé.
3. Ouvrir une publication LinkedIn approuvée. Vérifier le sélecteur, le nom et l'identifiant du profil, ainsi que les instructions de planification.
4. Pour contrôler seulement l'interface, choisir le profil puis fermer la fiche. Aucun envoi n'a lieu.
5. Si une recette jusqu'à l'aperçu est souhaitée, utiliser une publication de test planifiée via l'action existante et attendre son échéance. Cliquer « Publier sur LinkedIn » : cette première action prépare uniquement la confirmation.
6. Contrôler le texte, la destination et le visuel sélectionné, puis cliquer **Annuler**. **Ne pas cliquer « Confirmer la publication » pendant cette recette sans envoi.** Cette confirmation ferait un véritable envoi public.
7. Vérifier que Viewer n'a pas l'action et qu'un autre workspace ne propose pas ce profil. L'historique d'opération reste disponible aux rôles éditoriaux autorisés.

Un essai réel ultérieur doit être décidé explicitement par l'utilisateur. En cas de résultat indéterminé, vérifier manuellement LinkedIn et l'opération durable avant toute autre action ; ne pas contourner le verrou anti-doublon.
