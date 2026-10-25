# Activation LinkedIn par workspace

## Mise à jour : permission de publication personnelle

L'utilisateur confirme les produits Share on LinkedIn et OpenID Connect accordés à l'application. La configuration locale demande désormais `openid profile w_member_social`. Les organisations restent désactivées. Les sections ci-dessous décrivent l'activation initiale avec les scopes minimaux.

Après redémarrage de l'API avec le code recompilé, ouvrir le workspace comme Owner/Admin, cliquer **Reconnecter** sur le profil, autoriser LinkedIn, puis sélectionner le même profil et cliquer **Ajouter les comptes sélectionnés**. L'enregistrement met à jour la connexion existante dans ce workspace sans doublon et sans affecter les autres workspaces. Le bouton Vérifier / renouveler ne remplace pas le consentement OAuth pour obtenir un nouveau droit.

La permission de publication est affichée seulement si `w_member_social` figure dans les scopes retournés par LinkedIn. Sur une nouvelle connexion, l'absence du champ scope ne vaut plus confirmation des scopes demandés : la capacité reste non confirmée. Lors d'un renouvellement sans champ scope, seuls les scopes précédemment confirmés sont conservés. Aucun appel de publication n'est effectué pour tester cette capacité.

Le changement de configuration seul ne modifie pas les droits stockés des connexions existantes. L'utilisateur doit terminer la reconnexion et l'enregistrement. L'interface explique cette étape et recharge les droits après la réussite de l'enregistrement.

## Diagnostic et correction du 25 octobre 2026

Le profil de lancement local utilise Development et `https://localhost:65474`. Les secrets utilisateur de l'API contiennent déjà `LinkedIn:ClientId` et `LinkedIn:ClientSecret`. Aucun secret n'est reproduit ici. L'ancien parcours demande `openid profile w_member_social` et utilise `/api/dev/linkedin/callback` ; sa connexion est conservée séparément avec protection DPAPI.

Le bouton multi-tenant dépend de la disponibilité du fournisseur **et** du chiffrement SaaS. Les paramètres `SocialConnections:Providers:LinkedIn` et la clé AES étaient absents des secrets locaux. Le code ne réutilisait pas les identifiants historiques. Les permissions Owner/Admin restent contrôlées côté serveur ; Editor/Viewer restent en lecture seule.

Correction : option explicite `UseLegacyClientCredentials` pour référencer la paire historique, sans accéder au magasin de jetons historique. Une paire multi-tenant partiellement renseignée n'est jamais complétée avec le secret d'une autre application. Des messages LinkedIn précisent les éléments manquants et les capacités demandées. Aucun changement de comportement Facebook/Instagram.

Configuration enregistrée dans les secrets utilisateur serveur, hors dépôt :

- `SocialConnections:Providers:LinkedIn:Enabled=true`
- `SocialConnections:Providers:LinkedIn:UseLegacyClientCredentials=true`
- `SocialConnections:Providers:LinkedIn:PermissionsApproved=true`
- `SocialConnections:Providers:LinkedIn:Scopes=openid profile`
- `SocialConnections:Providers:LinkedIn:OrganizationsEnabled=false`
- Une clé AES-256 aléatoire et son identifiant actif dans `SocialConnections:Encryption`, sans remplacer une clé active existante.

Le choix minimal repose sur l'intégration historique annoncée fonctionnelle. `PermissionsApproved` est une déclaration locale de configuration, pas une lecture du portail LinkedIn. Les permissions actuellement provisionnées et le nouveau callback n'ont pas été vérifiés dans le portail. Le consentement et l'appel userinfo réels restent nécessaires ; aucune réussite de connexion n'est annoncée avant leur validation et la sélection enregistrée.

Redémarrer l'API avec son profil Development habituel après compilation pour charger la configuration. Les secrets utilisateur .NET concernent le développement local ; pour Linux en production, fournir les mêmes paramètres via le gestionnaire de secrets du service. Conserver une sauvegarde sécurisée de la clé AES séparée de la base.

## Action dans LinkedIn Developer Portal

Dans l'application correspondant aux identifiants existants, onglet **Auth**, section **OAuth 2.0 settings**, paramètre **Authorized redirect URLs for your app**, ajouter exactement :

```text
https://localhost:65474/api/social-oauth/2/callback
```

Cette URL vient de `Authentication:PublicOrigin` (`https://localhost:65474`) et du chemin `/api/social-oauth/{platform}/callback`, où LinkedIn vaut 2. Conserver aussi l'ancien callback `https://localhost:65474/api/dev/linkedin/callback` pour l'administration locale. Ne pas utiliser le port Vite, une adresse HTTP ou un slash final supplémentaire.

Dans **Products**, vérifier que **Sign In with LinkedIn using OpenID Connect** est accordé. Dans **Auth / OAuth 2.0 scopes**, vérifier `openid` et `profile`. S'ils manquent, demander ce produit avant la recette. Leur absence effective ne peut pas être déduite des secrets locaux. [Documentation LinkedIn OIDC](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2).

La connexion minimale identifie le profil sans demander de publication. Pour une autorisation de publication sur profil, vérifier le produit **Share on LinkedIn** et le scope `w_member_social`, puis seulement l'ajouter au paramètre Scopes et refaire le consentement. [Produits et permissions LinkedIn](https://learn.microsoft.com/en-us/linkedin/shared/authentication/getting-access).

Les organisations restent désactivées : ne pas demander leurs scopes sans accès accordé. Leur activation séparée exige les permissions d'organisation documentées en phase 4 et une version API compatible. Aucun droit de page n'est déduit du droit de publication personnel.

## Recette manuelle, sans publication

1. Compiler/redémarrer l'API en Development et ouvrir `https://localhost:65474` avec le certificat local accepté.
2. Se connecter à SocialFlow comme Owner/Admin, choisir le workspace voulu et ouvrir « Réseaux sociaux ». Le bouton LinkedIn doit être actif ; le message indique que la publication n'est pas demandée et que les pages ne sont pas activées.
3. Cliquer « Connecter LinkedIn », vérifier le nom de l'application et consentir avec le compte LinkedIn souhaité. Un refus ne doit créer aucune connexion.
4. Au retour, vérifier le workspace, sélectionner le profil proposé puis cliquer « Ajouter les comptes sélectionnés ». Le simple retour OAuth n'enregistre pas automatiquement un candidat.
5. Vérifier le nom, le type Profile, l'état Connecté et l'absence de permission de publication. Rafraîchir la page pour confirmer la persistance. Aucune publication ne doit être déclenchée.
6. Pour un second profil, recommencer en changeant de session LinkedIn ; la session navigateur peut réutiliser le même compte. Le même profil ne crée pas de doublon actif dans le même espace. Vérifier l'isolation dans un autre workspace.
7. Vérifier la déconnexion locale et la reconnexion. Déconnecter n'annule pas le consentement global LinkedIn, qui peut servir à plusieurs espaces.

Cette recette crée des connexions réelles uniquement lorsqu'elle est exécutée par l'utilisateur. Elle n'a pas été exécutée lors de cette intervention, conformément à la contrainte de ne pas modifier les bases réelles.

Le state est aléatoire, lié à l'utilisateur, au fournisseur et au workspace serveur, expirant et consommé avant échange du code. Les rôles sont revérifiés au callback et à l'enregistrement. Les tests simulés vérifient les tentatives de rejeu, utilisateur/espace étrangers, changement de rôle, refus du consentement et sélection concurrente. Les secrets restent chiffrés côté serveur et absents des réponses au navigateur.

## Fichiers et validation

- `Redeemer.SocialFlow.Infrastructure/Social/WorkspaceOAuthProviders.cs` : référence explicite des identifiants, validation et messages LinkedIn.
- `Redeemer.SocialFlow.Infrastructure/Social/SocialConnections.cs` : diagnostic de disponibilité LinkedIn et chiffrement.
- `Redeemer.SocialFlow.UnitTests/SocialConnectionSecurityTests.cs` : tests de réutilisation, paire partielle, priorité et callback.
- Ce guide ; configuration locale dans les secrets utilisateur hors dépôt.

Tests ciblés réussis : 7 tests unitaires de sécurité, 27 tests d'intégration d'isolation/OAuth, 10 tests React de la page sociale et du retour OAuth dans WorkspaceShell. Aucun appel fournisseur réel, migration, modification de base réelle, publication, worker ou déploiement effectué.

