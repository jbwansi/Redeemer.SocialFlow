# Visuels des publications — Phase 3

## Architecture et contrats

Application définit IVisualStorage (écriture immuable, lecture, suppression physique), IVisualValidator
(décodage et dimensions), IPostVisualStore (transactions métier et maintenance) et PostVisualService
(import, génération explicite, lecture vérifiée). Infrastructure fournit LocalVisualStorage,
SkiaVisualValidator et SqlitePostVisualStore. Le moteur IVisualGenerator de Phase 2 est réutilisé ;
aucune génération n'est déclenchée au démarrage ou par le worker.

PostVisual contient propriétaire, origine Generated/Uploaded, clé opaque, MIME réel, dimensions,
taille, SHA-256, dates de création/validation/suppression/purge, état de préparation et métadonnées
de génération. SocialPost.SelectedVisualId est nullable ; une FK composite impose le même
propriétaire. VisualBrief et VisualUrl sont conservés. Aucun binaire n'est enregistré dans SQLite.

Les mutations sont limitées aux publications non supprimées en Draft ou Rejected.
La sélection explicite valide le visuel et remplace uniquement la sélection ; elle ne supprime
aucune version. Un candidat sélectionné ne peut pas être supprimé : sélectionner un autre
candidat d'abord. La lecture reste possible dans les autres statuts pour les publications actives.
La suppression d'un candidat est logique. Les DTO historiques de publication restent inchangés.

## Accès administratif local

Cette protection n'est PAS une authentification de production. OAuth LinkedIn n'est pas utilisé
comme authentification applicative. Les anciennes routes ne sont pas sécurisées par ce dispositif.

Toutes les routes visuelles sont absentes par défaut et hors Development, y compris dans OpenAPI.
Pour les activer volontairement sur un poste de développement :
- ASPNETCORE_ENVIRONMENT=Development
- Visuals__Enabled=true
- SOCIALFLOW_VISUAL_ADMIN_SECRET : secret aléatoire de 32 à 256 caractères, fourni uniquement
  par variable d'environnement, distinct de OpenAI:ApiKey et OpenAI:Images:ApiKey.
- En-tête de chaque requête : X-SocialFlow-Visual-Admin. Jamais dans une URL.
- HTTPS requis par défaut. Visuals__AllowHttpLoopback=true permet exceptionnellement HTTP
  sur loopback seulement lorsque HTTPS n'est pas disponible.

Ne jamais enregistrer le secret dans Git, les exemples, les journaux ou un fichier appsettings.
Le contrôle précède le model binding. Les empreintes SHA-256 sont comparées avec
CryptographicOperations.FixedTimeEquals. IP distante loopback et Host localhost/loopback
sont requis ; les en-têtes Forwarded et X-Forwarded-* sont refusés.
Aucun middleware de proxy ne doit réécrire l'adresse distante en amont de ces contrôles.
Réponses privées no-store et nosniff ; erreurs sans données fournisseur ou secrets.
Les routes ne conviennent pas à un déploiement partagé ou distant.

## Stockage et validation

SkiaSharp 4.153.1 décode intégralement PNG et JPEG, sans réencoder les originaux.
Les noms, extensions et MIME fournis à l'import sont ignorés. PNG : CRC des chunks,
structure et fin exacte vérifiés, animations refusées. JPEG : décodage complet et marqueurs
de début/fin requis. Le SHA-256 est revérifié à chaque lecture.

Limites par défaut, configurables uniquement à la baisse via Visuals:Limits :
MaxBytes=20971520, MaxDimension=8192, MaxPixels=36000000.
Deux décodages natifs simultanés au maximum ; annulation avant/après décodage
(le décodage natif lui-même n'est pas interrompable). Limite multipart HTTP 21 Mio ;
corps JSON de génération 64 Kio. Le moteur Phase 2 conserve ses propres limites.

Visuals:Storage:RootPath doit être un chemin absolu privé. Défaut :
LocalApplicationData/Redeemer.SocialFlow/Visuals.
Les racines de l'API, wwwroot et du frontend sont exclues ; les chemins de type reparse
sont refusés. Les clés sont des identifiants générés serveur de 32 caractères hexadécimaux.
Écriture dans un temporaire exclusif, flush disque, déplacement sans écrasement.
Aucune route statique n'est ajoutée. Le compte système de l'application doit avoir le contrôle
exclusif du dossier ; les ACL et l'exclusion de tout serveur statique externe relèvent du déploiement.
L'interface permet un futur adaptateur cloud sans modifier le métier.

## Cohérence et maintenance

Une réservation SQLite précède l'écriture du fichier. Le candidat n'est visible qu'après
confirmation de l'écriture et nouvelle vérification du statut. Un échec laisse une réservation
abandonnée récupérable ; aucun fichier n'est effacé lors d'un résultat de commit ambigu.
Les transactions SQLite sérialisent sélection, suppression, finalisation et claims de publication.
Les champs de concurrence existants restent actifs ; la sélection est aussi un jeton de concurrence.

IPostVisualStore.CleanupAsync(cutoff, token) est une maintenance explicitement invoquée,
non planifiée et non exposée par HTTP. Rétention minimale 24 h ; lots de 100 clés.
Elle traite les suppressions logiques et réservations anciennes non terminées.
Exclusions : visuel sélectionné et TOUT média d'une publication possédant une
PublicationOperation durable, quel que soit son état. Les candidats prêts non supprimés
et les médias d'une publication supprimée seule ne sont pas purgés automatiquement.
Les tombstones restent en base ; les clés déjà purgées sont revisitées pour traiter un écrivain
retardataire. Un échec de stockage laisse la maintenance réessayable.
Aucun worker existant n'a été modifié. L'exécution opérationnelle périodique de cette
maintenance reste à raccorder dans une phase ultérieure.

## API

Préfixe : /api/posts/{postId}/visuals
- POST /upload : multipart, champ file ; retour 201 et Location protégée.
- POST /generate : IdentityId, Name, Style, Colors, Slogan optionnel, VisualBrief optionnel.
  Sans override, le VisualBrief de la publication est utilisé ; un override ne le remplace pas.
  L'identité est fournie explicitement ; aucune marque ni aucun logo n'est ajouté automatiquement.
- GET / : liste des candidats actifs, métadonnées et indicateur Selected.
- GET /{visualId} : contenu binaire pour prévisualisation contrôlée.
- POST /{visualId}/select : sélection et validation.
- DELETE /{visualId} : suppression logique, 204.

La prévisualisation nécessite un client capable d'envoyer l'en-tête administratif
(par exemple récupération du binaire puis URL blob). Aucune modification React n'est incluse.

## Migration

20261010005243_AddPostVisuals ajoute PostVisuals et SocialPosts.SelectedVisualId nullable.
Les migrations précédentes sont intactes. Avec SQLite, EF peut reconstruire la table
pour ajouter la FK ; la préservation des données est vérifiée sur une base temporaire.
La migration n'est jamais appliquée au démarrage par ce changement.
Sauvegarder la base ET le répertoire des médias avant toute application manuelle.
Aucune migration n'a été appliquée à la base active pendant le développement.
Le Down retire les métadonnées de cette phase, pas les fichiers ; ce n'est pas un nettoyage.

## Dépendance et licences

ImageSharp n'est pas installé. La licence officielle Six Labors Split License nécessite une
licence commerciale pour certains usages directs dans des logiciels fermés d'entreprises
à but lucratif atteignant le seuil de revenu indiqué. L'éligibilité actuelle et future n'était
pas établie : https://sixlabors.com/pricing/ et
https://github.com/SixLabors/ImageSharp/blob/main/LICENSE

SkiaSharp a été approuvé comme alternative : licence MIT permettant l'usage commercial,
avec conservation des notices. Les notices de Skia et composants natifs doivent également
accompagner les distributions. Copies des notices du package 4.153.1 : docs/third-party.
Source officielle : https://github.com/mono/SkiaSharp/blob/main/LICENSE.md
Package : https://www.nuget.org/packages/SkiaSharp/4.153.1

Validation effectuée sur Windows. Linux nécessite le package d'assets natifs correspondant
et une validation de déploiement ; aucun package Linux supplémentaire n'a été ajouté.
Aucun appel payant n'est nécessaire pour les tests : générateur et transport sont simulés.
