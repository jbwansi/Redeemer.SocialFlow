# Phase 5 — publication LinkedIn avec image

## Comportement

LinkedInPostPublisher réutilise le parcours durable existant pour les publications manuelles
et programmées. Il lit le fichier privé identifié dans le snapshot, vérifie taille/MIME/dimensions
et SHA-256, initialise l'upload, transfère les octets originaux par PUT et crée le post avec
content.media.id contenant l'URN image. Les publications sans sélection restent du texte seul.
VisualUrl historique ne déclenche aucun téléchargement externe.

Le snapshot est capturé dans la même transaction SQLite que le claim PublicationOperation.
PublicationVisualSnapshot conserve VisualId, StorageKey, ContentType, ByteLength, Sha256,
Width, Height et ValidatedAt. Le visuel doit appartenir à la publication, être prêt, non supprimé
et validé. Le publisher ne relit pas la sélection après le claim. La confirmation manuelle
protégée inclut également l'identifiant du visuel sélectionné.

IPostPublisher, PublishScheduledPost et le store existants sont étendus ; aucun second système
de publication. Le worker appelle déjà ce use case via ProcessDueAsync, son code reste inchangé.
Les snapshots privés sont exclus de la sérialisation HTTP de PublicationOperationDto.

## Échecs et anti-doublon

- Erreur de fichier, intégrité, initialisation ou upload : aucune création du post texte seul.
- Rejet explicite du post : Failed, selon la classification existante.
- Timeout, erreur réseau après dispatch du post, 408/409/5xx, ou création sans identifiant
  exploitable : Indeterminate. Aucune relance automatique.
- Annulation remontée au use case après claim : résultat conservateur Indeterminate,
  conformément au comportement durable existant.
- Claim déjà présent : lecture du résultat uniquement, aucun nouvel upload ni nouvel envoi.
- Mort du processus après claim : l'opération reste protégée contre une nouvelle tentative.
- Les anciennes opérations restent inchangées ; aucun ré-envoi des posts déjà publiés.

La conservation des fichiers de Phase 3 exclut déjà tous les médias des publications ayant une
opération durable. Un test confirme cette protection même après modification administrative de
la sélection et suppression logique du candidat. Les originaux ne sont jamais modifiés.

## LinkedIn et sécurité

Version conservée : 202609. Auteur conservé : le membre OAuth connecté.
Aucun scope OAuth supplémentaire, aucune publication vers une page entreprise ajoutée.
Le flux utilise POST /rest/images?action=initializeUpload puis PUT et POST /rest/posts.

La permission w_member_social est en écriture seule pour les Images API versionnées :
aucun GET/polling de statut n'est ajouté. L'acceptation du post ne prouve donc pas que le
traitement asynchrone de l'image est terminé. Un rejet du média ne déclenche aucune relance
ni aucun fallback texte. La vérification visuelle finale côté LinkedIn reste nécessaire.

Seules les URLs HTTPS www.linkedin.com:443/dms-uploads/ sont acceptées pour transférer le
fichier et le bearer token ; userinfo, fragments, URL expirée, URN malformé ou hôte différent
sont refusés. Le handler existant interdit redirections, cookies, loggers HTTP et retries.
Le JSON d'initialisation est limité à 64 Kio avec un délai de lecture de 30 secondes.
Les autres réponses fournisseur ne sont pas lues ni exposées. Aucune URL d'upload signée
ou credential n'est persistée dans l'opération ou renvoyée au navigateur.

Sources officielles consultées :
- [Images API et permissions](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/images-api?view=li-lms-2026-09)
- [Transfert binaire des images](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/vector-asset-api?view=li-lms-2026-09#upload-the-image)
- [Posts API](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api?view=li-lms-2026-09)

## Migration et déploiement

20261010021112_AddPublicationVisualSnapshot ajoute une seule colonne nullable TEXT, Visual,
à PublicationOperations. Elle contient des métadonnées JSON, jamais les binaires.
Les anciennes opérations conservent Visual=null. Les migrations précédentes sont inchangées.

La migration a été générée et testée uniquement sur des bases temporaires.
Avant de lancer cette version sur la base active : arrêter l'application/worker, sauvegarder
SQLite et le stockage privé, puis appliquer volontairement la migration depuis la racine :

    dotnet ef database update 20261010021112_AddPublicationVisualSnapshot --context SocialFlowDbContext --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api

Cette commande n'a PAS été exécutée sur la base active pendant cette phase.
La configuration existante LinkedIn:PublicationEnabled et LinkedIn:WorkerEnabled reste nécessaire.
Aucune activation ni modification des secrets n'a été effectuée.

## Tests et limites

401 tests unitaires, 419 tests d'intégration, 105 tests frontend réussis.
38 tests ajoutés : 26 unitaires et 12 d'intégration.
Les parcours texte, image, échecs, anti-doublon, concurrence manuel/worker, snapshot figé,
protection du fichier, statut du candidat, intégrité et migration historique sont couverts.
HTTP LinkedIn est intégralement simulé. Aucune requête LinkedIn ou OpenAI réelle.

Le flux est limité à une image sélectionnée PNG/JPEG. Le scope membre existant reste le seul
pris en charge. Un changement du domaine d'upload LinkedIn sera refusé tant qu'il n'aura pas été
explicitement vérifié. Un upload réussi suivi d'un échec de création peut laisser un média
non publié chez LinkedIn ; aucun nettoyage distant ou mécanisme de ré-envoi n'est ajouté.
L'URN image est utilisé dans l'envoi mais n'est pas persisté séparément du résultat du post.
Aucune dépendance ajoutée, aucune modification React, aucun commit ni push.
