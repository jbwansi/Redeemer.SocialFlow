# Diagnostic Facebook Login for Business

Le diagnostic SaaS conserve des informations structurées, sans corps Graph brut,
jeton, secret, code OAuth, URL sensible ou message d'erreur du fournisseur.
Les sessions antérieures restent lisibles mais ne contiennent pas ces nouveaux champs.

## Relancer une découverte

1. Redémarrer le backend Development et le frontend avec le lanceur local existant
   (`scripts/Start-Local.ps1`). Si le frontend est servi depuis son build, le reconstruire.
2. Avec le rôle Owner/Admin, ouvrir les connexions sociales du workspace et lancer
   une nouvelle connexion Facebook. Ne pas réutiliser un ancien code OAuth.
3. Après le retour de Meta, ouvrir « Diagnostic Facebook détaillé ».
4. Vérifier les valeurs effectives lues lors du callback : App ID
   `1145889561364859`, LoginConfigId `2140120956587865`. Les surcharges serveur
   peuvent remplacer les valeurs du fichier Development. Seuls les identifiants
   numériques valides sont affichés ; une valeur invalide est masquée.
5. Examiner le type du jeton final, la correspondance de son App ID et les trois
   permissions issues de `/me/permissions`. « Non vérifiée » signifie que l'étape
   n'a pas abouti ; cela ne signifie pas un refus de permission.
6. Examiner chaque réponse de l'étape « liste des Pages » : statut HTTP,
   structure de `data`, présence d'erreur Graph, code et sous-code numériques.

Le callback reste `https://localhost:65474/api/social-oauth/1/callback`.
En mode Business, les permissions sont définies dans la configuration Meta ;
la configuration locale des scopes ne remplace pas les permissions effectivement accordées.

## Interprétation

Le panneau expose aussi les `granular_scopes` du jeton USER final validé par
`/debug_token`, pour les trois permissions Pages uniquement. Il affiche les
`target_ids` numériques, dédupliqués, par permission. Ces IDs sont des ressources
ciblées rapportées par Meta ; leur nom n'est pas résolu et ils ne deviennent pas
des candidats de connexion ou des droits de publication.

`array` avec IDs indique les cibles observées ; `empty_array` un tableau
explicitement vide ; `missing` un champ ou une permission absent ; `invalid`
une forme non exploitable ; `ambiguous` une duplication ou une entrée ambiguë.
`too_many` indique plus de 500 IDs distincts pour une permission. Dans les cas
non exploitables, aucun ensemble partiel n'est présenté comme complet. Absence
de champ et ensemble vide ne sont pas équivalents. Une ancienne session ou une
inspection de jeton échouée peut ne contenir aucune observation granulaire.

Pour Redeemer Holding, comparer l'ID numérique exact de la **Page** à chaque
liste (pas l'App ID, un Business ID ou le nom). Une présence démontre seulement
que Meta rapporte cette cible pour la permission ; elle n'explique pas, seule,
pourquoi `/me/accounts` est vide. Sans cet ID et une nouvelle observation réelle,
l'inclusion de Redeemer Holding ne peut pas être confirmée. Aucun appel Graph
supplémentaire n'est ajouté pour cette analyse.

- HTTP 200, `empty_array`, aucune erreur Graph, découverte terminée : vrai résultat
  vide de `/me/accounts`, sans Page éliminée par SocialFlow.
- Objet `error`, y compris HTTP 200 avec `data: []` : découverte en échec ; aucune
  sélection possible. Le JSON et le message d'erreur ne sont pas enregistrés.
- `missing`, `object`, `null`, `non_object_root` ou `invalid_json` : forme reçue,
  distincte d'un tableau vide. Les contrôles de lecture existants restent applicables.
- Une réponse par requête permet d'identifier la page de pagination en échec.
- Le type de jeton correspond au jeton final inspecté avant la liste, pas aux
  jetons de Page. La correspondance d'App ID compare ce jeton à l'App ID effectif.

Les diagnostics restent réservés à l'initiateur autorisé dans son workspace.
Cette procédure ne nécessite aucune publication. Une découverte vide confirmée
nécessite ensuite de vérifier les accès à la Page et la configuration dans Meta ;
elle ne justifie pas automatiquement un autre endpoint ou de nouvelles permissions.
