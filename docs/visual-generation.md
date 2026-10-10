# Moteur de génération de visuels — Phase 2

Le moteur est enregistré par `AddOpenAIVisualGeneration` dans le host API.
L'enregistrement est différé : aucun appel réseau au démarrage, aucune configuration
image nécessaire pour les workflows texte existants. Aucun endpoint n'invoque
encore ce moteur. Aucun stockage, traitement des photos, logo, migration ou
adaptation LinkedIn n'est ajouté dans cette phase.

## Contrat Application

`IVisualGenerator.GenerateAsync(GenerateVisualRequest, CancellationToken)` retourne
`GeneratedVisual` : octets en mémoire et `VisualGenerationMetadata` (fournisseur,
modèle configuré, identité, plateforme, brief utilisé, MIME, dimensions lues dans
l'en-tête, longueur, qualité demandée et date locale de fin de génération).

Le futur cas d'usage transmettra `SocialPost.VisualBrief` ou sa valeur éditée ;
le moteur n'accède pas à la base et ne modifie pas le post. Le brief non vide est
limité à 4000 caractères. `VisualIdentity` fournit un identifiant, un nom, un style,
un slogan facultatif et une palette copiée de 1 à 8 couleurs #RRGGBB. Les identités
sont propres à chaque requête ; aucune marque n'est codée en dur.

Les valeurs descriptives sont sérialisées en JSON dans le prompt et traitées comme
données. La politique interdit de générer ou d'imiter un logo et réserve sa place
pour une future composition déterministe. Un prompt ne garantit pas à lui seul
la conformité du rendu : validation humaine requise dans une phase ultérieure.
Aucune photo importée n'est transmise à OpenAI.

## Configuration indépendante

Exemple sans secret (le modèle est un exemple explicite, pas une valeur par défaut) :

```json
{
  "OpenAI": {
    "Images": {
      "Model": "gpt-image-1",
      "Quality": "medium",
      "TimeoutSeconds": 120,
      "MaxConcurrentGenerations": 2,
      "MaxImageBytes": 20971520
    }
  }
}
```

- `OpenAI:Images:Model` obligatoire, famille GPT Image ; l'accès effectif au modèle
  dépend du compte fournisseur et n'est pas testé par un appel réel.
- `OpenAI:Images:ApiKey` facultatif : réutilise `OpenAI:ApiKey` s'il est absent.
  Ne pas stocker de clé dans Git. Une valeur explicitement vide est invalide.
- `Quality` : low, medium ou high ; défaut medium.
- `TimeoutSeconds` : 1 à 600 ; défaut 120.
- `MaxConcurrentGenerations` : 1 à 16 ; défaut 2.
- `MaxImageBytes` : 1024 à 52428800 ; défaut 20971520.
- Les variables d'environnement utilisent les doubles underscores, par exemple
  `OpenAI__Images__Model`.

Le client officiel OpenAI 2.14.0 utilise `ImageClient.GenerateImageAsync`.
Une requête produit une image PNG ; aucun retry SDK ni retry métier n'est activé.
Les options de format image sont expérimentales dans cette version du SDK
(diagnostic OPENAI001 supprimé uniquement dans l'adaptateur).

## Formats et limites

`VisualLayout` propose Square (1024×1024), Landscape (1536×1024) et Portrait
(1024×1536). Par défaut Facebook utilise Landscape, LinkedIn et Instagram Square.
Ce sont des dimensions natives de génération, pas les futurs formats de livraison
1200×1200, 1200×630 et 1080×1080. Le redimensionnement sera traité ultérieurement.

La réponse doit contenir des octets PNG avec une signature/IHDR et des dimensions
conformes ; une URL seule n'est jamais téléchargée. Cette vérification d'en-tête
n'est pas un décodage complet ni une validation des chunks/CRC. Le futur stockage
devra décoder et valider complètement les images. La limite d'octets est vérifiée
après désérialisation SDK : elle ne borne pas le corps HTTP avant allocation.

La concurrence est limitée dans l'instance singleton du processus ; les appels
excédentaires sont refusés immédiatement. Ce n'est pas un quota distribué, un
budget monétaire ni une file de travaux. Annuler ou atteindre le délai ne garantit
pas que le fournisseur n'a pas commencé/facturé la génération.

## Erreurs et diagnostics

`VisualGenerationException.Failure` distingue authentification, limitation
fournisseur, rejet, indisponibilité, réponse invalide, taille excessive, délai et
concurrence. Aucun payload fournisseur ni exception interne n'est exposé.
L'annulation de l'appelant reste une `OperationCanceledException`.
Les logs ne contiennent que des événements fixes et le code d'échec ; aucun brief,
octet d'image, secret ou message brut fournisseur.

## Vérification

Les tests utilisent le vrai SDK sur un `HttpClientPipelineTransport` simulé et
des fixtures PNG construites localement avec la BCL. Aucun appel payant.

```text
dotnet test Redeemer.SocialFlow.UnitTests --filter FullyQualifiedName~Visual
dotnet test Redeemer.SocialFlow.UnitTests
dotnet test Redeemer.SocialFlow.IntegrationTests
cd Redeemer.SocialFlow.Web
npm test
```

Référence API : https://developers.openai.com/api/reference/resources/images/methods/generate
