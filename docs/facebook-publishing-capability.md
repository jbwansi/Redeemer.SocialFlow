# Capacités de publication Facebook — phase 1

La connexion OAuth et la validité d’un jeton PAGE ne prouvent pas la tâche de création de contenu. La découverte complémentaire reste disponible, mais ne fabrique aucune tâche.

## États et preuves

`PublishingCapabilityStatus` distingue `Unverified` (0), `Verified` (1) et `Denied` (2). Les DTO exposent `publishingCapability` avec `status`, `reason`, `source`, `checkedAtUtc` et la tâche de création effectivement observée, le cas échéant. Aucun jeton n’est exposé.

| Observation | État | Diagnostic |
| --- | --- | --- |
| Permissions nécessaires et tâche explicite sur la bonne Page via `/me/accounts` | Verified | PageCreateContentTaskVerified |
| Page absente, champ tasks absent ou inexploitable, ancien jeton PAGE sans contexte USER | Unverified | PageTaskEvidenceUnavailable |
| Permissions nécessaires absentes | Denied | PublishingScopeMissing |
| Liste de tâches exploitable sans tâche de création | Denied | PageCreateContentTaskMissing |
| Échec technique de la vérification Meta | Unverified | MetaVerificationUnavailable |
| Autorisation expirée/invalide/révoquée | Denied | AuthorizationInvalid |
| Déconnexion locale | Denied | ConnectionRevoked |

Seules les valeurs `CREATE_CONTENT` et `PROFILE_PLUS_CREATE_CONTENT` réellement retournées sont reconnues. Aucune équivalence n’est inventée pour `MANAGE`, `FULL_CONTROL`, les scopes ou les cibles granulaires. Les tâches manquantes ne sont pas assimilées à une liste vide explicitement retournée.

## Revalidation

Pour les nouvelles connexions, `SocialCredential.FacebookVerification` contient le jeton USER utilisé pour la découverte et son échéance. Ce contexte facultatif reste dans le secret chiffré lié au workspace et à la connexion. Il ne remplace jamais `AccessToken`, qui reste le jeton PAGE. Le fournisseur de publication ne reçoit que le jeton PAGE. La déconnexion ou une autorisation invalide effacent le secret complet.

La revalidation inspecte le jeton PAGE (`debug_token`, type, application, validité et échéances), vérifie `/me?fields=id`, inspecte le contexte USER, relit `/me/permissions`, puis `/me/accounts?limit=100&fields=id,tasks`. Elle recalcule les scopes à partir des réponses actuelles, sans conserver une intersection irréversible avec les anciens scopes. Les tâches ne sont prises en compte que pour l’identifiant de Page connecté.

La pagination est bornée à cinq pages de cent éléments. Les curseurs répétés et les réponses ambiguës échouent. Les URLs `paging.next` ne sont pas suivies. Les requêtes Graph conservent `appsecret_proof`. Aucune requête `tasks` sur un objet Page, aucun endpoint Business non confirmé, aucune écriture Meta ne sont ajoutés.

Une erreur technique retire la capacité vérifiée, conserve le secret pour réessayer et reste renvoyée à l’appelant. Une erreur d’autorisation met la connexion en autorisation à renouveler et efface ses secrets. La révision de connexion change, invalidant les préparations d’envoi antérieures.

## Compatibilité et publication

La migration `20261108153342_AddPublishingCapabilityEvidence` ajoute cinq colonnes nullable. Elle ne réécrit ni les booléens ni les secrets existants. Les anciens secrets et anciennes sessions OAuth restent désérialisables.

Une ancienne autorisation vraie garde sa compatibilité sous la provenance `LegacyStoredGrant`, sans tâche ni date de vérification inventées. Une ancienne connexion Facebook non autorisée expose `Unverified / PageTaskEvidenceUnavailable`. Lors de la prochaine revalidation, une connexion sans contexte USER ne peut pas conserver une capacité vérifiée sur la seule base du jeton PAGE : une reconnexion est nécessaire pour obtenir de nouvelles preuves.

Pour les nouvelles évaluations, `PublishingPermissionGranted` est synchronisé avec l’état : vrai uniquement pour `Verified`. `CanPublish` refuse aussi un état explicite `Unverified` ou `Denied` si un ancien booléen incohérent vaut vrai. Ce contrôle est utilisé pour les destinations, la résolution de connexion, le service commun de publication et la prise de l’opération durable. Le pipeline, la planification, les verrous d’idempotence et les règles LinkedIn/Instagram restent en place.

## Validation locale sans publication

1. Sauvegarder la base locale. Maintenir `SocialConnections:Publication:WorkerEnabled=false` et la publication Facebook désactivée. Ne pas lancer un worker historique.
2. Appliquer la migration à la base locale choisie avec `dotnet ef database update --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api --context SocialFlowDbContext --connection "Data Source=<chemin absolu de la base locale>"`, en remplaçant le chemin. La fabrique design-time utilise sinon un chemin relatif. Cette commande n’a pas été exécutée sur la base utilisateur pendant l’implémentation ; les migrations ont été exercées sur les bases temporaires de tests.
3. Ouvrir les connexions de l’espace. Vérifier que les connexions anciennes sont présentes et que les métadonnées héritées ne prétendent pas à une nouvelle preuve Meta.
4. Reconnecter Facebook pour stocker le contexte de vérification USER chiffré, puis sélectionner la Page. Ne pas créer de publication ni autoriser l’automatisation.
5. Utiliser « Vérifier / renouveler ». Avec `/me/accounts` vide et les scopes présents, attendre `Unverified`, `PageTaskEvidenceUnavailable`, booléen faux et absence de tâche confirmée. Si Meta retourne la bonne Page et une tâche de création, attendre `Verified`, la tâche exacte, la provenance et la date.
6. Vérifier que les réponses frontend et les journaux ne contiennent aucun jeton. Les tests automatisés couvrent les transitions, les erreurs, la révocation et le blocage sans utiliser les comptes Meta réels.

## Limite pour Redeemer Holding

Avec les observations connues (`/me/accounts` vide, jeton PAGE valide, scopes accordés), Redeemer Holding reste `Unverified` faute de preuve Meta de tâche. Cette phase ne débloque pas artificiellement la Page. Aucun appel authentifié à cette Page n’a été effectué pour constater son état réel pendant l’implémentation.

## Fichiers concernés par cette phase

Le workspace contenait déjà des modifications. Cette liste décrit les fichiers touchés pendant cette phase, sans attribuer leurs changements antérieurs au présent correctif.

- `Redeemer.SocialFlow.Domain/Entities/SocialConnection.cs`
- `Redeemer.SocialFlow.Application/Accounts/SocialConnections.cs`
- `Redeemer.SocialFlow.Infrastructure/Social/FacebookWorkspaceOAuthProvider.cs`
- `Redeemer.SocialFlow.Infrastructure/Social/WorkspaceOAuthProviders.cs`
- `Redeemer.SocialFlow.Infrastructure/Social/SocialConnections.cs`
- `Redeemer.SocialFlow.Infrastructure/Social/WorkspaceSocialConnectionResolver.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Configurations/SocialConnectionConfiguration.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Migrations/20261108153342_AddPublishingCapabilityEvidence.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Migrations/20261108153342_AddPublishingCapabilityEvidence.Designer.cs`
- `Redeemer.SocialFlow.Infrastructure/Persistence/Migrations/SocialFlowDbContextModelSnapshot.cs`
- `Redeemer.SocialFlow.Infrastructure/Publishing/SqlitePublicationOperationStore.cs`
- `Redeemer.SocialFlow.Api/Publishing/WorkspacePublicationService.cs`
- `Redeemer.SocialFlow.Web/src/api/socialConnections.ts`
- `Redeemer.SocialFlow.Web/src/pages/SocialConnections.tsx`
- `Redeemer.SocialFlow.Web/src/pages/SocialConnections.test.tsx`
- `Redeemer.SocialFlow.UnitTests/FacebookWorkspaceOAuthTests.cs`
- `Redeemer.SocialFlow.UnitTests/FacebookCapabilityValidationTests.cs`
- `Redeemer.SocialFlow.IntegrationTests/Api/FacebookCapabilityStateTests.cs`
- `Redeemer.SocialFlow.IntegrationTests/Api/WorkspaceFacebookPublicationTests.cs`
- `Redeemer.SocialFlow.IntegrationTests/Persistence/PublishingCapabilityMigrationTests.cs`
- `Redeemer.SocialFlow.IntegrationTests/Persistence/SocialConnectionConsolidationTests.cs`
- `docs/facebook-publishing-capability.md`

## Résultats des validations du 8 novembre 2026

- Suite unitaire complète : 696 réussis, aucun échec ni ignoré.
- Suite d’intégration complète : 583 réussis, aucun échec ni ignoré.
- Après les derniers ajouts, intégration ciblée sur états, migration historique, consolidation et blocage du pipeline : 11 réussis, dont les trois nouveaux cas ajoutés après la suite complète.
- Suite React complète : 193 réussis dans 23 fichiers.
- Après le dernier ajout frontend, tests ciblés `SocialConnections.test.tsx` : 27 réussis, incluant la conservation du diagnostic et de l’erreur après un échec Meta.
- Compilation API réussie ; compilation TypeScript/Vite réussie ; `git diff --check` sans erreur d’espacement.
- Les tests Meta emploient uniquement des réponses simulées. Aucun worker réel n’a été activé, aucune publication réelle, aucun commit, push ou déploiement.

La suite React complète émet un avertissement de clés dupliquées dans `WorkspaceShell.test.tsx`, sans échec et hors de ce correctif. Les outils EF signalent que leur version 10.0.11 est plus ancienne que le runtime 10.0.12 ; génération de migration et tests de migration réussis. Les tests et la compilation Vite ont nécessité une exécution autorisée hors du bac à sable, qui bloquait esbuild.
