# Phase 4 — Connexions sociales par workspace

Livraison du 25 octobre 2026. Code implémenté. Migration appliquée à la base réelle le 25 octobre 2026 après autorisation explicite, sauvegardes et répétition vérifiée. Aucun appel OAuth réel, publication, activation de worker ou déploiement. La phase suivante attend validation.

## Fonctionnalités

- Plusieurs comptes LinkedIn, pages Facebook et comptes professionnels Instagram par workspace ; sélection multiple après OAuth et filtre par plateforme.
- Owner/Admin : connexion, validation, reconnexion et déconnexion. Editor/Viewer : lecture des seules métadonnées. Editor peut aussi préparer les destinations d'une publication.
- Statuts Connected, Expired, Revoked et ReauthorizationRequired ; échéance et autorisation de publication affichées séparément. Une autorisation détectée n'active aucune publication automatique.
- Unicité d'un compte externe non révoqué par plateforme et workspace, sans unicité globale. Une reconnexion met à jour la connexion existante ; les anciennes connexions révoquées restent dans l'historique, sans jeton.
- Associations publication/destinations avec clés étrangères composites imposant le même workspace. Affectation autorisée seulement pour un brouillon ou une publication rejetée, vers des connexions utilisables de la même plateforme.

Les intégrations locales et leurs secrets DPAPI restent séparés. Aucun jeton historique n'est importé et aucune publication historique n'est affectée automatiquement.

## Sécurité

Les nouvelles données utilisent les filtres et gardes de tenant du contexte EF. Les services vérifient aussi le workspace actif, l'appartenance courante et le rôle. Les mutations revérifient les droits dans leur transaction ; les révisions protègent des mises à jour concurrentes.

Le state OAuth aléatoire de 256 bits est conservé sous forme de hash. La session est liée au workspace initial, à l'utilisateur et à la plateforme, expire après dix minutes et est consommée transactionnellement avant l'échange du code. Un paramètre workspace ajouté au callback ne définit jamais la destination. Les droits sont revérifiés après l'appel externe et avant la sélection. Les mutations HTTP conservent la protection CSRF et l'authentification par cookie.

Le moteur supporte PKCE S256 et le teste avec un fournisseur simulé. Les adaptateurs actuels utilisent les flux web confidentiels avec secret client ; ils n'activent pas d'extension PKCE non documentée pour ces flux. Le [PKCE LinkedIn documenté pour les applications natives](https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow-native) utilise un parcours distinct soumis à activation ; ce n'est pas le [flux web confidentiel](https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow) utilisé ici.

Les jetons et les candidats temporaires sont chiffrés en AES-256-GCM, avec nonce aléatoire et contexte authentifié incluant workspace, identifiant et usage. Ils ne figurent ni dans les DTO frontend ni dans les messages d'erreur. Le stockage est compatible Linux et ne dépend pas de DPAPI.

Les clients HTTP OAuth n'utilisent pas les gestionnaires de journalisation et ne suivent pas les redirections. Le callback capture puis retire sa query string avant les middlewares suivants ; les journaux ASP.NET Core sont filtrés au niveau Warning. **Configurer également le reverse proxy pour ne pas journaliser les query strings des chemins `/api/social-oauth/*/callback` et ne pas activer de journalisation des corps HTTP OAuth.** Les erreurs des fournisseurs sont remplacées par des messages contrôlés.

## Configuration

Les fournisseurs sont indisponibles par défaut. Configurer via variables d'environnement ou gestionnaire de secrets, sans committer les secrets :

| Clé .NET (séparateur environnement : `__`) | Valeur |
| --- | --- |
| `Authentication:PublicOrigin` | Origine HTTPS publique exacte, sans chemin |
| `SocialConnections:Encryption:ActiveKeyId` | Identifiant de clé sans point, par exemple `key1` |
| `SocialConnections:Encryption:Keys:key1` | 32 octets aléatoires cryptographiques encodés en Base64 |
| `SocialConnections:Providers:LinkedIn:Enabled` | `true` uniquement après configuration |
| `SocialConnections:Providers:LinkedIn:PermissionsApproved` | `true` uniquement après vérification des accès dans le portail fournisseur |
| `SocialConnections:Providers:LinkedIn:ClientId` / `ClientSecret` | Identifiants de l'application |
| `SocialConnections:Providers:LinkedIn:Scopes` | Liste séparée par des espaces, voir ci-dessous |
| `SocialConnections:Providers:LinkedIn:OrganizationsEnabled` | `false` pour commencer avec les profils seuls |
| `SocialConnections:Providers:LinkedIn:ApiVersion` | Version LinkedIn prise en charge, au format `YYYYMM`, obligatoire pour les organisations |

Les sections `Facebook` et `Instagram` possèdent chacune `Enabled`, `PermissionsApproved`, `ClientId`, `ClientSecret`, `Scopes` et `ApiVersion`. Pour Meta, renseigner une version Graph actuellement disponible au format `vN.0` ; aucune version arbitraire n'est imposée dans le code. L'intégration Instagram choisie utilise Facebook Login et peut partager l'application Meta si celle-ci possède les accès nécessaires.

Par exemple, `SocialConnections__Providers__Facebook__Enabled=false` maintient le fournisseur désactivé. `PermissionsApproved` représente une déclaration de configuration de l'opérateur, pas une preuve d'autorisation externe : les réponses OAuth et les autorisations effectivement accordées sont aussi contrôlées.

Enregistrer exactement les callbacks suivants auprès des fournisseurs (remplacer l'origine) :

- Facebook : `https://votre-domaine/api/social-oauth/1/callback`
- LinkedIn : `https://votre-domaine/api/social-oauth/2/callback`
- Instagram via Facebook Login : `https://votre-domaine/api/social-oauth/3/callback`

Conserver la clé de chiffrement hors de la base métier, avec accès limité au compte de service et sauvegarde sécurisée séparée. Pour une rotation, ajouter une nouvelle clé, changer ActiveKeyId et conserver les anciennes clés nécessaires au déchiffrement. Les nouvelles écritures et validations réussies utilisent la clé active ; aucune rotation massive automatique n'est exécutée. La perte d'une clé exige une reconnexion des comptes concernés.

## Permissions externes et limites

### LinkedIn

Demander `openid profile` pour identifier le profil ; ajouter `w_member_social` seulement si l'application y a accès. La connexion ne suppose pas ce droit acquis. Les jetons de renouvellement sont conservés et utilisés uniquement lorsque LinkedIn les émet ; sans refresh token utilisable, une reconnexion est nécessaire à expiration.

Pour les organisations, activer OrganizationsEnabled, renseigner ApiVersion, obtenir `r_organization_admin` ou `rw_organization_admin`, et `w_organization_social` pour le droit de publication. L'adaptateur découvre actuellement les organisations avec rôle ADMINISTRATOR approuvé ; les autres rôles ne sont pas exposés. Les produits et autorisations applicables doivent être accordés par LinkedIn. Voir la [documentation officielle des accès aux organisations](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/organizations/organization-access-control-by-role?view=li-lms-2026-03).

### Facebook et Instagram

Pour Facebook, demander `pages_show_list pages_read_engagement` et, si disponible, `pages_manage_posts`. Seules les pages sont candidates ; le droit de publication requiert aussi la tâche CREATE_CONTENT. Un profil Facebook personnel n'est pas une destination.

Pour Instagram, demander `pages_show_list pages_read_engagement instagram_basic` et, si disponible, `instagram_content_publish`. Le parcours découvre les comptes professionnels liés aux pages accessibles, pas les comptes personnels. Il utilise l'API Instagram avec Facebook Login, pas le produit Instagram Login et ses scopes différents. Voir la [collection officielle Meta](https://www.postman.com/meta/instagram/folder/u4g5a2a/instagram-api-with-facebook-login).

La disponibilité hors utilisateurs de test dépend des accès effectivement accordés à l'application, de l'examen des permissions et, selon le cas, des vérifications demandées par Meta. Aucune approbation externe n'a été obtenue ou vérifiée pendant cette livraison. Les boutons restent indisponibles tant que la configuration requise manque.

Le parcours Meta échange le jeton initial contre un jeton de longue durée, puis stocke les jetons de page nécessaires aux candidats sélectionnés. L'expiration du grant utilisateur est utilisée comme échéance conservatrice. Une expiration inconnue est refusée ; aucune durée n'est inventée. Il n'y a pas de renouvellement Meta automatique en arrière-plan.

### Limites communes

- La déconnexion révoque **localement** la connexion et efface son secret. Elle ne supprime pas le grant global chez le fournisseur, qui peut servir à d'autres workspaces. Une révocation globale se fait dans le portail du fournisseur et peut affecter tous les espaces concernés.
- La validation est explicite ; aucun worker de renouvellement ou de nettoyage n'est activé. Les sessions expirées sont inutilisables ; leurs secrets sont purgés opportunément au prochain démarrage OAuth du même utilisateur dans le même workspace. Prévoir une politique de purge périodique avant exploitation à grande échelle.
- Découverte bornée à 500 candidats et sélection à 100 ; un dépassement échoue explicitement. Pas de publication réseau ni de test avec comptes externes réels dans cette phase.
- Les destinations sont préparées au niveau modèle et API. Leur sélection dans l'éditeur de publications reste à intégrer ; le modèle actuel impose la même plateforme que la publication. La publication future devra revérifier les droits, tâches et capacités du fournisseur au moment d'envoyer.

## API et migration

Base authentifiée : `/api/workspaces/{workspaceId}/social-connections`. Liste, `providers`, `start`, `sessions/{sessionId}`, `sessions/{sessionId}/select`, `{id}/validate`, `{id}/disconnect` ; lecture/écriture des associations via `posts/{postId}/destinations`.

Migration nécessaire : **`20261025142152_AddWorkspaceSocialConnections`**. Elle crée SocialConnections, SocialOAuthSessions et PostSocialDestinations, leurs index, contraintes et triggers d'immutabilité du workspace. Elle ne réécrit pas les tables historiques. Le rollback destructif est volontairement refusé : restaurer une sauvegarde cohérente si nécessaire.

**Migration appliquée à la base réelle locale le 25 octobre 2026.** Sauvegardes dans `artifacts/phase4-real-migration/20261025-153913/`. Intégrité et clés étrangères vérifiées ; toutes les données préexistantes et les comptes Identity sont inchangés. Les trois nouvelles tables sont vides. Pour une autre installation, après validation explicite seulement, sauvegarder les bases et le trousseau de clés, répéter sur une copie puis appliquer en ciblant explicitement le fichier métier :

```powershell
dotnet ef database update 20261025142152_AddWorkspaceSocialConnections --context SocialFlowDbContext --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --connection "Data Source=C:\chemin\explicite\socialflow.db"
```

Ne pas lancer cette commande avec le chemin d'exemple. Aucun changement de schéma Identity n'est nécessaire. Avant migration, les nouvelles routes nécessitant ces tables ne sont pas utilisables.

## Fichiers livrés

- Domain : `Entities/SocialConnection.cs` (connexions, sessions OAuth, associations).
- Application : `Accounts/SocialConnections.cs` ; `Accounts/Collaboration.cs` (permission ManageSocial).
- Infrastructure : `Social/SocialConnections.cs`, `SocialSecretProtector.cs`, `WorkspaceOAuthProviders.cs`, `WorkspaceSocialConnectionResolver.cs`, `SocialDependencyInjection.cs` ; `Persistence/SocialFlowDbContext.cs`, `Configurations/SocialConnectionConfiguration.cs`, migration, Designer et snapshot.
- API : `Controllers/SocialConnectionsController.cs`, `Program.cs`, gestionnaire d'exceptions API.
- Web : `src/api/socialConnections.ts`, `src/pages/SocialConnections.tsx`, `src/App.tsx`, `src/auth/WorkspaceShell.tsx`.
- Tests : `SocialConnectionSecurityTests.cs`, `Api/SocialConnectionTests.cs`, fixture `Api/WorkspaceIsolationTests.cs`, `SocialConnections.test.tsx`, `WorkspaceShell.test.tsx`.
- Documentation : ce guide et README.

## Vérifications

Les fournisseurs OAuth et leurs réponses HTTP sont simulés. Les bases d'intégration sont temporaires ; les tests de migration vérifient les contraintes sans migrer la base réelle.

- Tests unitaires : **600 réussis**.
- Tests d'intégration : **516 réussis dans la suite complète**, puis **2 nouveaux tests réussis** pour changements de rôle/callbacks concurrents et révocation/refus du consentement, soit 518 tests vérifiés.
- Tests React : **160 réussis**, dans 21 fichiers.
- Compilation complète .NET et frontend de production : **réussie, 0 erreur et 0 avertissement**.
- Couverture ajoutée : chiffrement contextualisé et rotation, absence de fuite, state/rejeu/expiration, CSRF, rôles et changements de rôle, comptes multiples et doublons, isolation et clés étrangères, expiration/révocation, erreurs fournisseurs, scopes réellement accordés, sélection multiple et retours OAuth React.

## Phase suivante : IA et visuels multi-tenant

Préparer séparément les autorisations, quotas, configurations IA et ressources visuelles par workspace, avec accès signé/contrôlé aux médias et tests d'isolation. Réutiliser les destinations explicites sans charger de secrets sociaux dans le navigateur ou les prompts. Définir ensuite le parcours éditeur et une publication autorisée par destination. Aucun développement de cette nouvelle phase n'est engagé ici.

