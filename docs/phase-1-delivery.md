# Livraison de la phase 1 — 25 octobre 2026

L’authentification Identity et les pages React sont implémentées. Les comptes ordinaires sont séparés de l’accès administratif local et n’ont aucun accès aux données métier globales. Aucune modification de la base réelle, aucun déploiement, aucun appel réel à un fournisseur IA/social, aucune publication et aucun démarrage de worker n’ont été réalisés.

## Validation

| Vérification | Résultat |
|---|---|
| Tests unitaires .NET | 588 réussis |
| Suite complète d’intégration .NET | 490 réussis |
| Tests ciblés comptes et frontière administrative, relancés après les derniers ajustements de sécurité | 25 réussis |
| Tests React et passerelle Vite | 133 réussis, 16 fichiers |
| Tests isolés du lanceur PowerShell | 33 assertions réussies |
| Compilation solution .NET et frontend | Réussie, sans avertissement ni erreur |
| `git diff --check` | Réussi |
| Base métier réelle | SHA-256 identique avant/après |

Les contrôles couvrent inscription, confirmation obligatoire et rejeu, cookies sécurisés, déconnexion et rejeu de cookie, récupération et renvoi génériques, réinitialisation à usage unique avec révocation, CSRF inter-navigateurs, HTTPS, verrouillage, limite de débit, expiration de session sans prolongation, refus des comptes ordinaires sur les vraies routes Development activées, et restrictions de l’administration locale. Les publications et échanges OAuth des tests historiques utilisent des fournisseurs simulés.

La compilation et certains tests ont nécessité une exécution hors du bac à sable Windows, dont les restrictions empêchaient esbuild ou PowerShell de fonctionner. Cela n’a pas entraîné de lancement de l’application contre ses données réelles.

## Migration

Créée : `20261025112137_InitialAccounts`, contexte `AccountDbContext`, dossier `Redeemer.SocialFlow.Infrastructure/Accounts/Migrations`. Appliquée uniquement aux fichiers SQLite temporaires des tests. Aucun fichier de base Identity réel n’a été créé. Les migrations historiques restent inchangées.

## Fichiers créés

```text
Redeemer.SocialFlow.Api/Controllers/AuthController.cs
Redeemer.SocialFlow.Api/Security/AccountDesignTimeFactory.cs
Redeemer.SocialFlow.Api/Security/AccountSecurity.cs
Redeemer.SocialFlow.Api/Security/LocalOAuthTickets.cs
Redeemer.SocialFlow.Application/Accounts/Contracts.cs
Redeemer.SocialFlow.Infrastructure/Accounts/AccountDbContext.cs
Redeemer.SocialFlow.Infrastructure/Accounts/AccountEmailSender.cs
Redeemer.SocialFlow.Infrastructure/Accounts/AccountService.cs
Redeemer.SocialFlow.Infrastructure/Accounts/Migrations/20261025112137_InitialAccounts.cs
Redeemer.SocialFlow.Infrastructure/Accounts/Migrations/20261025112137_InitialAccounts.Designer.cs
Redeemer.SocialFlow.Infrastructure/Accounts/Migrations/AccountDbContextModelSnapshot.cs
Redeemer.SocialFlow.IntegrationTests/Api/AccountsApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/LocalAdministrationTests.cs
Redeemer.SocialFlow.IntegrationTests/BusinessApiFactory.cs
Redeemer.SocialFlow.Web/src/AccountShell.tsx
Redeemer.SocialFlow.Web/src/AccountShell.test.tsx
Redeemer.SocialFlow.Web/src/accounts.css
Redeemer.SocialFlow.Web/src/api/accounts.ts
Redeemer.SocialFlow.Web/src/api/accounts.test.ts
docs/phase-1-authentication.md
docs/phase-1-delivery.md
```

## Fichiers modifiés

```text
README.md
Redeemer.SocialFlow.Api/Controllers/DevLinkedInController.cs
Redeemer.SocialFlow.Api/Controllers/FacebookOAuthController.cs
Redeemer.SocialFlow.Api/Program.cs
Redeemer.SocialFlow.Api/appsettings.json
Redeemer.SocialFlow.Infrastructure/Redeemer.SocialFlow.Infrastructure.csproj
Redeemer.SocialFlow.IntegrationTests/Api/DevelopmentAiApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/GenerateDraftApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/KnowledgeDocumentsApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/LinkedInOAuthTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/LinkedInPublicationApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Api/PostsApiTests.cs
Redeemer.SocialFlow.IntegrationTests/Publishing/FacebookPublicationTests.cs
Redeemer.SocialFlow.IntegrationTests/Publishing/LinkedInImagePublicationTests.cs
Redeemer.SocialFlow.IntegrationTests/Visuals/VisualApiTests.cs
Redeemer.SocialFlow.Web/README.md
Redeemer.SocialFlow.Web/server/visualGateway.ts
Redeemer.SocialFlow.Web/server/visualGateway.test.ts
Redeemer.SocialFlow.Web/src/api/linkedin.ts
Redeemer.SocialFlow.Web/src/api/posts.ts
Redeemer.SocialFlow.Web/src/components/LinkedInConnection.tsx
Redeemer.SocialFlow.Web/src/components/LinkedIn.test.tsx
Redeemer.SocialFlow.Web/src/main.tsx
Redeemer.SocialFlow.Web/src/test/setup.ts
scripts/LocalDevelopment.psm1
scripts/Start-Local.ps1
docs/local-development.md
docs/linkedin-image-publication.md
docs/linkedin-v1-local.md
```

Les adaptations des tests historiques consistent à fournir explicitement leur administrateur simulé et à tenir compte du ticket OAuth et des nouvelles routes documentées. Les règles métier et les tests unitaires existants sont conservés.

## Exploitation et suite

La [procédure détaillée](phase-1-authentication.md#lancement-et-premier-compte) explique la compilation, l’initialisation manuelle de la nouvelle base, le lancement, la confirmation via boîte mail locale, le SMTP et les limites. L’application ne crée pas spontanément de base ou de premier compte.

Restent hors périmètre : validation SMTP réelle et recette manuelle complète dans un navigateur, configuration d’hébergement, isolation multi-tenant, migration des connexions sociales, facturation. `IWorkspaceAccessContext` renvoie `null` et prépare la phase 2 sans autoriser implicitement l’accès aux données historiques. La phase 2 n’est pas commencée.
