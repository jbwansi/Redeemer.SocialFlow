# Brouillons IA et visuels dans un espace de travail

Depuis **Posts**, un Owner, Admin ou Editor peut choisir **Générer un brouillon**, sélectionner Facebook ou LinkedIn, et ajouter un contexte facultatif (4 000 caractères). Le mode Éditorial utilise le brief ; le mode Avec mes sources recherche les documents éligibles du workspace. Sans résultat exploitable dans ce dernier mode, aucun brouillon n’est créé.

Après enregistrement du brouillon, le panneau des visuels permet de générer une image à partir du brief visuel ou d’importer une image PNG/JPEG. Choisir une version la rend disponible au parcours de publication existant. La génération de texte, la génération d’image et la sélection du visuel ne planifient et ne publient rien.

Le panneau des visuels du workspace est activé dans `Redeemer.SocialFlow.Api/appsettings.Development.json` uniquement :

```json
{
  "Visuals": { "WorkspaceEnabled": true },
  "OpenAI": { "Images": { "Model": "gpt-image-1.5" } }
}
```

La clé reste dans les User Secrets : `OpenAI:Images:ApiKey`, ou le repli
`OpenAI:ApiKey`. Aucun appel fournisseur n'est nécessaire pour consulter le statut.
Ne pas recopier cette activation dans la configuration Production.

`GET /api/posts/workspace-visuals/status` reste enregistré et exige
`WorkspaceMember`, même lorsque le module est désactivé. Il retourne `enabled`,
`uploadEnabled`, `generationEnabled` et un code `reason` non sensible :
`WorkspaceVisualsDisabled` ou `ImageGenerationNotConfigured`. Une configuration
image invalide bloque seulement la génération ; la lecture et l'import restent
disponibles. Le statut valide la configuration locale, pas le quota, les droits
OpenAI ni l'accessibilité du stockage sur disque.

Les deux vues utilisent ce même statut. Un chargement de statut ou de liste en
échec bloque les mutations et affiche une erreur distincte d'une désactivation.
Les actions désactivées ne sont pas envoyées au serveur ; les autorisations
backend continuent de s'appliquer indépendamment de l'interface.

Redémarrer l’API. Les actions texte et image nécessitent la configuration OpenAI côté serveur ; l’import n’en a pas besoin. Le panneau accepte une identité proposée dans le formulaire (modifiable), sans dépendre de la bibliothèque d’administration locale. Les mutations exigent les droits d’édition et le CSRF de la session ; les lectures vérifient l’appartenance au workspace.

Pour une recette sans publication, générer un brouillon Facebook et un brouillon LinkedIn avec le même sujet et un contexte explicite. Vérifier le texte et le brief visuel, générer ou importer une image puis la sélectionner. Rouvrir chaque brouillon pour vérifier que le bon visuel est affiché. Relire et approuver séparément avant de choisir de publier ou de planifier.

## Diagnostiquer un échec de brouillon

Les erreurs de génération restent des réponses HTTP 500 ProblemDetails, avec
`code: DraftGenerationFailed` et `traceId`. L'écran affiche le code et la référence.
La même référence est enregistrée dans un événement structuré **5101**,
`DraftGenerationFailed`, de catégorie logger
`Redeemer.SocialFlow.Api.Controllers.PostsController`, au niveau Error.

| Stage dans les logs | Interprétation |
| --- | --- |
| Configuration | Échec de résolution du cas d'usage ou de validation de ses options |
| KnowledgeSearch | Recherche ou préparation des passages de connaissances |
| ProviderCall | Appel du générateur, erreur HTTP, réseau ou timeout |
| ResponseValidation | Trois réponses IA non exploitables, ou résultat inutilisable avant persistance |
| Persistence | Ajout des entités au contexte ou sauvegarde du brouillon et de son audit |

Les autres champs sont `Category`, `TraceId`, `ExceptionType` (liste autorisée),
`ProviderHttpStatus` (si disponible), `ApiKeyPresent` et `ModelPresent`.
Les deux booléens indiquent seulement la présence des paramètres dans la
configuration serveur : ils ne prouvent ni la validité de la clé ni le quota.
Aucun objet exception, message brut, prompt, réponse IA, identité utilisateur ou
secret n'est transmis au logger par ce diagnostic. Les détails techniques restent
côté serveur. Les annulations de requête conservent leur HTTP 499.

En Development, conserver la console de l'API ouverte et son niveau Error pour
ce logger. Après une tentative de génération depuis Posts, copier la référence
affichée puis rechercher exactement cette valeur dans la console ou le collecteur
de logs existant. `scripts/Start-Local.ps1` affiche les sorties API dans son terminal
et désactive les workers de publication ; il ne conserve pas de fichier de logs.
Une tentative utilisant le fournisseur réel peut être facturée : elle ne fait pas
partie des vérifications automatisées de ce correctif.

Pour reproduire les catégories sans appel IA et sans utiliser la base réelle,
exécuter les tests ciblés (SQLite temporaire et transports HTTP simulés) :

```powershell
$env:SocialConnections__Publication__WorkerEnabled = 'false'
$env:Facebook__WorkerEnabled = 'false'
$env:LinkedIn__WorkerEnabled = 'false'
dotnet test Redeemer.SocialFlow.IntegrationTests --filter FullyQualifiedName~GenerateDraftApiTests --artifacts-path artifacts/ai-diagnostics-validation
```

Ces variables ne concernent que le terminal de test et ses processus enfants.
Les tests contrôlent notamment la corrélation API/logs, une erreur OpenAI simulée
sans retry de transport, les trois tentatives de validation, l'échec de sauvegarde
après génération, et l'absence de données sensibles.
