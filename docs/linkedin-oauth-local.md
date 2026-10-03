# Connexion LinkedIn personnelle — V1 locale

Les trois routes `GET /api/dev/linkedin/connect`, `callback` et `status` sont
enregistrées uniquement en Development (404 dans les autres environnements).
La connexion OAuth ne publie rien et ne démarre aucun worker. L'adaptateur de publication
dispose d'une [activation séparée et explicite](linkedin-publication-local.md).

## Configuration

Dans le portail développeur LinkedIn, vérifier l'accès aux produits **Sign In with
LinkedIn using OpenID Connect** et **Share on LinkedIn**, puis enregistrer exactement :

`https://localhost:65474/api/dev/linkedin/callback`

Cette adresse correspond au profil `Redeemer.SocialFlow.Api` de `launchSettings.json`
(HTTPS 65474, HTTP 65475, environnement Development). Connect exige HTTPS.
Les scopes demandés sont exactement `openid profile w_member_social`, sans `email`.

Depuis la racine du dépôt, commandes PowerShell avec valeurs fictives :

```powershell
dotnet user-secrets set "LinkedIn:ClientId" "CLIENT_ID_FICTIF" --project .\Redeemer.SocialFlow.Api
dotnet user-secrets set "LinkedIn:ClientSecret" "CLIENT_SECRET_FICTIF" --project .\Redeemer.SocialFlow.Api
dotnet dev-certs https --trust
dotnet run --project .\Redeemer.SocialFlow.Api --launch-profile Redeemer.SocialFlow.Api
```

User Secrets conserve la configuration hors Git, selon le `UserSecretsId` existant.
Ce fichier de configuration locale n'est pas un coffre chiffré.
Ouvrir ensuite `https://localhost:65474/api/dev/linkedin/connect` dans le navigateur.
Après consentement et échange serveur réussi, le callback redirige vers `status`.

## Comportement et protection

- State aléatoire de 256 bits, dix minutes, consommé atomiquement une seule fois,
  lié à un nonce de navigateur indépendant dans un cookie Secure, HttpOnly, SameSite=Lax.
  Les autorisations en attente sont en mémoire : un redémarrage impose de recommencer.
- Échange du code côté serveur, puis identité `sub` obtenue par `/v2/userinfo` avec
  le jeton Bearer. Aucun ID token non validé n'est interprété. Ce flux connecte un
  compte au serveur local ; il n'authentifie pas les utilisateurs de SocialFlow.
- Une connexion personnelle par utilisateur Windows local. Le jeton, l'identité,
  le ClientId et l'expiration sont chiffrés par ASP.NET Data Protection dans
  `%LOCALAPPDATA%\Redeemer.SocialFlow\LinkedIn\connection.protected`.
  Le trousseau adjacent `keys` est protégé par DPAPI pour l'utilisateur Windows courant.
  L'écriture remplace atomiquement le fichier ; jeton et clés survivent au redémarrage.
  Hors Windows, l'enregistrement échoue sans stocker de jeton en clair.
- Expiration calculée depuis `expires_in`, sans durée supposée ni renouvellement.
  Un échec de reconnexion conserve la connexion précédente. La perte des clés exige
  une reconnexion. Le statut est local : il ne détecte pas une révocation chez LinkedIn.
- Aucune donnée fournisseur sensible ne figure dans les réponses ou diagnostics.
  Les loggers du client HTTP sont supprimés ; les logs ASP.NET Core sous Warning
  sont filtrés en Development pour éviter de journaliser le code dans l'URL du callback.
  Ne pas ajouter de journalisation brute des requêtes/réponses à ce flux.
- V1 locale, mono-processus, sans isolation multi-utilisateur. Les endpoints ne sont
  pas destinés à exposer un serveur Development sur Internet.

## Réponses

- Connect : 302 vers LinkedIn ; 400 sans HTTPS ; 503 sans configuration.
- Callback : 302 vers status après succès ; 401 state invalide/expiré ;
  400 refus ou code absent ; 502 échec fournisseur/stockage, message générique.
- Status : 200 avec uniquement `connected`, `state` et `expiresAt` ;
  `state` vaut `disconnected`, `connected` ou `expired`. Stockage illisible : 503.
  Les réponses ne sont pas mises en cache. L'annulation de la requête donne 499.

```json
{
  "connected": true,
  "state": "connected",
  "expiresAt": "2030-01-01T01:00:00+00:00"
}
```

L'identité reste côté serveur. Son utilisation par l'adaptateur est décrite dans
[la documentation de publication](linkedin-publication-local.md) ; aucune capacité
d'idempotence fournisseur n'est supposée.

## Diagnostic d'un callback 502

En Development, un événement Warning `LinkedIn OAuth` contient uniquement l'étape,
le nom du type d'exception, le statut HTTP éventuel et un code fixe. Aucune exception
complète, aucun message fournisseur, corps, paramètre du callback ou jeton n'est enregistré.
Le statut peut être 200 lorsqu'une réponse HTTP réussie échoue ensuite à la validation.

| Code | Point à examiner |
| --- | --- |
| `LI_CONFIG_MISSING` | Configuration absente (503, pas 502) |
| `LI_TOKEN_TRANSPORT` | Transport ou timeout de l'échange |
| `LI_TOKEN_HTTP` | Statut non réussi de l'échange |
| `LI_TOKEN_RESPONSE` | JSON, jeton ou durée absents/invalides |
| `LI_TOKEN_SCOPE_ABSENT` | Champ absent : scopes demandés retenus selon RFC 6749 §5.1 |
| `LI_TOKEN_SCOPE_FORMAT` | Champ présent mais format invalide |
| `LI_TOKEN_PERMISSION_MISSING` | Permission requise absente de la liste explicite |
| `LI_TOKEN_EXPIRY` | Calcul de l'expiration hors limites |
| `LI_USERINFO_TRANSPORT` | Transport de userinfo |
| `LI_USERINFO_HTTP` | Statut non réussi de userinfo |
| `LI_USERINFO_RESPONSE` | JSON/identité invalides ou connexion déjà expirée |
| `LI_STORAGE_SAVE` | Protection DPAPI, sérialisation ou écriture du fichier |

Ces diagnostics ne changent ni les validations ni la réponse publique générique.
Un nouveau parcours de connexion est nécessaire après échec : le state reste à usage unique.

### Formats de scope

La requête d'autorisation conserve les scopes séparés par des espaces, encodés dans
l'URL. La documentation du endpoint token décrit également une chaîne avec espaces
URL-encodés ; celle d'introspection décrit une chaîne séparée par des virgules.
Le parseur token accepte les espaces (dont `%20` et `+`) et, par compatibilité, les
virgules, sans prétendre que la documentation token impose ce dernier format.
Les permissions sont comparées par égalité ordinale sensible à la casse.
Une valeur nulle, vide ou malformée n'est pas assimilée à un champ absent.

Selon RFC 6749 §5.1, un champ scope omis signifie que le périmètre reste celui demandé.
Cette règle s'applique ici car le code vient exclusivement du parcours à state validé
et aux trois scopes fixes. Aucune introspection supplémentaire n'est nécessaire dans
ce cas. Un futur import de jeton/périmètre inconnu devra vérifier les permissions via
`POST https://www.linkedin.com/oauth/v2/introspectToken` (formulaire serveur contenant
client_id, client_secret et token), vérifier active et les permissions exactes.
Une liste explicite insuffisante est immédiatement refusée, sans repli ni stockage.

## Documentation officielle consultée

- [OAuth Authorization Code Flow](https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow)
- [OpenID Connect et userinfo](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2)
- [Accès à w_member_social](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/share-on-linkedin)
- [Introspection LinkedIn : scope séparé par virgules](https://learn.microsoft.com/en-us/linkedin/shared/authentication/token-introspection)
- [RFC 6749 §5.1 : scope facultatif si identique à la demande](https://www.rfc-editor.org/rfc/rfc6749.html#section-5.1)

Les tests `LinkedInOAuthTests` remplacent intégralement le transport HTTP LinkedIn
et utilisent un répertoire temporaire avec DPAPI, sans connexion réelle.
