# Frontend local HTTPS

Depuis la racine du dépôt, avec l'API déjà disponible :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-Frontend.ps1
```

Ou depuis `Redeemer.SocialFlow.Web` : `npm.cmd run dev`.
Ouvrir **https://localhost:5173** (pas `http://127.0.0.1:5173`).
Le lanceur refuse un port 5173 déjà occupé et ne démarre ni API, ni worker,
ni passerelle d'administration locale. Ctrl+C arrête le processus enfant.
Pour démarrer API et frontend ensemble :
`powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-Local.ps1`.
Ce dernier conserve son prérequis de configuration locale initialisée.

Le certificat ASP.NET Core existant doit être approuvé. Le lanceur vérifie sa
confiance avant export ; s'il manque, exécuter manuellement
`dotnet dev-certs https --trust`, accepter la demande système, puis relancer.
Il n'y a aucun repli HTTP ni désactivation de validation TLS.

Le certificat PEM et sa clé sont exportés dans
`%LOCALAPPDATA%\Redeemer.SocialFlow\LocalDevelopment`, hors dépôt, avec un ACL
limité à l'utilisateur Windows et SYSTEM. La clé non chiffrée requise par Vite
reste dans ce dossier privé ; elle n'est jamais affichée ou transmise au frontend.
Les variables `SOCIALFLOW_HTTPS_CERT` et `SOCIALFLOW_HTTPS_KEY` portent uniquement
les chemins côté Node. `NODE_EXTRA_CA_CERTS` fournit le certificat public au proxy.

Le proxy `/api` conserve sa cible `https://localhost:65474`, `changeOrigin: true`
et `secure: true`. Les cookies Secure/HttpOnly et le client CSRF restent inchangés.
L'authentification se fait sur le frontend via `/api/auth/...` ; les cookies de
localhost ne sont pas isolés par port. Les liens serveur et callbacks OAuth
conservent leur origine API existante. Aucun appel OAuth/Meta n'est nécessaire
pour valider HTTPS.

Vérifications locales réalisées : page Vite HTTP 200 sous TLS validé ;
`/api/auth/csrf` via le proxy HTTP 200, cookie Secure ; POST de connexion vide
sans CSRF rejeté HTTP 400, avec CSRF accepté jusqu'à la validation des champs
Email/Password (HTTP 400). Aucun identifiant réel envoyé et aucune session créée.
La validation interactive d'une session Owner/Admin existante reste à effectuer
dans le navigateur : ouvrir l'URL Vite, vérifier l'espace actif et les connexions
sociales, sans lancer de diagnostic Meta ni publication.
