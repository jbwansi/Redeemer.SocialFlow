# Phase 4 — interface des visuels et passerelle locale

La gestion des visuels est disponible dans l'éditeur des publications déjà enregistrées.
VisualBrief reste éditable. Le panneau permet génération explicite, régénération, import PNG/JPEG,
prévisualisation des versions, sélection/validation et suppression logique des candidats inutilisés.
Les données éditoriales restent dans l'état du formulaire. Les boutons de sauvegarde et fermeture
sont bloqués pendant une opération ; l'attente peut être annulée.
PostDetails affiche uniquement le visuel sélectionné. Les publications sans image restent utilisables.

## Activation locale explicite

La fonctionnalité reste désactivée par défaut. Aucun secret ou configuration active n'a été créé.
Prérequis API : Phase 3, migration appliquée volontairement par l'opérateur, environnement
Development, Visuals:Enabled=true et SOCIALFLOW_VISUAL_ADMIN_SECRET dans le processus API.

Pour le processus Vite :
- SOCIALFLOW_VISUAL_GATEWAY_ENABLED=true
- SOCIALFLOW_VISUAL_ADMIN_SECRET : même secret administratif que l'API, uniquement dans
  l'environnement du processus serveur. Ne pas utiliser une variable VITE_* ni un fichier versionné.
- Si nécessaire, NODE_EXTRA_CA_CERTS : chemin du certificat/CA de développement au format PEM,
  exporté et vérifié par l'opérateur. Le fichier public ne doit pas contenir la clé privée.
  Cette variable doit être définie avant de démarrer Node.
- Démarrer le script npm run dev en mode development.
- Ouvrir http://127.0.0.1:5173 ou http://localhost:5173.

La destination est FIXE : connexion 127.0.0.1:65474 en HTTPS, nom TLS localhost.
Le certificat doit être valide pour localhost et reconnu par Node. Une erreur TLS renvoie 502,
sans tentative non sécurisée. Ne pas désactiver la validation TLS.
Le proxy générique /api existant est conservé ; la passerelle /local-visuals ne l'utilise pas.

## Sécurité

Le module server/visualGateway.ts s'exécute exclusivement dans le processus Vite.
Il n'est actif que sur le serveur de développement et seulement sur activation explicite.
Il vérifie IP distante loopback, Host exact, Origin exact sur les mutations, et Sec-Fetch-Site
lorsque présent. Les en-têtes Forwarded/X-Forwarded-* sont refusés.
L'en-tête non simple X-SocialFlow-Local:1 est requis sur toutes les requêtes.
Combiné au contrôle Origin et à l'absence de CORS/préflight autorisé, il protège contre les
requêtes intersites ; ce n'est pas un secret ni une authentification de production.

Seules les six routes visuelles avec UUID et méthodes autorisées sont relayées.
Aucune query string, route de publication ou destination arbitraire n'est acceptée.
Le secret administratif fourni par un client n'est jamais transféré : le serveur injecte sa
propre valeur. Aucun cookie ou en-tête de proxy du client n'est relayé à l'API.
L'API conserve tous ses contrôles de Phase 3 ; elle refuse notamment un secret identique
à une clé OpenAI ou un environnement autre que Development.

Corps limités (64 Kio génération, 21 Mio import), réponses bornées, délai réseau de 180 s,
aucune relance automatique, aucune redirection suivie. Les réponses sont no-store/nosniff.
Le secret ne figure ni dans les URL, ni dans les journaux ajoutés, ni dans les réponses.
Un build avec secret sentinelle fictif a confirmé son absence du bundle client.

## React et transport

Le client partagé conserve ApiError et AbortSignal ; il prend désormais en charge FormData
sans imposer Content-Type (le navigateur ajoute la boundary), ainsi que les réponses Blob.
Le client visuel appelle une route de même origine fixe, indépendante de VITE_API_BASE_URL.

Les images sont récupérées via la passerelle protégée puis affichées par URL.createObjectURL.
Les URLs sont révoquées au démontage/changement d'image. Les requêtes sont interrompues au
démontage et les réponses tardives ne créent pas d'URL. Aucun chemin de stockage n'est exposé.

Seules les publications Draft/Rejected autorisent les mutations ; le backend arbitre aussi les
conflits avec un statut devenu obsolète. Un visuel sélectionné ne peut pas être supprimé.
La génération utilise le brief courant du formulaire sans enregistrer implicitement les autres
champs. L'identité (identifiant, nom, style, palette, slogan facultatif) est saisie explicitement
et peut être enregistrée explicitement dans le stockage local du navigateur. Seuls les champs d'identité sont conservés, sans secret ni contenu éditorial. Le modèle Redeemer Holding est proposé sur choix explicite et reste modifiable.

Les seuls appels automatiques sont des lectures de disponibilité, liste et image.
Aucun chargement, rafraîchissement ou enregistrement de publication ne lance une génération.
Une annulation arrête l'attente et propage AbortSignal ; elle ne garantit pas l'annulation d'un
traitement fournisseur déjà effectué. Actualiser les candidats avant de réessayer après annulation.

## Validation et limites

375 tests unitaires .NET, 407 tests d'intégration .NET et 97 tests frontend/passerelle passent.
32 tests ajoutés couvrent actions explicites, versions, imports, erreurs, statuts, annulation,
conservation éditoriale, blobs et sécurité du relais. Générateur et HTTPS amont simulés.
TypeScript (serveur et client) et build de production passent. Aucune dépendance ajoutée.

La confiance TLS réelle dépend du certificat installé sur le poste ; aucun test manuel contre
une API active ni génération OpenAI réelle n'a été effectué. La passerelle reste locale :
aucune authentification de production, aucun support de serveur frontend distant.
Il faut enregistrer un nouveau brouillon avant de gérer ses visuels.
Les identités peuvent être différentes à chaque génération ; les identités enregistrées localement sont rechargeables depuis le formulaire. L'interface de visuels est exclue du build de production.
Le publisher LinkedIn et le Background Worker restent inchangés ; cette phase ne publie
pas les images sur LinkedIn. Aucune migration supplémentaire, commit ou push.
