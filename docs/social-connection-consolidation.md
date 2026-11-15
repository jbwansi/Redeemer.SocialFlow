# Unicité et reconnexion des comptes sociaux

## Cause et correction

La recherche à la sélection ignorait les connexions révoquées, et l’index unique ne couvrait que `ConnectionStatus <> 3`. Après déconnexion, une nouvelle connexion était donc créée avec un autre ID interne ; l’ancienne restait visible et pouvait être référencée par des publications.

La clé d’identité devient unique sans condition d’état : `(WorkspaceId, Platform, ExternalAccountId)`. Le nom affiché n’intervient jamais. Les adaptateurs conservent leurs identifiants fournisseur existants : `sub` LinkedIn pour le profil, URN pour les organisations LinkedIn, ID de Page Facebook et ID de compte professionnel Instagram. Deux identifiants distincts de même nom restent deux comptes ; deux workspaces restent indépendants.

La reconnexion actualise la même ligne, y compris après révocation : ID interne inchangé, état courant et consentement actualisés, nouveau chiffrement lié à cette même ligne. Le contrôle de session, les rôles, l’expiration et l’usage unique demeurent inchangés. SQLite sérialise les sélections dans leurs transactions d’écriture, et l’index unique fournit la garantie finale entre processus.

## Migration générée, non appliquée à la base réelle

`20261103152618_ConsolidateSocialConnectionIdentities` s’exécute dans la transaction de migration EF :

1. Pour chaque identité, conserver la ligne non révoquée si elle existe (l’ancien index en autorisait au plus une). Sinon conserver la dernière connexion révoquée selon `ConnectedAtUtc`, puis l’ID en cas d’égalité. La ligne retenue conserve son ID, son état, ses droits et son ciphertext ; aucun jeton n’est déplacé vers un autre contexte de chiffrement.
2. Archiver intégralement les autres lignes dans `SocialConnectionConsolidationArchive`, avec leur ancien ID, le workspace, le nouvel ID de référence et la date d’archivage. Ces archives sont des tables d’audit de migration, hors du modèle opérationnel EF et sans endpoint d’exposition.
3. Archiver les destinations et plans automatiques concernés dans `SocialDestinationConsolidationArchive` et `LinkedInAutomationConsolidationArchive`.
4. Réaffecter les destinations des publications à l’ID conservé. Si une publication référençait déjà les deux lignes de la même identité, conserver une seule association ; les associations originales restent dans l’archive. Les publications elles-mêmes et leurs opérations durables ne changent pas.
5. Réaffecter les plans automatiques concernés en les **bloquant**, avec nouvelle révision et motif explicite. Aucune autorisation d’envoi n’est créée par la consolidation ; une replanification autorisée sera nécessaire.
6. Supprimer les lignes redondantes de la table opérationnelle uniquement après archivage et réaffectation, puis créer l’index unique permanent. Les clés étrangères restent actives : une référence imprévue fait échouer la transaction, sans suppression partielle.

Les archives contiennent des données confidentielles, dont les éventuels secrets déjà chiffrés : conserver les protections de la base et ne pas les exposer au navigateur. Ne pas supprimer ces archives lors d’un nettoyage de données.

Le `Down` est volontairement refusé : une inversion automatique pourrait écraser des reconnexions intervenues après migration ou réactiver des autorisations anciennes. Avant application réelle, arrêter l’API, sauvegarder et vérifier SQLite, appliquer cette migration à la bonne base, puis contrôler intégrité et références. Pour revenir en arrière, restaurer la sauvegarde avec la version précédente du code, ou préparer une réconciliation explicite des archives et des changements ultérieurs.

Le nouveau backend doit être utilisé avec cette migration : il ne masque pas les doublons d’une base non migrée. Un inventaire en lecture seule de la base locale pendant l’implémentation a trouvé une identité LinkedIn en double, soit une ligne redondante. La base réelle est restée à `AddLinkedInAutomation`.

## Vérification

Les tests de reconnexion couvrent les trois fournisseurs : répétition, révocation puis reconnexion, références de publication, deux comptes de même nom, deux workspaces et sélections concurrentes. La migration est testée sur SQLite avec doublons référencés, archive, collision de destinations, plan automatique, opérations historiques et rejet des futurs doublons révoqués. Les tests React vérifient une seule carte après reconnexion et deux cartes pour deux identités distinctes de même nom.

Aucun changement aux permissions Facebook, notamment à la règle concernant `PROFILE_PLUS_CREATE_CONTENT`. Aucun OAuth réel, envoi ou worker n’est nécessaire à ces vérifications.
