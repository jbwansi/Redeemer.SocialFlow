# Demarrage local Windows (.NET + Vite)

Depuis la racine du depot, avec Windows PowerShell 5.1, .NET 10 et Node/npm installes.

## Premiere initialisation

Installer une fois les dependances frontend : `npm --prefix Redeemer.SocialFlow.Web ci`.

```powershell
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1 -Initialize
```

Cette commande ne demarre aucun serveur. Elle genere une seule fois 32 octets cryptographiquement aleatoires (256 bits), stockes avec DPAPI CurrentUser dans `%LOCALAPPDATA%\Redeemer.SocialFlow\LocalDevelopment\visual-admin.dpapi`. Le repertoire autorise uniquement le compte Windows courant et SYSTEM. Relancer l'initialisation conserve le secret existant ; un fichier corrompu provoque un refus, pas une regeneration silencieuse.

Le certificat ASP.NET Core existant doit etre valide et approuve. Le script utilise `dotnet dev-certs https --check --trust`, puis exporte uniquement sa partie publique en PEM dans le meme repertoire. Il ne cree ni n'approuve de certificat. Si la verification echoue, executer separement `dotnet dev-certs https --trust` et approuver l'operation Windows, puis relancer l'initialisation. L'export PEM sans option de mot de passe ne contient aucune cle privee, selon la [documentation Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

Si la politique PowerShell de votre organisation interdit les scripts, suivre sa procedure d'autorisation ; le lanceur ne contourne pas cette politique.

## Demarrage quotidien : une commande

```powershell
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

Arreter d'abord les instances API/Vite lancees par Visual Studio ou d'autres terminaux. Le lanceur refuse les ports deja occupes (65474, 65475, 5173), ce qui evite de melanger des instances utilisant des secrets differents.

Il demarre l'API dans son repertoire habituel, avec `dotnet run --no-launch-profile`, puis Vite sur `http://127.0.0.1:5173`. L'API conserve ses URL locales `https://localhost:65474` et `http://localhost:65475`. Le callback OAuth HTTPS ne change pas. Les user-secrets .NET habituels sont toujours charges en Development ; aucune configuration Meta/OpenAI n'est reecrite.

Le meme secret est fourni exclusivement dans l'environnement des deux processus enfants. Il n'est ni un argument de commande, ni une variable VITE_*, ni une valeur persistante de l'environnement Windows. Le terminal parent n'est pas modifie. `SOCIALFLOW_VISUAL_GATEWAY_ENABLED=true`, `NODE_EXTRA_CA_CERTS` et les environnements Development sont prepares automatiquement. Le certificat public est reexporte a chaque demarrage pour suivre son renouvellement. Le proxy `/api` et la passerelle verifient TLS ; `NODE_TLS_REJECT_UNAUTHORIZED=0` est refuse.

Ouvrir l'adresse Vite indiquee. Garder ce terminal ouvert ; Ctrl+C arrete les processus lances et leurs descendants. Si un serveur s'arrete, le lanceur arrete l'autre. En cas de fermeture brutale du terminal, arreter les eventuels processus restants avant de relancer.

Ce lanceur desactive les workers Facebook et LinkedIn dans ses processus uniquement. Il n'active aucune publication, integration OAuth ou fonctionnalite visuelle : leurs autres options existantes restent applicables. Il ne lance aucune migration, connexion OAuth ou publication. Pour deboguer l'API, attacher Visual Studio au processus deja lance plutot que demarrer une seconde API.

## Rotation explicite

Arreter les deux serveurs, puis :

```powershell
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1 -RotateSecret
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

La rotation remplace atomiquement le fichier chiffre ; l'ancien secret ne doit plus etre utilise. Elle ne modifie ni les jetons OAuth ni les secrets des fournisseurs. DPAPI lie le stockage au compte Windows et a sa protection locale : apres migration vers un autre compte/machine, reinitialiser ou effectuer une rotation explicite. Un administrateur ou un processus compromis sous le meme compte peut acceder aux secrets en memoire : DPAPI protege le stockage, pas une session compromise.

Ne jamais copier le secret dans `.env`, launchSettings, un transcript, une capture d'environnement ou un log de diagnostic. Les protections Git couvrent aussi les copies `*.dpapi`, `*.pem`, `*.key` et `.local-development/`.

## Verifications sans demarrer les applications

```powershell
powershell.exe -NoProfile -File .\scripts\Test-LocalDevelopment.ps1
npm --prefix Redeemer.SocialFlow.Web test
npm --prefix Redeemer.SocialFlow.Web run build
dotnet build Redeemer.SocialFlow.slnx --no-restore -c Release
```

Les tests PowerShell utilisent un repertoire temporaire, DPAPI et des commandes certificat simulees. Ils ne touchent ni au certificat reel, ni a la configuration locale persistante, et ne demarrent aucun worker.

## Diagnostics OAuth dans la console

Le lanceur redirige et lit simultanement stdout et stderr de chaque serveur. Les lignes portent le prefixe `[API]` ou `[Vite]`. Aucune copie dans un fichier de logs n'est creee. Les anciennes instances doivent etre arretees pour utiliser ce nouveau relais.

Pour consulter les diagnostics en direct, demarrer normalement :

```powershell
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

`dotnet run --no-launch-profile` compile les sources actuelles avant de lancer l'API (aucun `--no-build`). Les erreurs de compilation et de demarrage sont maintenant visibles. Le PID affiche est celui du lanceur dotnet ; le processus API enfant peut avoir un autre PID. Le controle des ports refuse une instance preexistante, sans la tuer. Pour verifier le proprietaire du port, sans afficher ses arguments ou son environnement :

```powershell
Get-NetTCPConnection -State Listen -LocalPort 65474 |
    ForEach-Object { Get-Process -Id $_.OwningProcess } |
    Select-Object Id, ProcessName, StartTime, Path
```

Lors du prochain essai OAuth manuel, commencer un nouveau parcours puis chercher les lignes `[API]` contenant `Facebook OAuth`, `Facebook HTTP` et `Facebook callback`. `Stage` localise l'etape (`code_exchange`, `token_extension`, `debug_token`, `permissions`, `pages` ou `local_storage`). `Category` classe l'echec ; `ExceptionType` distingue les exceptions sans exposer leur message. Les diagnostics de reponse ajoutent `Field`/`Reason`, et ceux du fournisseur le statut HTTP et les codes Meta numeriques lorsqu'ils sont disponibles.

Le lanceur active Information pour FacebookGraphClient (classifications des metadonnees) et Warning pour FacebookOAuthController, dans le fournisseur Console uniquement. Il n'active pas les logs HTTP detailles. Ne pas ajouter l'objet exception, son message brut ou une URL de callback aux logs : ils peuvent contenir un code, un jeton ou une reponse sensible. La reponse HTTP reste generique. Partager uniquement ces diagnostics filtres, jamais une capture complete d'une requete OAuth. Le correctif rend le prochain 502 observable ; il ne prejuge pas de sa cause sans ces diagnostics.
