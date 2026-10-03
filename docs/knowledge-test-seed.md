# Seed manuel Knowledge Base (Development)

Depuis la racine du dépôt, dans PowerShell :

```powershell
Set-Location .\Redeemer.SocialFlow.Api
dotnet run --launch-profile Redeemer.SocialFlow.Api -- --seed-knowledge-test=true
```

Le profil existant sélectionne Development. La commande utilise exactement la
configuration `ConnectionStrings:SocialFlow` de l'API (y compris les éventuelles
surcharges locales), écrit le document de test puis quitte sans ouvrir de serveur
HTTP ni appeler OpenAI. Sans l'argument explicite, aucun seed n'est exécuté.
L'argument est refusé hors Development, avant tout accès à la base.

Avec la configuration du dépôt, `Data Source=socialflow.db` désigne
`Redeemer.SocialFlow.Api/socialflow.db` quand l'API est lancée depuis ce dossier.
Lancer l'API depuis ce même dossier avec le même profil et les mêmes surcharges
garantit la même base. Si l'API utilise un autre fichier, fournir son chemin absolu
aux **deux** commandes, par exemple :

```powershell
dotnet run --launch-profile Redeemer.SocialFlow.Api -- --seed-knowledge-test=true --ConnectionStrings:SocialFlow="Data Source=C:\chemin\socialflow.db"
dotnet run --launch-profile Redeemer.SocialFlow.Api -- --ConnectionStrings:SocialFlow="Data Source=C:\chemin\socialflow.db"
```

La base doit déjà contenir les tables Knowledge Base : aucune migration n'est
appliquée et aucun schéma n'est créé par le seed. Une erreur est renvoyée si elles
manquent. Ne pas exécuter plusieurs seeds simultanément.

Le titre réservé `TEST — Accompagnement des bénévoles` identifie le document.
S'il existe, rien n'est ajouté ni modifié, même après des modifications locales.
Sinon le document et ses deux chunks sont enregistrés dans une seule transaction
avec les factories Domain : langue `fr`, thème `accompagnement des bénévoles`,
statut Approved, actif, usage Generation, version 1, SourceType Other et
AuthorityLevel Low. Les chunks portent la section `Données fictives de test`.
Il n'y a aucune attribution réelle et ces données ne constituent pas une source
officielle ou vérifiée. Aucun SocialPost ni historique de génération n'est créé.
