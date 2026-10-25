# Phase 2 — Espaces et isolation

## État de livraison

**Mise à jour du 25 octobre 2026 :** après autorisation explicite de l'utilisateur, les deux migrations ont été appliquées à `Redeemer.SocialFlow.Api/socialflow.db`. Les sauvegardes SQLite cohérentes des bases métier et Identity se trouvent dans `artifacts/phase2-real-migration/`. Une répétition sur copie a précédé la migration réelle. Les contrôles d'intégrité et de clés étrangères passent ; toutes les valeurs des anciennes colonnes des huit tables sont identiques à la sauvegarde. Sont conservés : 25 posts, 14 générations, 2 documents, 3 passages, 14 opérations de publication, 4 visuels et 1 identité graphique (aucun logo). Redeemer Holding reste suspendu et sans propriétaire. Aucun serveur, worker ou appel fournisseur n'a été lancé. Les paragraphes ci-dessous décrivent la livraison initiale et la procédure désormais exécutée pour les migrations ; l'attribution du propriétaire reste à faire sur désignation explicite.

Implémentation prête à valider, sans application des migrations aux bases réelles, sans déploiement ni appel réel aux fournisseurs. Les workers LinkedIn et Facebook restent désactivés au démarrage, même si une ancienne configuration les active. La phase 3 n'est pas commencée.

Les espaces personnels et professionnels possèdent des membres actifs, avec rôles Owner et Member. La création d'un espace et de son premier propriétaire est transactionnelle. L'unicité de l'espace personnel et de l'adhésion est imposée en base. Le domaine et des triggers SQLite interdisent de retirer ou désactiver le dernier propriétaire actif.

La confirmation d'adresse crée l'espace personnel. Identity et métier ayant deux bases distinctes, un échec de provisionnement ne défait pas la confirmation : l'interface réessaie l'opération idempotente à la connexion. Cela initialise également les comptes de test confirmés préexistants, après migration autorisée.

## Frontières d'accès

Chaque requête métier ordinaire exige un cookie confirmé, HTTPS et un `X-Workspace-Id` dont l'adhésion active est revérifiée côté serveur. Sélectionner un espace ne crée pas une autorisation durable. Les espaces suspendus sont exclus. Les mutations conservent la protection CSRF de phase 1.

Les huit ressources portent WorkspaceId : SocialPost, AiGeneration, KnowledgeDocument, KnowledgeChunk, PublicationOperation, PostVisual, StoredVisualIdentity et IdentityLogo. Les filtres EF nommés, les contrôles d'écriture, le jeton de concurrence WorkspaceId et les clés étrangères composites protègent lectures, écritures et références. Les triggers empêchent le déplacement d'une ressource vers un autre espace. Sans contexte explicite, les lectures de ressources sont vides et les écritures échouent.

La corbeille et le nettoyage ignorent uniquement le filtre SoftDelete, jamais Workspace. Les magasins créant leur propre DbContext reçoivent le contexte d'espace. L'annuaire emploie un contexte sans espace uniquement pour les métadonnées Workspace/Membership, avec prédicat explicite sur l'utilisateur authentifié. La maintenance exige `--workspace-id=<GUID>` ; le traitement historique local est explicitement limité à l'espace historique.

React propose « Mes espaces », la création d'organisation et le sélecteur. Le changement d'espace annule les requêtes, rejette aussi les réponses tardives de transports ignorant l'annulation, et remonte l'application avec un état neuf. Aucun identifiant conservé dans localStorage n'accorde de droits.

## Migrations préparées

- `20261025130100_AddWorkspaceIsolation` : tables des espaces/adhésions, rattachement historique des huit tables, filtres et relations composites, index par espace.
- `20261025130226_EnforceWorkspaceInvariants` : vérification des clés étrangères et triggers dernier Owner, immutabilité de WorkspaceId et cohérence du visuel des opérations de publication.

L'espace historique a l'identifiant `a661b775-1b98-4c97-8ec0-2a461f0c7001`, le nom « Redeemer Holding » et le statut Suspended. Aucune adhésion n'est créée par migration. Aucun auteur historique n'est inventé. Les valeurs antérieures des ressources sont conservées, y compris identifiants, statuts, dates, hashes, relations et snapshots de publication. L'identité graphique historique est conservée sans suppression/réinsertion, donc ses personnalisations restent intactes.

SQLite reconstruit les tables pour changer les contraintes. Les clés étrangères sont temporairement désactivées pendant ces reconstructions puis réactivées ; la seconde migration contrôle leur intégrité. Exécuter hors service, sur une copie vérifiée d'abord. Les migrations Down refusent de supprimer l'isolation : le retour arrière se fait par restauration d'une sauvegarde cohérente.

## Initialisation — uniquement après autorisation

Ces commandes sont fournies pour une exécution ultérieure. Elles n'ont pas été exécutées sur les bases réelles dans cette livraison.

1. Arrêter l'application et les accès concurrents. Sauvegarder les bases métier/Identity et les fichiers de visuels/logos, puis vérifier la restauration. Si SQLite utilise WAL, employer une sauvegarde SQLite cohérente ou fermer toutes les connexions avant copie ; ne pas copier seulement un fichier actif.
2. Tester les migrations et l'intégrité sur une copie isolée. Choisir explicitement le chemin absolu de la base cible afin d'éviter les ambiguïtés de répertoire courant.
3. Après validation et autorisation de modifier la base réelle, depuis la racine du dépôt :

```powershell
$businessDatabase = 'C:\chemin-explicite\socialflow.db'
dotnet ef database update 20261025130226_EnforceWorkspaceInvariants --context SocialFlowDbContext --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --connection "Data Source=$businessDatabase"
```

4. Vérifier l'historique EF, `PRAGMA foreign_key_check` (aucune ligne), les nombres de ressources et leur rattachement à l'identifiant historique. Vérifier que cet espace n'a aucun membre. La base Identity existante ne nécessite pas de migration supplémentaire pour cette phase.
5. Configurer les chemins absolus des deux bases avant tout lancement. Se connecter avec un compte confirmé : l'interface crée ou retrouve son espace personnel. Créer un espace professionnel depuis « Mes espaces ». Les quatre comptes de test de phase 1 ne sont ni recréés ni modifiés par les migrations.
6. Désigner humainement le propriétaire de Redeemer Holding : récupérer l'ID Identity du compte confirmé choisi (par exemple son propre profil `/api/auth/me`) et vérifier son identité. Exécuter ensuite, uniquement avec l'autorisation d'attribution :

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__SocialFlow = 'Data Source=C:\chemin-explicite\socialflow.db'
$env:ConnectionStrings__Accounts = 'Data Source=C:\chemin-explicite\socialflow-accounts.db'
dotnet run --project Redeemer.SocialFlow.Api --no-launch-profile -- --assign-historical-owner=<ID-IDENTITY-CONFIRME>
```

Remplacer le marqueur par l'ID choisi. Cette commande ajoute le propriétaire, active l'espace et quitte avant le serveur et les workers. Elle est idempotente pour le même propriétaire actif ; toute réattribution automatique à un autre compte est refusée. Elle ne change aucun jeton social et ne reprend aucune publication.

## API disponible

| Méthode | Route | Effet |
|---|---|---|
| GET | `/api/workspaces` | Espaces actifs accessibles |
| POST | `/api/workspaces/personal` | Créer/retrouver l'espace personnel |
| POST | `/api/workspaces` | Créer une organisation, corps `{ "name": "Studio" }` |
| GET | `/api/workspaces/{id}` | Informations accessibles ou 404 |
| POST | `/api/workspaces/{id}/select` | Valider l'accès ; envoyer ensuite X-Workspace-Id |

## Fonctionnalités encore bloquées

Les comptes ordinaires accèdent à la rédaction, aux transitions éditoriales, au calendrier et à la corbeille de leur espace. Une date programmée ne déclenche pas une publication.

La génération IA, les API de documents, les visuels/logos et la bibliothèque graphique restent réservés à l'administration locale historique. Leur stockage et leurs relations sont isolés, mais leurs parcours utilisateurs ordinaires ne sont pas ouverts dans cette phase. Les connexions globales LinkedIn/Facebook, OAuth, les jetons et les publications manuelles restent inaccessibles aux comptes ordinaires. Les services de publication refusent un contexte autre que l'espace historique.

Les invitations, abonnements, administration des membres et rôles avancés ne sont pas implémentés. Le contrat `IWorkspaceSocialConnectionResolver` et `WorkspaceSocialConnection` prépare une résolution explicite par WorkspaceId/ConnectionId sans implémentation ni transfert des jetons globaux.

## Vérifications

- 588 tests unitaires réussis.
- 500 tests d'intégration réussis, dont la migration de toutes les tables historiques, les accès de deux utilisateurs par véritables cookies Identity, la confirmation d'adresse, la création concurrente d'espaces personnels et l'attribution historique explicite.
- 139 tests React réussis : sélection, organisation, révocation, annulation et réponses tardives, en plus des parcours existants.
- Build solution et build frontend réussis, sans avertissement du build .NET.
- Les tests emploient des bases temporaires et des fournisseurs simulés. Les anciennes fixtures métier reçoivent un contexte historique explicite, en conservant les filtres, gardes et contraintes. Les nouveaux tests API multi-utilisateurs utilisent l'authentification réelle.
- Pas de test manuel dans un navigateur ni d'appel réel aux fournisseurs pour cette livraison.
- Empreintes SHA-256 réelles inchangées : métier `79640AAD4C64564B5685522C7E345EC3EB5D0281FD639812C7D32A8E2F4024A3` ; Identity `F25797E6668765F86BF8A939D4546CDD0DA8872B6D788884BA99922C42D2336A`.

## Préparation de phase 3 — à valider avant démarrage

Construire les invitations et la gestion des membres sur WorkspaceMembership, définir les permissions par rôle et les changements concurrents de propriété, puis tester révocation/suspension et journalisation. Concevoir les connexions sociales par espace avec stockage des secrets et autorisation explicite avant toute publication multi-tenant. Ne rouvrir les fonctions IA/visuels qu'après validation de leurs parcours et permissions. Aucun de ces développements n'est démarré ici.

La liste exhaustive des fichiers de cette phase se trouve dans [phase-2-files.txt](phase-2-files.txt).
