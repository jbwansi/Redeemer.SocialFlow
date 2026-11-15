# Facebook SaaS : configuration et validation locale

Le parcours conserve les sessions OAuth liées à l’utilisateur et au workspace, à usage unique et limitées à dix minutes. Seuls Owner/Admin peuvent lancer le parcours et choisir les Pages. Les jetons obtenus sont chiffrés avec un contexte propre au workspace et à la connexion. Aucun jeton historique n’est importé.

## Paramètres locaux (exemples fictifs)

Configurer avec .NET User Secrets pour le projet `Redeemer.SocialFlow.Api`, ou avec des variables d’environnement serveur (remplacer `:` par `__`). Ne pas copier les secrets dans appsettings, React ou un fichier versionné.

```json
{
  "Authentication:PublicOrigin": "https://localhost:65474",
  "SocialConnections:Providers:Facebook:Enabled": true,
  "SocialConnections:Providers:Facebook:UseLegacyClientCredentials": true,
  "SocialConnections:Providers:Facebook:UseLegacyLoginConfigId": true,
  "SocialConnections:Providers:Facebook:ApiVersion": "v26.0",
  "SocialConnections:Providers:Facebook:Scopes": "pages_show_list pages_read_engagement pages_manage_posts"
}
```

Ces références utilisent les clés serveur historiques `Facebook:AppId`, `Facebook:AppSecret` et `Facebook:LoginConfigId` déjà présentes, uniquement après opt-in. Elles ne lisent jamais la connexion globale ou ses jetons. Ne pas redéfinir leurs valeurs pour ce correctif.

Alternative indépendante, avec valeurs **fictives** :

```json
{
  "SocialConnections:Providers:Facebook:UseLegacyClientCredentials": false,
  "SocialConnections:Providers:Facebook:UseLegacyLoginConfigId": false,
  "SocialConnections:Providers:Facebook:ClientId": "123456789000001",
  "SocialConnections:Providers:Facebook:ClientSecret": "EXEMPLE_SECRET_FICTIF_A_REMPLACER",
  "SocialConnections:Providers:Facebook:LoginConfigId": "123456789000002"
}
```

Une paire SaaS partielle ne sera jamais complétée avec un secret historique. `LoginConfigId` explicite prévaut sur sa référence historique. Pour Facebook Login classique, supprimer LoginConfigId et désactiver sa référence : `Scopes` est alors envoyé. Avec Login for Business, seul `config_id` est envoyé ; les permissions sont à configurer dans Meta. Utiliser une configuration **User access token**, pas un jeton System User, non pris en charge ici.

`PermissionsApproved` n’est plus un prérequis Facebook : aucune valeur n’est activée automatiquement. La disponibilité signifie seulement que la configuration permet de commencer. Les droits proviennent des réponses `/me/permissions` et `/debug_token`, jamais des scopes demandés. Le trousseau `SocialConnections:Encryption` existant reste nécessaire ; ne pas le remplacer.

## Meta Developer Portal

Dans Facebook Login / Facebook Login for Business, Settings, ajouter à **Valid OAuth Redirect URIs** exactement :

`https://localhost:65474/api/social-oauth/1/callback`

Cette URL est calculée depuis `Authentication:PublicOrigin` et la valeur numérique Facebook `1`. Elle est distincte du callback historique `/api/dev/facebook/callback` qui reste inchangé.

Dans la configuration Login for Business référencée, vérifier le type User access token et les permissions de Pages : `pages_show_list` pour découvrir ; `pages_read_engagement` et `pages_manage_posts` pour la capacité de publication. Vérifier les accès réellement approuvés dans le tableau de bord Meta ; le code ne peut pas confirmer l’état App Review à partir des identifiants. En mode développement, utiliser un compte autorisé par l’application et disposant des tâches nécessaires sur la Page.

## Expiration et capacités

Chaque nouveau jeton utilisateur et chaque jeton de Page sont inspectés côté serveur : validité, application, type, échéance du jeton et échéance d’accès aux données. Une échéance positive dépassée, un champ obligatoire absent ou un format invalide bloque le parcours. Un zéro explicitement renvoyé n’invente aucune durée : la validité est vérifiée en ligne, puis de nouveau à la sélection et à la validation de la connexion. Les échéances finies connues sont conservées de façon prudente, avec la plus proche comme limite. Les jetons des Pages sont aussi vérifiés contre l’identifiant de la Page.

La découverte n’exige pas la permission de publier : une Page peut être enregistrée sans cette capacité. La publication est marquée autorisée seulement si les permissions confirmées comprennent les deux scopes de publication et si la réponse des Pages indique `CREATE_CONTENT`.

**Limite volontaire :** la documentation officielle consultée décrit `CREATE_CONTENT` comme tâche de création de contenu mais ne permet pas de confirmer l’équivalence de `PROFILE_PLUS_CREATE_CONTENT`. Ce dernier seul ne confère donc pas la capacité de publication SaaS. La Page reste sélectionnable. L’adaptateur historique accepte déjà cet alias ; son comportement n’est pas modifié. Cette harmonisation reste à terminer après confirmation officielle, sans élargissement implicite des droits.

## Recette manuelle ultérieure

1. Configurer les références locales et le callback dans Meta ; ne pas activer de worker.
2. Se connecter comme Owner/Admin, sélectionner le workspace puis « Connecter Facebook ».
3. Après consentement réel, vérifier les Pages proposées, leurs limitations et sélectionner celles voulues.
4. Vérifier qu’elles apparaissent uniquement dans ce workspace, sans jeton dans les réponses navigateur.
5. Essayer le parcours avec un second utilisateur/workspace : une session étrangère doit être refusée. Vérifier qu’Editor/Viewer ne voient aucun détail de configuration et ne peuvent gérer les connexions.
6. Utiliser « Vérifier » pour contrôler à nouveau les jetons. Reconnecter pour renouveler les droits. Ne publier aucun contenu.

Cette recette n’a pas été exécutée avec Meta pendant l’implémentation. Aucun paramètre local réel n’a été changé, aucun worker n’a été démarré et aucune migration n’est nécessaire pour ce correctif.

## Références officielles consultées le 3 novembre 2026

- [Facebook Login for Business](https://developers.facebook.com/docs/facebook-login/facebook-login-for-business/) : configuration et `config_id`, parcours manuel, distinction User/System User.
- [Debug token](https://developers.facebook.com/docs/graph-api/reference/v26.0/debug_token/) : `is_valid`, `app_id`, `expires_at`, `data_access_expires_at`.
- [Long-lived tokens](https://developers.facebook.com/docs/facebook-login/guides/access-tokens/get-long-lived/) : durée relative `expires_in`.
- [Pages API overview](https://developers.facebook.com/docs/pages-api/overview/) et [Getting started](https://developers.facebook.com/docs/pages-api/getting-started/) : tâche `CREATE_CONTENT` et autorisations de Pages.

Les pages ont été lues directement en HTTP après des erreurs 429 du moteur de navigation. L’acceptation prudente d’une échéance zéro après vérification en ligne est une politique locale ; elle ne signifie pas une validité garantie illimitée.

## Fichiers et validation

- `Redeemer.SocialFlow.Infrastructure/Social/FacebookWorkspaceOAuthProvider.cs` : adaptateur Facebook, configuration explicite, inspection des jetons.
- `Redeemer.SocialFlow.Infrastructure/Social/WorkspaceOAuthProviders.cs` : points d’extension Meta, vérification des credentials de Pages.
- `Redeemer.SocialFlow.Infrastructure/Social/SocialConnections.cs` : diagnostics selon le rôle et revérification avant sélection.
- `Redeemer.SocialFlow.Web/src/pages/SocialConnections.tsx` : description accessible du motif de désactivation.
- `Redeemer.SocialFlow.UnitTests/FacebookWorkspaceOAuthTests.cs` et `SocialConnectionSecurityTests.cs` : configuration, consentement réel simulé, expirations, non-régression LinkedIn/Instagram.
- `Redeemer.SocialFlow.IntegrationTests/Api/FacebookSaasIsolationTests.cs` et `SocialConnectionTests.cs` : isolation utilisateur/workspace, rôles, secrets, expiration et rejeu du state.
- `Redeemer.SocialFlow.Web/src/pages/SocialConnections.test.tsx` : diagnostic Facebook et restriction des détails aux gestionnaires.
- Ce guide ; assets React régénérés dans `Redeemer.SocialFlow.Web/dist`.

Résultats : 24 tests unitaires ciblés réussis ; 4 cas d’intégration sur SQLite en mémoire réussis ; 12 tests React réussis ; compilation backend et `npm run build` réussies.

La suite API `WorkspaceIsolationTests` a également été exécutée : ses 39 cas échouent à l’initialisation, avant leurs assertions, sur `PendingModelChangesWarning`. Le modèle LinkedInAutomation préexistant n’a pas encore sa migration. Ce correctif ne modifie pas ce modèle, ne supprime pas l’avertissement et ne change pas la fixture de migration. Les nouveaux tests en mémoire vérifient le service actuel mais ne remplacent pas cette suite API : elle devra être rejouée une fois le chantier de migration LinkedIn résolu.
