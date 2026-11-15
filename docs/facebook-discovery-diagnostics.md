# Diagnostic « Aucune destination compatible »

## Branchement du panneau

Le titre « Diagnostic de découverte Facebook » est désormais visible (`h2`), et pas seulement un nom accessible. L’affichage exige toujours une session et le droit de gestion Owner/Admin. Un retour explicitement LinkedIn ou Instagram n’affiche pas ce panneau Facebook.

Le frontend suit les changements d’identifiant de session dans le fragment de navigation et recharge le diagnostic. Les résultats des différents chargements sont traités indépendamment : un échec de la liste des connexions ou des candidats ne jette plus un diagnostic reçu. Une réponse de diagnostic absente ou en échec produit un message visible, au lieu de supprimer silencieusement le panneau. L’endpoint renvoie du JSON explicite, y compris `null` pour une ancienne session sans diagnostic.

Le parcours HTTP complet est testé avec le véritable adaptateur Facebook et un transport Meta simulé, pour Owner et Admin, avec zéro, une ou trois Pages, ainsi qu’une Page reçue mais exclue. Ces tests vérifient les compteurs sérialisés, la liste des identifiants numériques et le refus d’un autre utilisateur ou workspace. Les contrôles d’accès n’utilisent jamais le numéro de plateforme du fragment.

## Ce que l’inspection démontre

L’ancien écran affichait la même phrase pour une liste `/me/accounts` vide et pour une liste de Pages toutes exclues faute de `access_token`. L’adaptateur sautait ces Pages sans expliquer pourquoi. Les refus de permissions et autres échecs étaient renvoyés comme un échec OAuth générique. Aucun compteur persistant ne permet de reconstituer ces distinctions pour une ancienne session.

Une Page sans `pages_manage_posts`, sans `pages_read_engagement` ou sans tâche `CREATE_CONTENT` n’est **pas** éliminée pour ce motif : elle reste proposée avec `PublishingPermissionGranted=false`. Cette règle reste inchangée ; `PROFILE_PLUS_CREATE_CONTENT` seul n’accorde toujours pas la capacité de publication.

La cause du cas réel ne peut donc pas être établie rétroactivement à partir de l’ancien message. Aucun OAuth réel ni appel Meta n’a été effectué pendant ce diagnostic.

## Informations disponibles à la prochaine connexion

Après une nouvelle connexion Facebook initiée par Owner/Admin, le panneau « Diagnostic de découverte Facebook » distingue :

| Résultat | Signification |
| --- | --- |
| `no_pages` | Meta a retourné une liste vide après vérification de `pages_show_list`. |
| `missing_discovery_permission` | `pages_show_list` n’est pas accordée ; la liste des Pages n’a pas été demandée. |
| `pages_filtered` | Meta a renvoyé des Pages, mais aucune n’a été retenue. |
| `pages_without_publishing` | Des Pages peuvent être connectées, mais aucune n’a de droit de publication confirmé. |
| `pages_available` | Des Pages peuvent être sélectionnées ; chaque carte indique ses capacités. |
| `http_error` | Statut HTTP fournisseur, sans corps de réponse. |
| `invalid_response` | JSON, structure, pagination ou métadonnées de validation invalides/incomplètes. |
| `authorization_invalid` | Une autorisation échoue aux contrôles de validité, d’application, de type ou d’expiration. |
| `network_error` | Communication échouée ou délai dépassé. |
| `pagination_limit` | Découverte interrompue au plafond de cinq requêtes de liste ; aucune sélection partielle. |

Les compteurs indiquent les entrées Pages reçues (doublons inclus), les identités distinctes retenues, les Pages retenues sans droit de publication et le nombre de requêtes de liste. Les exclusions sont comptées par raison : jeton de Page absent, identifiant de Page absent, identifiant déjà reçu. Les identifiants numériques de Pages sont inclus avec leur traitement, sans nom fourni par Meta ni identifiant de format inattendu. Une Page absente de l’inventaire ne peut être déclarée absente de la réponse Meta que si la découverte est complète et les identifiants disponibles.

La pagination utilise le curseur `after`, avec un hôte Graph fixé côté serveur. L’URL `paging.next` n’est jamais suivie directement. Un curseur manquant/répété ou une réponse invalide arrête la découverte ; les résultats partiels ne deviennent pas sélectionnables. Les compteurs affichés lors d’un échec décrivent uniquement la progression avant cet échec, et non des comptes enregistrés.

Pour investiguer le cas réel, relever **le résultat, l’étape, Pages reçues/retenues/sans publication, les exclusions, le nombre de requêtes, découverte complète ou non et le statut HTTP éventuel**. Ne pas transmettre de code OAuth, URL complète, jeton, secret ou réponse Meta brute. Une session ancienne doit être remplacée par un nouveau parcours pour produire ces informations.

## Accès et conservation

Le diagnostic utilise le champ chiffré existant de la session OAuth ; aucune migration n’est ajoutée. Il expire avec la session initiale (dix minutes), est supprimé lors de sa sélection et nettoyé avec les autres sessions expirées au prochain démarrage de connexion de cet utilisateur/workspace. Les anciennes sessions contenant seulement un tableau de candidats restent lisibles.

`GET /api/workspaces/{workspaceId}/social-connections/sessions/{sessionId}/diagnostics` recontrôle l’accès au workspace, le rôle Owner/Admin, l’initiateur, l’état et l’expiration de la session. Même un autre Admin du même workspace ne peut pas lire ce diagnostic. Seuls l’ID opaque de session, le workspace et le numéro de plateforme reviennent au navigateur. Ce numéro sert à l’affichage, jamais à autoriser l’accès. Un échec de découverte conserve un diagnostic chiffré mais un état `Failed` et zéro candidat, non sélectionnable et non rejouable.

Aucun jeton, secret, corps HTTP brut ou message d’exception fournisseur n’est journalisé par ce mécanisme. Les clients HTTP OAuth conservent leur configuration sans journalisation de requêtes.

## Fichiers principaux

- `Application/Accounts/SocialConnections.cs` : contrats de diagnostic sûrs.
- `Infrastructure/Social/WorkspaceOAuthProviders.cs` et `FacebookWorkspaceOAuthProvider.cs` : compteurs, exclusions, pagination et erreurs classées.
- `Infrastructure/Social/SocialConnections.cs` : stockage chiffré, compatibilité des sessions et contrôle d’accès.
- `Api/Controllers/SocialConnectionsController.cs` : endpoint réservé à la session.
- `Web/src/api/socialConnections.ts` et `Web/src/pages/SocialConnections.tsx` : lecture et affichage du diagnostic.
- Tests `FacebookDiscoveryDiagnosticsTests`, `SocialDiscoveryDiagnosticTests` et `SocialConnections.test.tsx`, plus les régressions OAuth, isolation et reconnexion existantes.

Les chemins ci-dessus sont relatifs aux projets `Redeemer.SocialFlow.*`. Aucune modification des règles de publication, aucun lancement de worker, aucune publication et aucune modification de la base réelle ne sont nécessaires.

Dernières vérifications : 37 tests unitaires OAuth/diagnostic réussis, 58 tests d’intégration SQLite/API (dont le retour HTTP complet avec Meta simulé) réussis et 25 tests React réussis. Les compilations backend Debug et frontend réussissent. Toutes les réponses fournisseur utilisées sont simulées ; l’API locale n’a pas été démarrée.
