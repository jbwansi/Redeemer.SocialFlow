# Vérification de l’expiration OAuth Facebook

> Evolution : les refus de zero et la fusion des echeances decrits ci-dessous documentent le correctif initial. La politique actuelle les remplace par une expiration inconnue explicite et une echeance de donnees distincte, avec revalidation obligatoire : voir [expiration utilisateur non fournie](facebook-unknown-expiry.md). Le type USER est maintenant obligatoire lors de debug_token.

## Correctif

L’extension du jeton pouvait répondre sans `expires_in` ni `expires`. Le client exigeait une durée relative et le callback retournait alors 502 avant la vérification des permissions.

Une durée présente reste strictement validée. Seulement si les deux champs sont absents, le client appelle `GET /v26.0/debug_token?input_token=...`, authentifié par le jeton applicatif `AppId|AppSecret` dans l’en-tête Authorization. La version existante est conservée.

La réponse doit contenir `data.is_valid: true`, l’`app_id` configuré (chaîne ou entier JSON comparé sans conversion flottante) et un `expires_at` entier Unix strictement futur. Aucune durée de substitution n’est créée. Les métadonnées ambiguës, incohérentes ou invérifiables sont refusées. La limite existante de 366 jours est conservée comme borne de rejet. Si présente, la date `data_access_expires_at` borne également la connexion ; une date nulle ou zéro est refusée. Les jetons sans expiration vérifiable ne sont donc pas pris en charge. L’horloge serveur doit être correcte.

Les permissions et Pages sont ensuite vérifiées comme auparavant, puis l’expiration est contrôlée à nouveau avant retour. Un échec conserve la réponse publique générique et ne remplace pas une connexion déjà enregistrée. Le stockage protégé reste inchangé.

## Confidentialité et documentation

`input_token` rend l’URL sensible. Le client enregistré supprime déjà tous les loggers HTTP, interdit les redirections et conserve la validation TLS. Les diagnostics ajoutés ne contiennent que des libellés fixes ; aucun message d’exception, corps HTTP, jeton ou secret. Les tests capturent aussi les logs du callback. Une éventuelle instrumentation externe, capture réseau ou télémétrie ajoutée ultérieurement devra exclure cette URL et l’en-tête Authorization.

Les pages courantes de documentation Meta n’étaient pas accessibles pendant cette intervention (429). Le mécanisme a été recoupé avec le [client OAuth du SDK officiel archivé](https://github.com/facebookarchive/php-graph-sdk/blob/5.x/src/Facebook/Authentication/OAuth2Client.php), ses [métadonnées de jeton](https://github.com/facebookarchive/php-graph-sdk/blob/5.x/src/Facebook/Authentication/AccessTokenMetadata.php) et son [jeton applicatif](https://github.com/facebookarchive/php-graph-sdk/blob/5.x/src/Facebook/FacebookApp.php). Cela ne valide pas un échange réel avec la version v26.0. Les formats non vérifiés restent refusés.

## Vérification manuelle, sans publication

1. Conserver la configuration locale existante : `Facebook:Enabled=true`, `Facebook:PublicationEnabled=false`, `Facebook:WorkerEnabled=false`. Aucun secret ou paramètre local n’est modifié par ce correctif.
2. Démarrer l’API en Development sur `https://localhost:65474` et le frontend avec la passerelle locale et le certificat approuvé décrits dans [le guide local](facebook-pages-local.md). Pour cet essai OAuth, ne pas activer la publication contrairement à l’exemple de publication du guide.
3. Dans Posts, choisir **Connecter Facebook**, puis **Ouvrir l’autorisation Facebook**. Commencer un nouveau parcours : un ancien state/code déjà consommé n’est pas réutilisable.
4. Autoriser les permissions demandées. Le callback enregistré doit être exactement `https://localhost:65474/api/dev/facebook/callback`.
5. Au retour, actualiser le statut : vérifier la connexion, son expiration et les Pages disponibles. La sélection d’une Page ne publie rien. Ne lancer aucune publication.
6. Si 502 persiste, relever uniquement les diagnostics fixes `Stage`, `Field`, `Reason` et `Category`. Ne copier ni URL complète du callback, ni jeton, ni corps de réponse Meta. Une expiration invérifiable doit toujours conduire à un refus.

Les tests automatisés utilisent exclusivement des handlers HTTP simulés ; aucun parcours OAuth réel n’a été lancé.

## Diagnostic cible de debug_token

La ligne `Facebook OAuth metadata: Stage=debug_token` expose uniquement des classifications locales :

| Champ | Valeurs possibles |
| --- | --- |
| ExpiryState | `missing`, `zero`, `unexpected_type`, `not_int64`, `negative`, `out_of_range`, `expired`, `beyond_policy`, `valid` |
| TokenType | `USER`, `PAGE`, `missing`, `unexpected_type`, `other` |
| Validity | `true`, `false`, `missing`, `unexpected_type` |
| AppMatch | `match`, `mismatch`, `missing`, `invalid_format` |

`unexpected_type` inclut null, chaine (meme numerique), booleen, objet et tableau. `not_int64` indique un nombre non lisible comme entier Int64 (fraction, notation exponentielle, depassement). `expired` inclut l'egalite avec l'instant courant ; `beyond_policy` signifie plus de 366 jours. `valid` decrit seulement l'expiration : les autres validations peuvent encore echouer. Le refus indique aussi `Field` et `Reason`, puis `Category=invalid_response` et `ExceptionType=JsonException`.

L'appel inspecte le jeton **utilisateur issu de l'extension**, avant `me/accounts`. Les jetons **Page** obtenus ensuite sont distincts. La politique reste inchangee : `type` peut etre absent, mais s'il est present il doit etre exactement `USER` ; `PAGE` est refuse ici. `is_valid` doit etre le booleen true, et `app_id` une chaine numerique ou un entier JSON correspondant exactement a l'application configuree. Aucun identifiant n'est journalise.

La reference Meta debug_token et le guide des jetons longue duree ont de nouveau renvoye HTTP 429. Le SDK officiel archive cite plus haut convertit les timestamps Unix mais conserve explicitement zero comme cas particulier. Cela ne confirme pas le comportement actuel de v26.0 pour chaque type de jeton, ni la nature du jeton effectivement recu. Aucune regle de duree des jetons Page n'est transposee au jeton utilisateur. Zero reste refuse, sans supposer une autorisation illimitee.

Redemarrer avec `scripts/Start-Local.ps1` pour voir cette ligne Information dans la console `[API]`. Ne pas copier le JSON Meta ou l'URL du callback : les classifications suffisent a distinguer la prochaine erreur.
