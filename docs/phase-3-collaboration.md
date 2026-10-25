# Phase 3 — Collaboration et rôles

**Mise à jour du 25 octobre 2026 :** sur demande explicite de l'utilisateur, `20261025133910_AddWorkspaceCollaboration` a été appliquée à la base métier réelle. Les sauvegardes cohérentes des deux bases sont dans `artifacts/phase3-real-migration/`. La migration a d'abord été vérifiée sur copie, puis sur l'original : intégrité et clés étrangères valides, anciens triggers conservés, toutes les valeurs des colonnes préexistantes inchangées, données Identity inchangées. Les 2 espaces, l'adhésion existante et les 25 publications sont conservés. Aucun serveur, worker, email ou publication n'a été lancé. Les mentions « non appliquée » ci-dessous décrivent l'état de livraison initial, désormais remplacé par cette mise à jour.

## Livraison

La collaboration est disponible dans les espaces professionnels : page Équipe, liste des membres, invitations par email avec rôle, acceptation, modification des rôles, retrait, révocation et départ volontaire. Les espaces personnels restent individuels. L'auto-approbation est autorisée dans les deux types d'espace ; les invariants du workflow éditorial restent inchangés.

Les contrôles React facilitent l'utilisation ; les permissions effectives sont définies dans la couche Application et vérifiées côté serveur. L'administration locale reste distincte et ne peut pas utiliser les API de collaboration réservées aux sessions Identity.

## Permissions

| Action | Owner | Admin | Editor | Viewer |
|---|---|---|---|---|
| Consulter publications, calendrier et corbeille | Oui | Oui | Oui | Oui |
| Créer, modifier, supprimer selon les états permis | Oui | Oui | Oui | Non |
| Soumettre, approuver, rejeter, y compris ses propres publications | Oui | Oui | Oui | Non |
| Programmer une date éditoriale / annuler la programmation | Oui | Oui | Oui | Non |
| Consulter les membres de son espace professionnel | Oui | Oui | Oui | Oui |
| Inviter et gérer Editor, Viewer et Admin | Oui | Oui | Non | Non |
| Inviter, attribuer ou gérer Owner | Oui | Non | Non | Non |
| Consulter et annuler les invitations | Oui | Oui, sauf annulation d'une invitation Owner | Non | Non |
| Quitter l'espace professionnel | Oui, sauf dernier Owner | Oui | Oui | Oui |

Programmer une date ne lance aucune publication. Les workers restent forcés à l'arrêt. Les fonctions IA, documents, visuels et connexions sociales gardent les restrictions de phase 2 : aucun rôle ordinaire, même Owner, ne récupère les droits d'administration locale ni les jetons globaux.

Les valeurs persistées sont Owner=1, Editor=2, Admin=3, Viewer=4. L'ancien Member=2 devient Editor sans modification des données ni perte de ses droits éditoriaux existants.

## Invitations et concurrence

- Jeton aléatoire de 256 bits, valable une heure, à usage unique. Seul SHA-256 est stocké ; aucune API de liste ne renvoie le jeton ni son empreinte.
- Le lien transporte le jeton dans le fragment `#accept-invitation?code=...`, absent des URL envoyées au serveur. React retire ce fragment sensible de la barre d'adresse et conserve le code en mémoire uniquement. Les erreurs d'envoi ne remontent pas les exceptions SMTP contenant potentiellement un lien.
- L'utilisateur doit se connecter avec une adresse confirmée correspondant exactement à l'adresse normalisée de l'invitation. Un nouvel utilisateur s'inscrit et confirme son email avec les écrans existants, puis rouvre son invitation.
- L'espace doit toujours être actif. À l'acceptation, les droits actuels de l'émetteur sont également revérifiés. Un émetteur retiré, désactivé ou rétrogradé ne conserve pas indirectement son pouvoir d'inviter.
- L'unicité d'une invitation Pending par espace/adresse est garantie en base. Une invitation expirée est inutilisable ; son statut est finalisé lors d'une nouvelle invitation à cette adresse. Une invitation acceptée ou révoquée ne peut pas être rejouée.
- Une adhésion active existante n'est jamais remplacée par une invitation. Une adhésion inactive peut être réactivée par une nouvelle invitation validée.
- Les changements sensibles exigent la Revision retournée par les listes. Une version obsolète produit HTTP 409. Les mutations utilisent une transaction SQLite et relisent les permissions ; les triggers de phase 2 continuent à protéger le dernier Owner. Le retrait désactive l'adhésion, il ne détruit pas son historique.
- Les invitations sont filtrées par espace et leurs écritures protégées. Seule l'acceptation effectue une recherche initiale sans filtre Workspace, strictement par empreinte du jeton secret, avant de poursuivre dans un contexte lié à l'espace et de vérifier destinataire, statut, expiration et émetteur. Cette exception ne permet aucune énumération.

## API

Routes sous `/api/workspaces/{workspaceId}/team` :

| Méthode | Suffixe | Corps |
|---|---|---|
| GET | (aucun) | Liste des membres, invitations autorisées, rôle actuel |
| POST | `/invitations` | `{ email, role }` |
| PUT | `/members/{userId}` | `{ role, revision }` |
| POST | `/members/{userId}/remove` | `{ revision }` |
| POST | `/invitations/{id}/revoke` | `{ revision }` |
| POST | `/leave` | `{ revision }` |

Acceptation : `POST /api/invitations/accept`, corps `{ code }`, retourne `{ workspaceId }`. Une adhésion préalable n'est naturellement pas requise pour accepter ; la session confirmée et l'adresse destinataire le sont. Toutes les mutations conservent CSRF. L'envoi et l'acceptation utilisent aussi la limitation de débit existante.

Pour les API d'équipe, le serveur autorise explicitement l'espace indiqué dans la route ; un en-tête d'espace ne remplace jamais cette vérification. Les publications conservent le contrôle de X-Workspace-Id et ajoutent les permissions Edit/Approve à chaque mutation.

## Migration nécessaire — non appliquée à la base réelle

`20261025133910_AddWorkspaceCollaboration` ajoute :

- `WorkspaceMemberships.Revision` ; la valeur initiale des adhésions préexistantes est Guid.Empty, remplacée à chaque modification.
- `WorkspaceInvitations`, ses index, sa clé étrangère d'espace et son trigger d'immutabilité de WorkspaceId.

Aucune ressource métier historique n'est réécrite. Aucune adhésion historique n'est attribuée. Redeemer Holding reste suspendu et sans propriétaire, tant qu'une personne n'a pas été désignée explicitement.

La migration n'est appliquée qu'aux bases temporaires des tests. L'application réelle nécessitera cette migration avant l'utilisation de la phase 3. Après validation explicite, arrêter l'application, sauvegarder les bases de façon cohérente, tester sur une copie, puis utiliser un chemin absolu :

```powershell
dotnet ef database update 20261025133910_AddWorkspaceCollaboration --context SocialFlowDbContext --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --connection "Data Source=C:\chemin-explicite\socialflow.db"
```

Ce guide ne vaut pas autorisation d'exécuter cette commande sur les bases réelles. Le retour arrière nécessite une sauvegarde vérifiée ; Down refuse de retirer les garde-fous ou l'historique d'invitations.

## Email et limites

L'envoi utilise le service de phase 1. En développement, `Authentication:Email:UseLocalPickup` écrit les messages dans le répertoire local AccountMail déjà prévu ; sinon le serveur SMTP doit être configuré. Aucun message réel n'a été envoyé lors des tests. Un échec d'envoi révoque l'invitation encore en attente et retourne un message permettant de réessayer.

Le jeton n'est pas conservé après fermeture/rechargement de la page : rouvrez le lien reçu. Il n'y a pas de renvoi automatique, de worker d'email ni de journal d'audit complet des modifications. Pour renvoyer, annuler l'invitation en attente puis en créer une nouvelle. Les changements de droits distants sont appliqués par le serveur à chaque requête ; une erreur d'autorisation ferme le contenu d'espace, puis la sélection recharge les droits. Le retour de l'écran Équipe aux publications revalide également le rôle.

Les tests UI sont automatisés sous jsdom, sans validation visuelle manuelle dans un navigateur. Aucun déploiement, publication réelle, connexion sociale multi-tenant, abonnement ou développement de phase 4 n'a été effectué.

## Vérifications

Les suites unitaires, d'intégration et React couvrent les quatre rôles, l'auto-approbation personnelle/professionnelle, les doublons, les jetons expirés/révoqués, les mauvais destinataires, les versions obsolètes, le dernier Owner, les requêtes étrangères et les tentatives d'élévation de privilèges. Les tests utilisent uniquement des bases temporaires, des emails simulés et les protections multi-tenant existantes. Les tests de migration vérifient aussi la conservation des données historiques et des relations.

Résultats finaux : **594 tests unitaires, 509 tests d'intégration et 153 tests React réussis**. Le build complet de la solution, incluant TypeScript/Vite, réussit sans avertissement ni erreur. La lecture finale de la base réelle confirme que sa dernière migration reste `20261025130226_EnforceWorkspaceInvariants` ; l'intégrité et les clés étrangères sont valides. Un espace personnel préexiste désormais dans cette base ; aucune adhésion n'existe pour l'espace historique.

## Fichiers créés et modifiés

Préfixes relatifs à la racine du dépôt :

- Domain : `Entities/Workspace.cs`, nouveau `Entities/WorkspaceInvitation.cs`.
- Application : nouveau `Accounts/Collaboration.cs` (permissions, DTO, contrats).
- Infrastructure : nouveau `Accounts/WorkspaceCollaboration.cs`, `Accounts/AccountEmailSender.cs`, `Persistence/SocialFlowDbContext.cs`, `Persistence/Configurations/WorkspaceConfiguration.cs`, nouveau `Persistence/Configurations/WorkspaceInvitationConfiguration.cs`, migration `20261025133910_AddWorkspaceCollaboration.cs` et son Designer, `Persistence/Migrations/SocialFlowDbContextModelSnapshot.cs`.
- Api : nouveau `Controllers/TeamController.cs` (équipe, acceptation, acteur HTTP), `Controllers/PostsController.cs`, `Security/AccountSecurity.cs`, `Security/WorkspaceAuthorization.cs`, `Errors/ApiExceptionHandler.cs`.
- Web : nouveaux `src/api/team.ts`, `src/pages/Team.tsx`, `src/pages/Team.test.tsx`, `src/InvitationAcceptance.tsx`, `src/InvitationAcceptance.test.tsx` ; modifications de `src/api/workspaces.ts`, `src/AccountShell.tsx`, `src/AccountShell.test.tsx`, `src/WorkspaceShell.tsx`, `src/App.tsx`, `src/components/PostDetails.tsx`, `src/components/workflows.test.tsx`, `src/pages/Posts.tsx`, `src/pages/Dashboard.tsx`, `src/pages/pages.test.tsx`.
- Tests .NET : nouveau `UnitTests/WorkspacePermissionTests.cs`, nouveau `IntegrationTests/Api/CollaborationTests.cs`, adaptation de `IntegrationTests/Api/WorkspaceIsolationTests.cs` pour partager les fixtures authentifiées et capturer les emails simulés.
- Documentation : ce guide et le lien ajouté au README.

## Préparation de phase 4

À valider avec le besoin métier avant de commencer : audit des changements de permissions et de l'approbation, politique de renvoi/rétention des invitations, granularité éventuelle des droits éditoriaux, puis conception des connexions sociales par espace. Les jetons globaux existants ne doivent pas être transférés automatiquement. La phase 4 n'est pas commencée.
