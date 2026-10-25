# Facebook : expiration utilisateur non fournie

## Decision et perimetre

Le diagnostic observe est `expires_at=0`, `type=USER`, `is_valid=true`, application correspondante. Le code assimilait zero a une reponse inutilisable et provoquait le 502. Nous traitons maintenant **zero explicite comme absence de date connue**, jamais comme une garantie de validite illimitee. Champ absent, null, chaine, nombre negatif ou malforme restent refuses.

La documentation Meta courante (`debug_token` et guide des jetons longue duree) renvoie encore HTTP 429. Le [SDK officiel archive](https://github.com/facebookarchive/php-graph-sdk/blob/5.x/src/Facebook/Authentication/AccessTokenMetadata.php) traite zero separement des timestamps, mais ne prouve pas les garanties actuelles de Meta. La decision repose sur le diagnostic USER confirme et une politique locale de revalidation obligatoire, pas sur une hypothese de duree illimitee. Les jetons Page restent distincts et ne peuvent pas remplacer le jeton utilisateur dans cette verification.

## Modele et stockage

- `FacebookConnection.ExpiresAt` devient nullable : null = expiration du jeton non fournie.
- `DataAccessExpiresAt` est une date distincte : fin de l'acces aux donnees, pas expiration du jeton. Elle n'est plus copiee dans ExpiresAt.
- Toute echeance connue atteinte bloque l'utilisation, meme si une revalidation retourne ensuite zero ou omet une echeance.
- `data_access_expires_at`, lorsqu'il est fourni, reste strictement positif et futur. Zero ou une forme invalide sont refuses par prudence ; cela peut necessiter un diagnostic complementaire.
- Le JSON chiffre existant accepte toujours les anciennes dates. Le champ ajoute est optionnel ; aucune migration SQL, aucun changement de cle ou de purpose Data Protection. Les tests relisent les nouvelles connexions avec une nouvelle instance du stockage.

Les seules dependances applicatives a ExpiresAt sont le client Graph, l'adaptateur Facebook, le runtime Facebook et la selection de Page du controleur Development. Le worker partage le runtime et le cas d'usage durable ; aucun changement LinkedIn n'est necessaire.

## Politique avant une operation sensible

1. Lors du fallback OAuth, exiger `is_valid=true`, application exacte et **type USER present**. Accepter soit une date future valide, soit zero explicite ; conserver les controles de permissions et de Pages avant stockage.
2. Une connexion sans date ne permet pas de considerer automatiquement le runtime connecte : interroger Meta a chaque evaluation qui pourrait autoriser la publication. Une erreur rend `Connected=false`.
3. Revalider avant une selection de Page lorsque la date est inconnue.
4. Pour **chaque publication**, connue ou inconnue, interroger `debug_token` sans cache, puis verifier permissions et Pages/taches. Recontroler les echeances enregistrees et celles de la verification juste avant le POST.
5. Tout echec de verification avant envoi retourne Failed sans POST externe. Les regles durables de non-reessai restent inchangees ; une ambiguite apres envoi reste Indeterminate.

Le runtime expose `expirationKnown`, `dataAccessExpiresAt` et `revalidationRequired` sans secret. Le frontend existant tolere deja expiresAt=null ; aucun changement UI n'est inclus.

## Limites

Une verification distante n'est pas une reservation atomique : Meta peut revoquer un jeton entre verification et envoi. Aucun exactly-once ou maintien de validite future n'est garanti. La politique ajoute des appels Meta ; panne, limitation de debit ou reponse douteuse bloquent la publication. Le runtime peut interroger Meta lors de consultations repetees, sans cache intentionnel. Une operation durable marquee Failed n'est pas relancee automatiquement.

## Validation manuelle sans publication

1. Conserver les workers et la publication desactives. Redemarrer via `powershell.exe -NoProfile -File .\scripts\Start-Local.ps1`.
2. Demarrer un nouveau parcours Connecter Facebook ; ne pas reutiliser un code/state deja consomme.
3. Verifier la ligne sure `ExpiryState=zero; TokenType=USER; Validity=true; AppMatch=match`, puis le retour a l'interface sans 502.
4. Selectionner une Page puis actualiser le runtime. Attendre `expirationKnown=false`, `expiresAt=null`, `revalidationRequired=true` et une connexion confirmee en ligne. Ne pas copier de code, jeton, URL OAuth ou JSON Meta.
5. Redemarrer et verifier la relecture de la connexion. Si Meta est indisponible, la publication doit rester indisponible. Les tests simules couvrent ce refus sans publication reelle.
