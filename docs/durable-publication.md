# Opération durable de publication

`IPublishScheduledPost.ExecuteAsync` utilise `IPostPublisher` et un store atomique.
Le host pourra activer le cas d'usage via `AddPostPublication<TPublisher>()` après
avoir choisi une implémentation. Aucun adaptateur ni déclencheur n'est installé
par défaut. `AddPersistence` enregistre uniquement le store SQLite.

## Identité et prise en charge

- L'appelant fournit PostId, OperationId stable, Provider et Destination. Provider
  doit désigner le publisher configuré ; le contrat existant IPostPublisher ne
  fournit pas de découverte des fournisseurs ou de routage.
- Une transaction SQLite immédiate lit l'opération existante et l'état courant
  du post. Une nouvelle opération exige un post non supprimé, Scheduled, avec
  ScheduledAt inférieur ou égal à l'instant fourni par TimeProvider. La comparaison
  est faite en .NET entre DateTimeOffset, pas lexicalement sur le TEXT SQLite.
- OperationId est la clé primaire. Un index unique SocialPostId interdit une
  deuxième opération, même avec un autre fournisseur, une autre destination ou un
  nouvel OperationId. Réutiliser un OperationId pour un autre post est refusé.
- Provider, Destination, Content et Platform sont figés à la prise en charge.
  Aucun titre, secret ou payload d'exception fournisseur n'est ajouté au contenu.
- L'état initial durable est **Indeterminate**, avec ClaimedAt et UpdatedAt,
  CompletedAt null. Seul l'appel ayant obtenu Acquired=true appelle le publisher,
  après commit, avec l'identité et le contenu enregistrés. La transaction est
  terminée et son contexte détruit avant l'appel externe.

## Résultats et interruptions

- Confirmed exige ExternalId et sauvegarde l'opération et Scheduled → Published
  dans une seule transaction. Failed sauvegarde Scheduled → Failed dans la même
  transaction. Indeterminate laisse le post Scheduled.
- CompletedAt indique l'enregistrement d'un résultat, y compris Indeterminate ;
  ce n'est pas une preuve de publication. PublishedAt est la date de confirmation
  locale, faute de date fournisseur dans le contrat actuel.
- Toute exception après prise en charge, dont timeout et annulation, conduit à
  Indeterminate. Aucun message brut d'exception n'est persisté. L'annulation avant
  prise en charge interrompt sans envoi. Après prise en charge, la finalisation
  utilise un token indépendant de l'annulation du demandeur.
- Si le processus meurt avant, pendant ou après l'envoi, ou si la transaction de
  finalisation échoue, le claim Indeterminate initial reste durable. Les reprises
  retournent cet état et n'appellent jamais le publisher. Cela peut bloquer une
  opération qui n'a en réalité jamais été envoyée : choix conservateur explicite.
- Même un Failed certain n'est pas renvoyé automatiquement. Aucune réconciliation
  ou nouvelle tentative manuelle n'est implémentée dans cet incrément.

## Concurrence et limites

Les transactions courtes sérialisent les claims SQLite entre contextes/processus ;
les contraintes uniques constituent une protection supplémentaire. Aucun verrou
en mémoire ou bail expirant ne rend une opération resendable. Le statut SocialPost
est un jeton de concurrence EF : une mutation chargée avant la confirmation ne
peut pas écraser ensuite Published avec une ancienne valeur Scheduled.

Une annulation du post commise pendant l'appel externe peut néanmoins devancer la
confirmation. Le Domain refuse alors Cancelled → Published : la transaction de
confirmation échoue et l'opération reste Indeterminate, à réconcilier, sans renvoi.
La coordination UX/worker de cette annulation reste à définir avant leur ajout.

Les règles de transition SocialPost n'ont pas changé. MarkAsPublished et
MarkAsFailed acceptent seulement un TimeProvider optionnel afin que la
finalisation utilise la même horloge que l'opération après relecture EF.

Il n'y a aucune garantie d'exactly-once chez le fournisseur, ni hypothèse
d'idempotence distante. La durabilité dépend aussi de la conservation de la base :
restaurer une sauvegarde antérieure au claim n'est pas couvert.

Migration : `20261002235255_AddDurablePublicationOperations`. Elle ajoute uniquement
la table et son index, préserve les données existantes et n'a pas été appliquée à
la base de l'API par cette implémentation. Les tests utilisent leurs propres bases.
