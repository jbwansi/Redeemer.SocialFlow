# Nettoyage local avant Instagram

Ce nettoyage part de la copie locale, y compris ses changements non commités.
Il ne remplace aucun fichier par une version distante.

## Supprimé

- Le diagnostic Facebook Business temporaire : contrat Application, méthode du
  service, implémentation Graph, route `facebook-business-diagnostic`, client API,
  bouton et composant React. Ses tests spécifiques et ses deux guides de dépannage
  (diagnostic Business et trace `business_management`) sont retirés avec lui.
- Les quatre indicateurs de trace `business_management` dans la découverte OAuth,
  leurs observations serveur et leur affichage. Les scopes réellement accordés,
  leur stockage et les vérifications de publication sont conservés.
- Les propriétés Graph de l'exception consommées uniquement par le diagnostic
  Business. Les codes Graph du diagnostic de découverte actif restent présents.
- La démonstration météo du projet historique hors solution : contrôleur,
  modèle `WeatherForecast` et fichier de requête HTTP. Aucun appel trouvé dans
  l'application, le frontend ou les tests.

La suppression est vérifiée sur les routes, contrats, références, DI et appels
frontend. Le diagnostic Business n'était lu par aucun mécanisme de publication :
sa suppression ne change pas les états `Verified`, `Unverified`, `Denied`, ni
`PublishingPermissionGranted`. Un test HTTP authentifié vérifie que son ancienne
route n'est plus exposée. Le test CSRF utilise désormais une route métier active.

## Conservé et raisons

- Les parcours globaux `/api/dev/*` LinkedIn/Facebook : ils sont encore appelés
  par les cartes de l'administration locale et par `PlatformPublication` sans
  workspace. Leurs services et enregistrements DI restent donc nécessaires.
- `LinkedInPublicationWorker` : malgré son nom historique, il contient le
  traitement par workspace, notamment Facebook. Le worker, son ordonnanceur,
  `Program.cs` et les réglages Development n'ont pas été modifiés.
- Les DTO de publication historiques utilisés également par les contrôleurs
  workspace. Leur déplacement isolé serait une refactorisation, pas un retrait
  de code mort.
- La découverte Facebook complémentaire, le contexte USER chiffré, les preuves
  de tâches, les diagnostics de découverte et leurs guides encore utiles.
- Les pipelines durables, destinations, droits, isolation, idempotence, reprise,
  création de posts, génération IA, visuels et calendrier.
- Les migrations existantes, y compris locales non commitées.
- `--seed-knowledge-test`, ses tests et sa documentation : toujours manuel,
  désactivé par défaut et limité à Development.
- Le lancement local HTTPS Vite, sa passerelle et ses contrôles de sécurité.

## À décider séparément

Le retrait des parcours globaux exige d'abord une décision de suppression de
l'administration locale qui les utilise, ainsi que la séparation des DTO et du
worker partagés. Ils ne sont pas assimilables à du code mort aujourd'hui.
Le squelette de l'ancien projet hors solution est conservé ; seule sa météo
sans consommateur est retirée. Ses configurations ne sont pas modifiées.

## Validation et diff

- Compilation API, Domain, Application, Infrastructure et tests : réussie.
- Compilation de l'ancien projet après retrait météo : réussie, zéro avertissement.
- Frontend : `npm.cmd run build` réussi ; 201 tests sur 23 fichiers réussis.
  Quatre tests de passerelle ont nécessité une relance hors restriction réseau du
  bac à sable (`EACCES` localhost). Avertissement React sur une clé `two` dupliquée
  dans le test WorkspaceShell, sans échec, hors du périmètre modifié.
- Tests unitaires : 698 réussis.
- Intégration : 601 tests existants réussis lors du lancement complet. Le nouveau
  test de route supprimée attendait initialement 404 au lieu du 403 imposé par la
  politique d'autorisation de repli. Corrigé pour vérifier aussi l'absence dans
  les endpoints enregistrés, il réussit lors de sa relance ciblée (602 tests
  validés au total, en deux exécutions ; aucun changement de sécurité applicative).
- `git diff --check` : réussi. Les 37 empreintes de fichiers protégés (worker,
  ordonnanceur, démarrage, réglages, migrations, seed et guide) sont inchangées.

Les builds .NET utilisent des sorties isolées sous `artifacts/cleanup-validation`
pour ne pas remplacer les DLL d'une éventuelle API locale en cours d'exécution.
Les tests utilisent leurs bases temporaires et fournisseurs simulés ; les trois
options de workers sont désactivées uniquement dans le processus de test.
Aucun service local connecté, OAuth réel, envoi, changement de droits, secret ou
base réelle n'a été utilisé ou modifié. Aucun commit ni push.

Le diff incrémental est disponible dans
`artifacts/cleanup-review/cleanup-only.patch`. Il compare le résultat à une copie
des fichiers concernés prise avant ce nettoyage, et inclut donc le retrait des
fichiers temporaires qui n'avaient jamais été commités. Le diff Git ordinaire
contient aussi les modifications antérieures de l'utilisateur. Les sauvegardes
et résultats de validation restent dans `artifacts`, ignoré par Git.
