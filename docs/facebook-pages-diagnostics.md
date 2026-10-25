# Diagnostic des Pages Facebook

## Visibilite des diagnostics (mise a jour)

Les lignes permission resources n'etaient emises que dans debug_token. Or ExchangeAsync n'appelle pas debug_token si l'extension fournit expires_in/expires valide. Information est deja active pour FacebookGraphClient par le lanceur. L'absence des lignes ne prouvait donc pas l'absence de granular_scopes. Aucun processus sur le port API n'etait disponible pendant cette verification pour identifier le binaire de la derniere tentative.

Desormais, ce chemin affiche `DebugTokenStatus=not_requested; Reason=relative_expiry_present`, puis trois lignes apres me/permissions :

```text
Facebook permission resources: Stage=permissions; Permission=pages_show_list; Granted=true; GrantedSource=me_permissions; GranularScopePresent=unknown; TargetIdsCount=unknown
Facebook permission resources: Stage=permissions; Permission=pages_manage_posts; Granted=true; GrantedSource=me_permissions; GranularScopePresent=unknown; TargetIdsCount=unknown
Facebook permission resources: Stage=permissions; Permission=pages_read_engagement; Granted=true; GrantedSource=me_permissions; GranularScopePresent=unknown; TargetIdsCount=unknown
```

Si debug_token est effectivement execute, Stage=debug_token et GrantedSource=debug_token_scopes ; les booleens sont `true`/`false`, les comptes numeriques, les valeurs absentes ou indeterminables explicitement `unknown` (au lieu de null). Ces nouvelles conventions remplacent les descriptions null et absence de ligne dans les sections historiques ci-dessous. Aucune metadata granulaire n'est inventee depuis me/permissions.

Redemarrer avec `powershell.exe -NoProfile -File .\scripts\Start-Local.ps1` pour recompiler puis faire un nouveau consentement. Si les lignes restent absentes malgre OAuthCompleted, verifier que cette instance recompilee et le niveau Information sont bien utilises ; ne pas conclure a un manque de ressources. Aucun nouvel appel Meta ni changement d'autorisation, de pagination ou de publication n'est ajoute.

Le champ historique `connected` signifie qu'une Page est selectionnee et utilisable par le parcours de publication. Une connexion OAuth stockee avec zero Page est donc possible, sans erreur OAuth. L'ancien affichage confondait ce cas avec un compte non connecte.

Le runtime distingue desormais `disconnected`, `expired`, `no-pages`, `select-page`, `connected` et `verification-required`. `connected` conserve sa signification pour les protections de publication. L'interface montre le compte connecte sans Page, masque le selecteur vide, propose Reconnecter Facebook et distingue une Page disponible non selectionnee. Une verification distante impossible ne presente plus une Page deja selectionnee comme simplement non selectionnee.

## Lire le parcours

Redemarrer via `powershell.exe -NoProfile -File .\scripts\Start-Local.ps1`. Les categories Facebook concernees sont activees au niveau Information, sans logs HTTP detailles.

1. `HasPagesShowList` et `HasPagesManagePosts` : permissions accordees. La permission pages_read_engagement reste egalement obligatoire. Une permission manquante provoque un refus, sans sauvegarder une nouvelle connexion.
2. `PagesFetchStatus=completed` : toutes les pages de pagination ont ete lues. `ReturnedPagesCount` compte les entrees Meta avant filtrage (doublons compris), `PagesCount` les Pages uniques retenues. `MissingPageTokenCount` et `UnsupportedTaskCount` expliquent les exclusions. `BatchesCount` compte les reponses Graph. Les URL next ne sont jamais suivies ; seuls les curseurs sont reutilises vers l'hote fixe.
3. `PagesFetchStatus=failed` : consulter Category, ExceptionType et les diagnostics HTTP numeriques. Une erreur reste propagee, jamais convertie en liste vide.
4. `OAuthCompleted=true` : echange et controles termines. `ConnectionPersisted=true` n'apparait qu'apres sauvegarde chiffree reussie. Les deux affichent uniquement PagesCount.
5. `Stage=runtime; ConnectionPersisted=True; PagesCount=...` : nombre relu depuis le stockage et retourne a l'interface. La passerelle relaie ce JSON sans filtrer les Pages.
6. Dans la console du navigateur en Development, `[Facebook] FrontendPagesCount=...` indique le nombre recu par React, sans noms, identifiants ou credentials.

Si ReturnedPagesCount=0, Meta a retourne une liste vide ; ce diagnostic seul ne dit pas pourquoi le compte n'a pas de Page accessible. Verifier dans Meta les acces du compte et les autorisations accordees a l'application, puis relancer manuellement OAuth. Si ReturnedPagesCount>0 et PagesCount=0, examiner les compteurs d'exclusion sans retirer les controles de securite.

Actualiser Facebook relit les Pages enregistrees ; cela ne relance pas me/accounts. Une nouvelle autorisation est necessaire pour enregistrer une nouvelle liste. Un ancien echec OAuth peut laisser l'ancienne connexion intacte : comparer les etapes de la meme tentative.

## Verification manuelle

Conserver publication et workers desactives. Effectuer un nouveau parcours OAuth, revenir au panneau puis actualiser. Comparer les compteurs aux six etapes ci-dessus. Attendre un message explicite si aucune Page n'est disponible, sinon selectionner une Page. Ne declencher aucune publication. Ne partager ni URL OAuth, ni JSON Meta, ni jeton ; seuls les compteurs et libelles fixes sont utiles.

Sans nouvelle observation Meta, il n'est pas possible d'attribuer la liste vide reelle a un manque de Pages ou a leur filtrage. Les corrections portent sur l'ambiguite d'etat et l'absence de diagnostics, pas sur une hypothese de droits Meta.

## Configuration Facebook Login for Business

Le diagnostic suivant a confirme `ReturnedPagesCount=0` et `BatchesCount=1` : Meta renvoie une premiere liste data vide sans suite exploitable, avant tout filtrage local. Les tests verifient aussi qu'une premiere liste vide avec un curseur next est poursuivie, qu'une erreur ne devient pas un resultat vide, et que le bearer de me/accounts est bien le jeton utilisateur final, distinct du jeton intermediaire et des jetons Page.

L'utilisateur a confirme l'existence d'une configuration Facebook Login for Business. Le code n'envoyait auparavant que scope, sans config_id. L'option serveur facultative `Facebook:LoginConfigId` permet maintenant d'utiliser cette configuration : config_id remplace scope dans l'URL d'autorisation. Le code, state, cookie, TLS, controles de jeton USER, permissions effectivement accordees et taches Page restent obligatoires. Sans cette option, le parcours classique reste identique. Une valeur presente mais non numerique rend la configuration invalide ; aucun repli silencieux n'est effectue.

Verifier dans Meta que cette configuration appartient a la meme application, utilise un jeton utilisateur compatible avec ce parcours et accorde les permissions Pages requises ainsi que la ressource voulue. Un droit disponible pour les tests ne prouve pas qu'il est accorde au jeton courant. Ne pas confondre l'identifiant de configuration avec l'identifiant d'application ou de Page.

Depuis la racine du depot (remplacer le marqueur par l'identifiant numerique de configuration, pas un secret) :

```powershell
dotnet user-secrets set "Facebook:LoginConfigId" "<IDENTIFIANT_CONFIGURATION_BUSINESS>" --project .\Redeemer.SocialFlow.Api
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

Une variable heritee `Facebook__LoginConfigId` est prioritaire sur User Secrets. Le lanceur ne remplace pas cette option. Aucun parametre local n'a ete lu ou modifie pendant le diagnostic.

Lancer un **nouveau** parcours OAuth. Attendre `AuthorizationFlow=business_configuration; ConfigIdPresent=True`. Les anciennes connexions ne recoivent pas retroactivement les nouvelles ressources. `granular_scopes`, lorsque present dans debug_token, produit des observations par permission : `GranularScopesShape`, `MatchingEntries`, `TargetEntriesCount`, `MissingTargetsCount`, `MalformedEntriesCount`. Aucun identifiant de ressource ni scope inconnu n'est journalise. Ces observations ne sont pas une nouvelle regle d'autorisation ; absence de target_ids ne prouve pas absence de ressources, et un compteur positif ne prouve pas que Redeemer Holding est incluse.

La [reference officielle Business Login](https://developers.facebook.com/docs/facebook-login/facebook-login-for-business/) et les references accounts/debug_token etaient inaccessibles (HTTP 429). Le [SDK officiel Meta](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/user.py) confirme l'edge GET accounts retourne comme Pages, mais ne permet pas d'inspecter les ressources de votre application. La prise en charge config_id corrige l'ecart constate avec la configuration Business confirmee ; elle ne demontre pas a elle seule la cause distante de la liste vide. Aucun scope supplementaire, endpoint de secours ou Page forcee n'est ajoute.

## Liste toujours vide avec config_id : faits et limites

Les logs fournis confirment le parcours Business, les trois permissions accordees selon me/permissions, la sauvegarde et une seule reponse accounts vide. Ils ne contiennent pas les granular_scopes reels : aucune restriction a une ressource particuliere ne peut encore etre demontree. La validation du type USER et de l'application ne prouve pas l'identite humaine du profil, ni l'inclusion d'une Page donnee dans l'autorisation.

Le code envoie exactement le jeton issu de la seconde reponse OAuth a debug_token (quand le fallback d'expiration est utilise), me/permissions et me/accounts. Les tests distinguent ce jeton de celui de la premiere reponse et des jetons Page. `TokenSource=final_oauth_exchange` signale ce parcours, sans empreinte ni identifiant. config_id est transmis au dialogue d'autorisation ; le code obtenu est echange cote serveur. debug_token ne fournit pas ici de preuve exploitee de l'identifiant de configuration Business : le lien aux ressources ne peut pas etre deduit du seul AppMatch.

La ligne `Facebook permission resources` contient, pour chacune des trois permissions :

- `Granted` : correspondance exacte dans le tableau scopes de debug_token, ou null si ce tableau est absent/malforme. `GrantedSource=debug_token_scopes` distingue cette observation des booleens HasPages... issus de me/permissions, qui restent les controles d'autorisation.
- `GranularScopePresent` : true si une entree correspond, false si le tableau valide n'en contient aucune, null si les metadonnees sont absentes/malformees.
- `TargetIdsCount` : somme des tailles des tableaux target_ids correspondants, sans deduplication ni affichage des valeurs ; zero signifie un tableau explicitement vide. Null signifie qu'aucun nombre fiable n'est disponible (entree absente, targets omis, forme invalide). Ce nombre n'identifie pas Redeemer Holding.

Ces lignes apparaissent uniquement lorsqu'un debug_token existant est execute. Si l'extension fournit une duree relative, le parcours OAuth actuel n'appelle pas debug_token : l'absence de ces logs ne signifie pas absence de ressources. Aucun nouvel appel distant ni changement de validation n'est ajoute pour ce diagnostic.

### Autres edges officiels : comparaison, pas repli

Le SDK officiel Meta expose [User/accounts et User/assigned_pages](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/user.py), ainsi que [Business/owned_pages et Business/client_pages](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/business.py). Ces edges portent respectivement sur un utilisateur et sur un Business ; ils ne sont pas interchangeables. Leur presence dans le SDK ne confirme ni les permissions requises pour notre jeton ni l'applicabilite a ce profil/configuration. La documentation web reste inaccessible (429). Aucun appel a ces edges, ajout de business_management ou repli automatique n'est justifie par les faits disponibles.

### Hypotheses a verifier dans Meta, sans partager d'identifiants

1. Pour la configuration effectivement referencee, verifier la variante de Login, le type de jeton choisi, le portefeuille et les types de ressources demandes. Une configuration specialisee ou une selection de ressources inadaptee reste une hypothese, pas un diagnostic etabli.
2. Verifier que le consentement utilise le profil attendu et que l'autorisation de la bonne application/configuration inclut la Page voulue pour les trois permissions. Ne partager que les conclusions oui/non, pas de jeton, capture de JSON ou identifiant.
3. Lors d'un nouveau consentement, relever les trois lignes permission resources et les compteurs accounts. Une limitation granulaire observee doit etre comparee aux ressources selectionnees dans Meta ; le compteur seul ne dit pas quelle Page est autorisee.
4. Si les ressources sont confirmees correctes mais accounts reste vide, verifier avec la documentation/support Meta la relation d'acces au portefeuille et l'edge adapte au type d'utilisateur avant tout changement fonctionnel. Une permission disponible pour les tests, un acces total dans Business Suite et un consentement OAuth accordent des informations differentes.

Une erreur JSON, data absent/null/non-tableau, une erreur Graph ou un curseur invalide echoue et n'est pas transforme en resultat vide. Une premiere page data vide avec next/cursor est poursuivie (test couvert). `completed`, `ReturnedPagesCount=0`, `BatchesCount=1` indique donc ici une liste vide terminee selon la pagination recue, pas une erreur de deserialisation masquee.
