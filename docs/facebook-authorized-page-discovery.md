# Découverte complémentaire des Pages Facebook

La liste `/me/accounts` peut être vide alors que le jeton USER est valide et que
`debug_token.granular_scopes` désigne des ressources explicitement autorisées.
Le diagnostic fourni établit cette divergence, sans établir sa cause interne chez Meta.
La lecture directe du champ `tasks` sur le nœud Page n'est pas valide.

Le fournisseur conserve `/me/accounts` et sa pagination comme source principale.
Il ne déclenche le complément qu'après une pagination valide, entièrement vide :
une erreur HTTP/Graph, une réponse malformée, une pagination incomplète ou des Pages
reçues mais filtrées ne déclenchent jamais ce parcours. Instagram et LinkedIn ne
l'utilisent pas. L'appel de diagnostic vers une Page codée en dur a été retiré.

## Contrôles

1. Inspecter à nouveau le jeton USER avec `debug_token` : validité, application,
   type et échéances. Croiser les permissions de cette inspection et celles
   effectivement accordées par `/me/permissions`.
2. Lire uniquement les cibles des trois permissions de Pages reconnues
   (`pages_show_list`, `pages_read_engagement`, `pages_manage_posts`). Écarter les
   tableaux ambigus, malformés ou dépassant 500 identifiants. Accepter uniquement
   des identifiants numériques de longueur bornée, dédupliquer et refuser une
   union dépassant 500 Pages avant tout appel à un nœud Page.
3. Demander `/{id}?fields=id,name,access_token` avec le jeton USER et
   `appsecret_proof`. Exiger l'identifiant attendu ; exclure une Page sans jeton.
4. Refuser un jeton identique au jeton USER. Réutiliser la validation PAGE
   existante : `debug_token` (type PAGE, même application, validité, échéances,
   intersection des scopes), puis lecture de la Page. Vérifier en plus que
   `/me?fields=id` avec le jeton PAGE renvoie l'identifiant attendu.
5. Conserver `PublishingPermissionGranted=false` : les scopes et le type PAGE ne
   prouvent pas la tâche `CREATE_CONTENT`. Le diagnostic porte le statut
   `retained_publishing_unverified`, affiché explicitement dans l'interface.
   Aucune tâche n'est inventée, aucun champ `tasks` n'est demandé sur le nœud Page,
   et aucun essai de publication n'est effectué.

Une erreur interrompt toute la découverte ; aucun candidat partiel n'est proposé.
Les diagnostics ne contiennent que des codes prédéfinis, identifiants numériques
et compteurs. Les jetons restent dans les modèles serveur et le stockage chiffré
existants. Les contrôles de session OAuth, d'utilisateur et d'espace sont conservés.
La sélection et la revalidation ne peuvent pas transformer le refus de publication
en autorisation ; les contrôles de destinations et de publication restent inchangés.

## Limite volontaire

Ce complément rétablit la découverte et la connexion, pas une autorisation de
publication non prouvée. Même avec `pages_manage_posts` et `pages_read_engagement`,
la publication reste bloquée faute de preuve de tâche dans ce parcours. Le chemin
standard conserve sa validation des tâches renvoyées par `/me/accounts`.
Les scopes non inspectables et les cibles invalides ne sont pas exploités.

## Validation locale, sans publication

1. Démarrer l'API et le frontend avec la configuration Facebook locale existante.
   Se connecter comme propriétaire ou administrateur de l'espace.
2. Lancer une nouvelle connexion OAuth Facebook et autoriser les Pages voulues.
3. Si `/me/accounts` est vide, vérifier les étapes de découverte complémentaire,
   les identifiants proposés et le statut « capacités de publication non vérifiées,
   publication bloquée ». Aucune valeur de jeton ne doit apparaître dans les
   réponses HTTP frontend ni les diagnostics.
4. Sélectionner une Page ; vérifier qu'elle est connectée sans droit de publication.
   Vérifier également qu'elle n'est pas admissible comme destination de publication.
   Ne pas déclencher de publication réelle.
5. Répéter avec un compte dont `/me/accounts` renvoie des Pages : le comportement
   standard doit être conservé. Vérifier les connexions Instagram et LinkedIn.
6. Les tests automatisés utilisent uniquement des réponses Meta simulées ; ils
   couvrent erreurs, identités incohérentes, permissions, limites, chiffrement,
   isolation HTTP et affichage React. Ils ne valident pas le comportement du
   compte Meta réel.
