# Publication LinkedIn personnelle — adaptateur Development

## API retenue (documentation consultée le 3 octobre 2026)

`POST https://api.linkedin.com/rest/posts`, texte seul, visibilité `PUBLIC`,
distribution `MAIN_FEED`. En-têtes :

- `Authorization: Bearer <jeton serveur>`
- `Content-Type: application/json; charset=utf-8`
- `LinkedIn-Version: 202609`
- `X-Restli-Protocol-Version: 2.0.0`

Le guide Share on LinkedIn associe le produit au scope `w_member_social` et pointe
encore vers UGC. La documentation Posts API indique qu'elle remplace UGC et autorise
la publication pour un membre via ce même scope. La version 202609 est active
(fin de support annoncée au 15 septembre 2027). Aucun accès organisation ou scope
de lecture supplémentaire n'est supposé. Les droits effectifs de l'application
restent soumis à l'activation du produit chez LinkedIn.

Références officielles :

- [Share on LinkedIn](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/share-on-linkedin)
- [Posts API et permissions](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api?view=li-lms-2026-09)
- [Versions actives](https://learn.microsoft.com/en-us/linkedin/marketing/integrations/migrations?view=li-lms-2026-09)
- [Identité du membre via userinfo](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2)

## Identité et contenu

`ILinkedInCredentialProvider` est une abstraction Infrastructure injectable. Son
implémentation locale lit le stockage OAuth existant protégé par Data Protection/DPAPI,
sans recopier le jeton dans une autre base. Elle refuse une connexion d'un autre ClientId.
L'adaptateur vérifie l'expiration avec TimeProvider et n'accepte que LinkedIn.

L'auteur personnel est `urn:li:person:{sub}`, construit depuis l'identité authentifiée
stockée par OAuth : le guide Share on LinkedIn renvoie à OIDC pour l'identité utilisée
dans cette Person URN. La destination durable doit être exactement cette URN, avec
comparaison ordinale ; un autre membre ou une organisation est refusé avant le POST.
Le `sub` reste associé au ClientId local, sans correspondance entre applications.

`commentary` reçoit exactement `PostPublicationRequest.Content`, issu du snapshot
durable, sans trim, ajout de titre, avertissement IA ou visualBrief. Aucun média n'est envoyé.
L'OperationId reste l'identité durable locale : aucun en-tête d'idempotence fournisseur
n'est inventé.

## Résultats et protection contre les doublons

- `Confirmed` : HTTP 201 et un unique `x-restli-id` non vide au format documenté
  `urn:li:share:{nombre}` ou `urn:li:ugcPost:{nombre}`.
- `Failed` : refus avant envoi (jeton absent/expiré, stockage illisible, plateforme ou
  destination incorrectes) ou rejet explicite 400/401/403/404/405/413/415/422/429.
- `Indeterminate` : timeout, annulation après envoi possible, erreur réseau, 5xx,
  408/409, redirection, succès sans identifiant utilisable ou autre réponse non concluante.

Aucun corps de réponse n'est lu ni journalisé. Les exceptions fournisseur/stockage
ne sont ni exposées ni persistées. Les loggers HttpClient sont retirés. Aucun retry
ou suivi automatique de redirection n'est installé.

L'adaptateur doit passer par `IPublishScheduledPost` : la prise en charge durable
précède le POST et empêche les répétitions, y compris après résultat indéterminé ou crash.
La confirmation et le statut Published restent sauvegardés par le parcours existant.
Un appel direct répété à IPostPublisher n'est pas idempotent. Aucune garantie exactly-once
chez LinkedIn, aucune réconciliation automatique ni renouvellement de jeton n'est ajouté.

## Activation explicite

Pour le parcours interface et worker, suivre la [procédure unique V1 locale](linkedin-v1-local.md).

Prérequis : terminer la [connexion OAuth locale](linkedin-oauth-local.md) avec
`openid profile w_member_social`. Depuis la racine du dépôt :

```powershell
dotnet user-secrets set "LinkedIn:PublicationEnabled" "true" --project .\Redeemer.SocialFlow.Api
dotnet run --project .\Redeemer.SocialFlow.Api --launch-profile Redeemer.SocialFlow.Api
```

La clé est absente/désactivée par défaut. Même à true, elle est ignorée hors Development.
Pour désactiver :

```powershell
dotnet user-secrets set "LinkedIn:PublicationEnabled" "false" --project .\Redeemer.SocialFlow.Api
```

Ces commandes ne sont pas exécutées par cette implémentation. Le branchement enregistre
IPostPublisher et IPublishScheduledPost ; le worker nécessite en plus LinkedIn:WorkerEnabled=true.
Aucun POST immédiat au démarrage. Le nouveau parcours Development fournit Provider `LinkedIn`,
la destination personnelle côté serveur et une OperationId stable. IPostPublisher ne reçoit
pas Provider ; le parcours hôte reste responsable de sélectionner cet adaptateur.

## Vérification

Tests HTTP simulés : contrat JSON, en-têtes, contenu fidèle, confirmation/ID, refus
locaux et HTTP, résultats ambigus, annulation, absence de relance. Tests SQLite :
intégration dans le parcours durable et aucun renvoi lors d'un appel répété.
Tests DI : activation explicite Development et aucun worker de publication par défaut.
Aucune connexion ni publication réelle n'a été effectuée.
