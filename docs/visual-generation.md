# Moteur de génération de visuels — Phase 2

Le moteur est enregistré par `AddOpenAIVisualGeneration` dans le host API.
L'enregistrement est différé : aucun appel réseau au démarrage, aucune configuration
image nécessaire pour les workflows texte existants. Le parcours actuel prépare et
valide un [concept visuel](visual-concepts.md) avant d'invoquer ce moteur.

## Contrat Application

`IVisualGenerator.GenerateAsync(GenerateVisualRequest, CancellationToken)` retourne
`GeneratedVisual` : octets en mémoire et `VisualGenerationMetadata` (fournisseur,
modèle configuré, identité, plateforme, brief utilisé, MIME, dimensions lues dans
l'en-tête, longueur, qualité demandée et date locale de fin de génération).

Le cas d'usage transmet le titre, le contenu, la plateforme et le brief enregistrés ;
le moteur n'accède pas à la base et ne modifie pas le post. Le brief non vide est
limité à 4000 caractères. `VisualIdentity` fournit un identifiant, un nom, un style,
un slogan facultatif et une palette copiée de 1 à 8 couleurs #RRGGBB. Les identités
sont propres à chaque requête ; aucune marque n'est codée en dur.

Les valeurs descriptives sont sérialisées en JSON dans le prompt et traitées comme
données. La politique interdit de générer ou d'imiter un logo et réserve sa place
pour une future composition déterministe. Un prompt ne garantit pas à lui seul
la conformité du rendu : validation humaine du concept puis du candidat requise.
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
# Composition de marque et visionneuse grand format

## Diagnostic et périmètre

La bibliothèque conservait le logo original avec son hash et son association à une identité révisionnée, mais `PostVisualService` enregistrait directement le résultat du fournisseur. Aucun moteur ne composait le logo après l'illustration. Le slogan figurait dans les données descriptives du prompt sans rendu typographique garanti.

Une lecture SQLite en mode read-only a confirmé dans la copie locale examinée une identité Redeemer Holding active/par défaut, révision 5, une approbation de cette révision et une référence PNG 3000 × 2000. Le fichier n'a pas été retrouvé dans la racine privée par défaut examinée : aucune conclusion n'est tirée sur la présence du slogan dans ses pixels. Aucun fichier officiel ni donnée existante n'a été modifié.

Le correctif antérieur du sélecteur demeure : catalogue workspace commun à la bibliothèque, présélection de l'unique défaut actif/approuvé sur formulaire vierge, actualisation explicite, conservation des choix et erreurs distinctes d'une liste vide.

## Implémentation sans migration

Cette version utilise une **configuration par composition**, pas une nouvelle politique permanente approuvée par workspace. La déclaration concernant le slogan intégré est une déclaration de l'utilisateur, pas une preuve obtenue par OCR. La politique persistante proposée dans l'audit précédent reste une extension possible, non implémentée ici.

- L'illustration reste un candidat original. Le prompt interdit les logos et le lettrage ; la propriété Slogan de l'identité n'est plus envoyée dans son JSON descriptif. Les autres données éditoriales restent inchangées.
- Nouvelle route `POST /api/posts/{postId}/workspace-visuals/{visualId}/compose`, réservée à `WorkspaceEdit` en plus de `WorkspaceMember`, avec CSRF existant.
- Corps : `libraryId`, `revision`, `mode` (`Logo`, `Slogan`, `LogoAndSlogan`), `logoContainsSlogan` nullable. Ni fichier, ni URL, ni chemin, ni slogan arbitraire n'est accepté comme élément approuvé.
- L'identité est résolue entièrement côté serveur. Le logo est relu depuis sa référence de bibliothèque, avec contrôles de workspace, disponibilité, hash et taille. La révision courante et son approbation sont revérifiées lors de la réservation du nouveau candidat.
- `LogoAndSlogan` exige une déclaration explicite. Si vrai, le rendu équivaut à `Logo` ; si faux, le slogan enregistré est ajouté. Sans déclaration, refus. Un slogan seul est possible sans logo. Un logo manquant n'est jamais inventé.
- SkiaSharp déjà installé compose un PNG. L'image est conservée entière, sans redimensionnement ni recadrage, et un bandeau est ajouté sous elle : hauteur supplémentaire compacte de 12 %, minimum 80 px ; classique de 16 %, minimum 100 px. Le ratio final change donc. Les limites de taille existantes sont revérifiées.
- Le logo garde son ratio, sa transparence et ses couleurs. Il est ajusté proportionnellement à la zone disponible, sans déformation. Une petite source peut donc être agrandie et perdre en netteté. Les logos clairs reçoivent un fond sombre de contraste sans recoloration des pixels. Le bandeau est blanc, son accent utilise la première couleur approuvée et le texte est noir.
- Le slogan est rendu sans changement de casse avec une police Arial demandée à Skia. La famille effectivement utilisée est tracée. Seuls les caractères latins jusqu'à U+00FF, hors contrôles, sont pris en charge par ce premier moteur ; les glyphes absents et les textes trop longs pour rester lisibles sont refusés. Aucune promesse de mise en forme des écritures complexes.
- Deux compositions au maximum sont rendues simultanément. Aucune opération de composition n'appelle OpenAI.

`VisualGenerationMetadata.Composition` conserve l'identifiant/hash source, le hash du logo, le mode effectif, la déclaration sur le slogan, la version du moteur et la famille de police. L'instantané d'identité conserve la bibliothèque, la révision, le logo et l'approbation. Ce sont des extensions du JSON déjà persisté : aucune table, colonne, migration ou nouvelle dépendance.

La source et le résultat sont des candidats distincts. Une composition ne peut pas être recomposée directement : repartir de l'original évite l'empilement de bandeaux. La suppression d'une source est refusée tant qu'un candidat composé non supprimé la référence. Les réservations, écritures privées et contrôles de sélection existants sont réutilisés. Aucune sélection automatique, aucun changement des opérations de publication.

## Interface et visionneuse

Dans une publication modifiable : choisir l'identité approuvée, puis la présentation de marque. Cliquer sur **Composer un aperçu de marque** sur un candidat existant. La composition est conservée comme nouveau candidat, puis ouverte en grand format pour revue. Elle n'est pas une prévisualisation éphémère : l'utilisateur peut ensuite la sélectionner ou la supprimer.

Chaque miniature dispose d'**Agrandir**. La visionneuse réutilise le dialogue modal natif : fermeture par bouton/Échap, retour du focus, identifiant ARIA unique même dans un éditeur modal. Elle affiche l'image entière avec ratio conservé, dimensions, date de création et statut. Zoom de 100 à 400 %, retour à l'image entière, déplacement par glissement ou flèches dans la zone focalisée, précédent/suivant.

Les images passent par les endpoints authentifiés existants. Seule l'image courante est chargée ; les requêtes sont annulées et les URL blob révoquées au changement/fermeture. Le téléchargement utilise ce blob déjà autorisé, sans endpoint ou URL publique supplémentaire. Ouvrir, naviguer, télécharger ou fermer ne sélectionne ni ne publie. La frontière workspace existante démonte la visionneuse ; les réponses tardives sont ignorées.

## Fichiers concernés par cette étape

- Nouveaux : `Application/Visuals/BrandComposition.cs`, `Infrastructure/Visuals/SkiaBrandComposer.cs`, `IntegrationTests/Api/WorkspaceBrandCompositionTests.cs`, `Web/src/components/VisualLightbox.tsx`, `VisualLightbox.test.tsx`, `BrandComposition.test.tsx` (préfixe de projet `Redeemer.SocialFlow.` pour les chemins backend).
- API : `Controllers/VisualsController.cs`, `Development/Visuals/VisualDependencyInjection.cs`, `VisualApiExceptionFilter.cs`.
- Application : `AI/GeneratedVisual.cs`, `Visuals/PostVisualService.cs`.
- Infrastructure : `Visuals/WorkspaceIdentityService.cs`, `SqlitePostVisualStore.cs`, `AI/OpenAIVisualGenerator.cs`.
- Web : `src/api/visuals.ts`, `components/PostVisuals.tsx`, `VisualIdentityLibrary.tsx`, `shared.tsx`, `styles.css`.
- Tests fournisseur : `UnitTests/Infrastructure/OpenAIVisualGeneratorTests.cs` ; ce document.

Les modifications locales antérieures de l'écran Identités et du sélecteur ont été conservées. Elles ne constituent pas de nouveaux changements métier de cette étape.

## Tests et builds

- 199 tests unitaires ciblés réussis (visuels, concepts, terminologie et génération).
- 236 tests d'intégration réussis : Phases 1–4, contrôles workspace, composition, publication durable et LinkedIn/Facebook simulés.
- Après le dernier ajustement de contraste/conservation des sources : 4 tests de composition relancés et réussis. Ils incluent un logo synthétique portant le slogan, une marque sans logo, deux espaces, un logo clair, la transparence, les hashes des originaux, les modes, les droits Editor/Viewer et les références étrangères/obsolètes. Ces 4 tests font partie des sélections précédentes, ce ne sont pas 4 tests distincts supplémentaires.
- Frontend complet : 276 tests réussis dans 32 fichiers. Les scénarios couvrent ouverture/fermeture/focus, zoom/navigation, chargement/erreur, isolation au remontage et composition sans génération/sélection.
- Builds API et TypeScript/Vite réussis ; `git diff --check` réussi.
- Aucune migration Phase 5. Les tests backend utilisent leurs bases SQLite temporaires habituelles, jamais la base de travail. Aucun appel fournisseur réel ni publication.

## Validation manuelle sans coût IA

1. Avec le backend contenant ce correctif et le frontend reconstruit, ouvrir une publication Draft/Rejected dans le bon workspace.
2. Vérifier l'identité active et approuvée sélectionnée. Dans Identités, inspecter le logo officiel : déterminer humainement si le slogan y figure. L'import d'un autre logo ou la modification de l'identité nécessiterait une nouvelle approbation, mais n'est pas requis pour tester le fichier déjà approuvé.
3. Utiliser un candidat existant : ne pas cliquer sur Générer. Choisir Logo seul, Slogan seul ou Logo et slogan. Dans ce dernier cas, répondre à la question sur le slogan intégré.
4. Cliquer sur Composer un aperçu de marque. Vérifier le nouveau candidat et la source toujours présente ; aucun ne doit changer automatiquement de statut de sélection.
5. Dans la visionneuse, contrôler les couleurs, le ratio du logo, le slogan, les marges et le contraste. Essayer zoom, glissement, flèches, précédent/suivant, téléchargement puis Échap ; vérifier le retour du focus.
6. Sur mobile, vérifier l'affichage entier et les contrôles. Le rendu visuel réel et les gestes tactiles restent à valider manuellement ; les tests jsdom ne constituent pas un contrôle graphique navigateur.
7. Fermer sans sélectionner pour un contrôle purement visuel. La sélection éventuelle doit être une action humaine séparée ; ne pas lancer de publication.

## Limites connues

- La déclaration « slogan intégré » peut être erronée. Aucun OCR, aucune validation automatique des pixels du logo ou de l'illustration. Un contenu déjà marqué importé de l'extérieur n'est pas détectable comme une composition SocialFlow.
- Le fond et la disposition sont une recette fixe, pas un éditeur graphique. La détection d'un logo clair est heuristique ; vérifier les logos multicolores. Les marges transparentes ou opaques du fichier officiel sont conservées, donc un logo avec beaucoup d'espace vide peut apparaître petit.
- La police peut varier selon les polices installées et le remplacement proposé par Skia ; sa famille est enregistrée. Une police de marque embarquée et licenciée et le support d'écritures complexes demandent une extension dédiée.
- Le bandeau modifie les dimensions/ratio du résultat. Une source trop grande pour les limites après ajout est refusée. Les fichiers originaux restent inchangés.
- Le modèle IA reçoit une instruction d'absence de lettrage, pas une garantie sur ses pixels. La revue humaine reste indispensable.
- La composition est explicitement déclenchée après l'illustration, pas automatiquement après chaque génération. Les images historiques ne sont jamais marquées en arrière-plan.


### Optimisation des dispositions (22 novembre 2026)

La nouvelle interface propose Compacte en premier. Le contrat conserve Classic lorsque le champ layout est absent, pour les clients existants. La disposition est conservée dans les métadonnées JSON de composition ; aucune migration. Le moteur de bandeau est identifié skia-banner-v2. Les marges verticales dépendent du bandeau (8 à 16 px), plus de la largeur de l’image. Les fichiers et les marges internes du logo restent intacts : un fichier contenant beaucoup de vide peut encore paraître petit.

Sans bandeau reste désactivé dans l’interface et refusé côté serveur : aucune analyse déterministe ne garantit une zone libre. Le schéma de disposition est gratuit ; le bouton Composer produit ensuite le véritable aperçu dans la visionneuse, avant sélection humaine. Changer la disposition seul ne fait aucun appel IA ni composition.

La déclaration existante LogoContainsSlogan empêche tout slogan séparé avec le logo lorsque sa valeur est true. Elle est remise à zéro au changement d’identité ou de révision. Il ne s’agit pas de reconnaissance optique : une déclaration erronée reste possible. Le fond de visionneuse devient gris clair ; les pixels noirs du fichier source restent volontairement inchangés. Le moteur ajoute uniquement sous l’image. L’origine du noir sur un fichier réel non inspecté ne peut être affirmée.

Vérification locale : choisir une publication existante et une identité approuvée, puis Logo et slogan. Déclarer Oui si le slogan apparaît déjà dans le fichier officiel. Comparer Compacte et Classique sur le même original via Composer un aperçu de marque. Ouvrir chaque candidat en grand format, vérifier ratio, lisibilité et slogan unique. Ne sélectionner qu’après contrôle. Vérifier que Sans bandeau est indisponible et qu’un changement de disposition seul ne génère rien. Refaire avec une autre identité/workspace ; la déclaration du slogan doit être redemandée. Aucun traitement des anciens fichiers n’est automatique.
