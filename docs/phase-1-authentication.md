# Phase 1 — Comptes et authentification

## Périmètre livré

Inscription, confirmation d’email obligatoire, connexion, profil, déconnexion, renvoi de confirmation, récupération et réinitialisation du mot de passe. Le frontend expose ces parcours avec chargement, erreurs et confirmations, sans stockage de mot de passe ou de jeton dans le stockage navigateur.

`Application/Accounts` contient les contrats. `Infrastructure/Accounts` contient `ApplicationUser`, le contexte EF Identity, le service de compte et l’envoi d’email. L’API compose l’authentification, l’autorisation et les protections HTTP. Le projet Domain et les règles métier historiques restent inchangés.

Les comptes utilisent une **base SQLite distincte** : `ConnectionStrings:Accounts` (`socialflow-accounts.db` par défaut). `SocialFlowDbContext`, les données et les migrations historiques ne sont pas modifiés. Aucune migration automatique n’est exécutée au lancement.

## Routes et autorisations

| Route | Méthode | Accès |
|---|---|---|
| `/api/auth/csrf` | GET | Public, HTTPS, émet un jeton de formulaire et un cookie antiforgery |
| `/api/auth/register` | POST | Public, HTTPS, CSRF, limite de débit |
| `/api/auth/login` | POST | Public, HTTPS, CSRF, limite de débit et verrouillage |
| `/api/auth/forgot-password` | POST | Public, HTTPS, CSRF, limite de débit |
| `/api/auth/resend-confirmation` | POST | Public, HTTPS, CSRF, limite de débit |
| `/api/auth/confirm-email` | POST | Public, HTTPS, CSRF, email et code |
| `/api/auth/reset-password` | POST | Public, HTTPS, CSRF, email, code et nouveau mot de passe |
| `/api/auth/me` | GET | Session Identity confirmée |
| `/api/auth/logout` | POST | Session Identity confirmée et CSRF |
| API métier, documents, IA, visuels, identités visuelles, statut/publication sociale | Toutes | Administration locale uniquement |
| `/api/dev/linkedin/connect-ticket` | POST | Administration locale uniquement |
| `/api/dev/linkedin/connect` | GET | Exception publique : HTTPS local, ticket administratif à usage unique, 10 minutes |
| `/api/dev/linkedin/callback` | GET | Exception publique : HTTPS local, state et cookie de navigateur à usage unique |
| `/api/dev/facebook/authorize`, `/callback` | GET | Exceptions publiques : contrôles locaux, ticket/state/cookie historiques conservés |
| OpenAPI et Scalar | GET | Development uniquement, administration locale |
| SPA compilée et ressources statiques | GET | Public ; aucune donnée métier embarquée |

La politique d’autorisation par défaut **et** la politique de repli exigent une identité administrative locale. Un compte confirmé reçoit `403` sur les routes métier ; un visiteur anonyme reçoit `401`. Les contrôleurs Development et les options d’activation des intégrations restent applicables. Une route inexistante peut aussi recevoir `401/403` avant le `404`, du fait de la politique de repli.

## Sessions et sécurité

- Cookie `__Host-SocialFlow.Session` : HttpOnly, Secure, SameSite=Lax, Path=/, aucun Domain, session non persistante, durée absolue maximale de 8 heures, sans renouvellement glissant.
- Vérification du security stamp et de l’email confirmé à chaque requête authentifiée. Déconnexion et réinitialisation révoquent les sessions existantes. La déconnexion concerne donc tous les navigateurs du compte.
- Cookie antiforgery `__Host-SocialFlow.Antiforgery` : HttpOnly, Secure, SameSite=Strict. Les mutations exigent `X-CSRF-TOKEN`. Le client obtient un jeton neuf avant chaque mutation, notamment après changement d’identité.
- Mot de passe : 12 à 128 caractères, majuscule, minuscule, chiffre et symbole ; hachage ASP.NET Core Identity. Verrouillage de 15 minutes après 5 échecs sur un compte confirmé.
- Plafond partagé de 20 tentatives de compte par adresse IP et par fenêtre de 15 minutes, sans file d’attente. Réponse `429` au-delà.
- Réponses génériques identiques pour une adresse connue/inconnue lors d’inscription, récupération et renvoi de confirmation. La connexion ne distingue pas mauvais mot de passe, compte inconnu, non confirmé ou verrouillé.
- Liens de confirmation/réinitialisation limités à une heure. Les codes sont placés dans le fragment du lien puis retirés de la barre d’adresse par le frontend, et envoyés dans le corps du POST. Aucune confirmation automatique au chargement.
- Erreurs HTTP normalisées avec ProblemDetails ; aucun mot de passe, cookie ou code de récupération n’est journalisé par le code ajouté. Échec d’envoi : diagnostic constant `ACCOUNT_EMAIL_DELIVERY_FAILED`, réponse publique générique.

## Administration locale transitoire

Ce n’est pas un rôle attribuable à un utilisateur Identity. Le schéma `LocalAdministration` exige simultanément : environnement Development, `LocalAdministration:Enabled=true`, HTTPS, adresse distante loopback, Host local, absence d’en-têtes de proxy, et secret administratif distinct des secrets fournisseurs. Le secret doit avoir au moins 32 caractères et est comparé par empreinte en temps constant.

Le lanceur existant injecte le secret DPAPI dans les seuls processus API/Vite, active ce mode et la boîte mail locale. Le navigateur ne reçoit jamais le secret. La passerelle Vite `/local-visuals` ajoute le secret côté serveur pour une liste fermée de routes métier, contrôle Origin/Host/adresse locale et exige `X-SocialFlow-Local: 1`. Elle ne relaie ni les cookies Identity ni les routes de compte. L’exemption antiforgery du serveur concerne uniquement cette identité administrative avec preuve explicite par en-tête, jamais une session utilisateur.

Sur `http://127.0.0.1:5173`, cliquer **Administration locale**. Cette capacité repose sur la confiance dans le compte Windows et ses processus locaux. Ne pas publier Vite ni exposer cette passerelle sur un réseau. Un utilisateur ordinaire distant n’y a pas accès. La SPA compilée ne présente pas ce bouton.

Les callbacks sociaux reviennent à `http://localhost:5173/#local` ; cliquer de nouveau sur Administration locale. Le démarrage LinkedIn requiert désormais un ticket obtenu par le bouton protégé, et non l’ancienne URL publique seule. Les connexions sociales existantes ne sont ni déplacées ni converties en connexions de compte.

Les workers Facebook et LinkedIn sont forcés à `false` au démarrage de l’API pendant cette phase, y compris si les anciennes user-secrets les activent. Les tests de publication utilisent exclusivement leurs fournisseurs simulés. La publication manuelle reste soumise aux options et confirmations historiques, dans l’administration locale.

## Migration créée, non appliquée à une base réelle

`Infrastructure/Accounts/Migrations/20261025112137_InitialAccounts` crée les tables Identity (utilisateurs, claims, logins, tokens) et un index unique sur l’email normalisé. Le snapshot est propre à `AccountDbContext`. Aucun rôle ni workspace n’est créé. La fabrique design-time évite de démarrer l’application pour générer la migration.

La migration a été appliquée uniquement aux bases temporaires des tests. La base métier réelle `Redeemer.SocialFlow.Api/socialflow.db` conserve l’empreinte SHA-256 :

```text
79640AAD4C64564B5685522C7E345EC3EB5D0281FD639812C7D32A8E2F4024A3
```

## Lancement et premier compte

Depuis la racine du dépôt, avec .NET 10, Node/npm, dotnet-ef et le certificat HTTPS local approuvé :

```powershell
dotnet restore
npm.cmd --prefix Redeemer.SocialFlow.Web ci
npm.cmd --prefix Redeemer.SocialFlow.Web run build
```

**Étape manuelle de création de la nouvelle base de comptes — non exécutée dans cette livraison.** Après validation de son emplacement, exécuter la commande ci-dessous. Elle cible explicitement un nouveau fichier Identity, jamais `socialflow.db`. Si un chemin différent est choisi, configurer aussi `ConnectionStrings__Accounts` avec ce même chemin absolu pour le serveur.

```powershell
$accountsPath = Join-Path (Get-Location) 'Redeemer.SocialFlow.Api\socialflow-accounts.db'
dotnet ef database update --context AccountDbContext --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --connection "Data Source=$accountsPath"
```

Puis utiliser le [lanceur local](local-development.md) :

```powershell
# Une seule fois si le secret administratif n’est pas encore initialisé :
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1 -Initialize
# À chaque session :
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

1. Ouvrir `https://localhost:65474/#register`. L’API sert le build React depuis `Web/dist`. Recompiler le frontend après ses modifications ; Vite reste réservé à l’interface locale de développement.
2. Saisir une adresse email et un mot de passe conforme. Le premier compte est un compte ordinaire, sans privilège administratif.
3. Avec ce lanceur, l’email est déposé en `.eml` dans `%LOCALAPPDATA%\Redeemer.SocialFlow\AccountMail`, sous le profil Windows courant. Aucun SMTP externe n’est contacté. Ouvrir le fichier avec un lecteur d’email, suivre le lien, puis cliquer **Confirmer mon adresse**. Un lien expiré peut être renouvelé via **Renvoyer la confirmation**.
4. Se connecter sur `https://localhost:65474/#login`. Le profil apparaît, avec l’indication que les espaces de travail ne sont pas encore disponibles.
5. Tester si souhaité la récupération et la déconnexion. Les fichiers `.eml` contiennent des liens personnels : les conserver dans ce répertoire privé et les supprimer après utilisation. Ne pas les ajouter au dépôt.

Le lancement ne crée pas la base Identity. Tant que l’étape manuelle n’a pas été faite, les requêtes de compte nécessitant la persistance ne peuvent pas aboutir.

Pour un SMTP réel, configurer via environnement ou gestionnaire de secrets `Authentication:Email:Host`, `Port` (587 par défaut), `From`, `Username`, `Password`, désactiver `UseLocalPickup`, et définir `Authentication:PublicOrigin` sur l’origine HTTPS publique exacte, sans chemin. TLS SMTP est obligatoire. Aucun envoi SMTP réel n’a été testé ici. Le déploiement n’est pas réalisé dans cette phase.

## Vérifications et limites

Les tests de comptes utilisent SQLite temporaire, une boîte mail simulée et Data Protection éphémère. Les tests historiques d’API métier utilisent explicitement `BusinessApiFactory`, qui fournit un administrateur simulé sans enlever les filtres locaux préexistants. Les tests `AccountsApiTests` et `LocalAdministrationTests` utilisent les vrais schémas d’authentification ; ils ne passent pas par cette fabrique.

Commandes :

```powershell
dotnet test Redeemer.SocialFlow.UnitTests --no-restore
dotnet test Redeemer.SocialFlow.IntegrationTests --no-restore
npm.cmd --prefix Redeemer.SocialFlow.Web test
npm.cmd --prefix Redeemer.SocialFlow.Web run build
powershell.exe -NoProfile -File .\scripts\Test-LocalDevelopment.ps1
```

Résultats et liste exhaustive des fichiers : voir [le bilan de livraison](phase-1-delivery.md).

Limites assumées : pas de workspace, d’invitation, de rôle SaaS, de MFA ni de facturation. Pas de file d’email durable ; un échec d’envoi nécessite un renvoi. La limite de débit est en mémoire par processus et repart au redémarrage ; une infrastructure multi-instance nécessitera une protection partagée. Derrière un proxy, définir séparément une chaîne de confiance des proxies et la gestion HTTPS avant toute exposition. Prévoir la persistance, la sauvegarde et la protection du trousseau Data Protection pour l’hébergement cible ; le trousseau par défaut de l’hôte est utilisé actuellement. Une rotation/perte des clés invalide sessions et liens.

## Préparation de la phase 2

`IWorkspaceAccessContext` est disponible dans Application, avec une implémentation qui renvoie toujours `null`. Aucune correspondance entre un utilisateur et les données globales n’est inférée. La phase 2 devra définir les workspaces et leurs adhésions, appliquer le filtrage dans les requêtes et autorisations, décider de l’attribution des données historiques, puis traiter les connexions sociales. La restriction actuelle ne devra être remplacée qu’après validation de cette isolation. Aucun de ces travaux n’est commencé ici.
