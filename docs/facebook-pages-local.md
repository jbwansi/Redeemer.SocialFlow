# Phase 7A — Facebook Pages en Development local

Cette phase publie un SocialPost Facebook sur une Page choisie. Une publication conserve une seule plateforme ; la publication simultanée LinkedIn/Facebook (7B) n’est pas implémentée.

## Fonctionnement

Le routage PlatformPostPublisher choisit l’adaptateur LinkedIn ou Facebook. LocalPublicationService partage les confirmations, la recherche des échéances et l’appel du cas d’usage PublishScheduledPost. Le worker déjà présent garde son unique minuterie de 30 secondes et traite les deux plateformes séparément. Les indicateurs d’activation sont indépendants.

Le workflow reste Draft → Ready → Approved → Scheduled. Pour une publication manuelle comme automatique, le post doit être Scheduled et arrivé à échéance. Aucun post Approved n’est planifié implicitement. Choisir Facebook lors de la création du post dans le sélecteur existant.

PublicationOperation fige plateforme, fournisseur, Page, texte et visuel sélectionné/validé avant l’appel externe. Une opération déjà prise en charge, même échouée, interrompue ou indéterminée, n’est jamais réémise automatiquement. Une réponse perdue, un identifiant de post absent ou une erreur transitoire reste indéterminée. Le résultat doit alors être vérifié sur Facebook.

Le texte utilise POST /{page-id}/feed. L’image originale vérifiée par longueur et SHA-256 est envoyée en multipart à POST /{page-id}/photos avec son texte et published=true. Cet appel peut publier pendant le transfert : tout résultat ambigu est conservé comme tel. Aucun envoi de texte seul après un échec d’image. Aucun fichier privé n’est servi publiquement et aucune image n’est recomposée.

La Page active est une sélection locale globale. Elle sert aux prochains posts à traiter ; la destination d’une opération déjà prise en charge reste figée. Changer la Page après une prévisualisation invalide la confirmation manuelle. La sélection d’une Page distincte par brouillon est hors de cette phase.

## Configuration Visual Studio 2026

Aucune configuration active, clé ou token n’a été écrit par cette phase. Les valeurs par défaut désactivent Facebook et son worker.

Dans **Gérer les secrets utilisateur** du projet API (hors Git), compléter :

```json
{
  "Facebook": {
    "Enabled": true,
    "AppId": "IDENTIFIANT_APPLICATION_META",
    "AppSecret": "SECRET_APPLICATION_META",
    "PublicationEnabled": true,
    "WorkerEnabled": false
  }
}
```

Conserver les autres paramètres existants. AppId est l’identifiant numérique de l’application Meta, pas celui d’une Page. Ne pas placer AppSecret dans React, VITE_*, les launchSettings versionnés, une URL de navigateur ou les logs. Les user-secrets sont hors dépôt mais ne constituent pas un coffre chiffré ; le secret applicatif peut aussi être fourni via Facebook__AppSecret dans l’environnement du processus serveur.

Réutiliser votre lanceur local de la Phase 4 pour lancer Visual Studio et Vite avec le même SOCIALFLOW_VISUAL_ADMIN_SECRET aléatoire en mémoire. Celui-ci doit être distinct des clés OpenAI et du secret Meta. La passerelle existante doit recevoir SOCIALFLOW_VISUAL_GATEWAY_ENABLED=true et NODE_EXTRA_CA_CERTS pointant vers le certificat public de développement approuvé. Aucune désactivation TLS n’est requise ou autorisée. Le certificat localhost doit être valide ; l’API utilise HTTPS localhost:65474 et Vite localhost:5173 ou 127.0.0.1:5173.

Le relais Facebook emprunte /local-visuals/dev/facebook pour réutiliser la protection locale existante. L’activation Facebook ne dépend pas de Visuals:Enabled pour le texte seul ; la passerelle Vite reste nécessaire. Pour gérer/générer les visuels, conserver l’activation visuelle existante.

Dans l’application Meta, enregistrer exactement cette redirection OAuth :

```text
https://localhost:65474/api/dev/facebook/callback
```

Configurer le produit/login et les accès Pages appropriés à votre application, puis accorder pages_show_list, pages_read_engagement et pages_manage_posts. Seules les Pages retournées par me/accounts avec une tâche de création de contenu sont sélectionnables. Selon les rôles et le mode de l’application, l’accès avancé et une revue Meta peuvent être nécessaires : les mocks ne valident pas l’éligibilité de votre compte.

Après redémarrage de l’API et de Vite : ouvrir Posts, cliquer **Connecter Facebook**, puis **Ouvrir l’autorisation Facebook** ; autoriser les Pages voulues ; revenir à l’interface et actualiser ; sélectionner une Page puis cliquer **Utiliser cette Page**. La sélection n’effectue aucune publication.

Conserver WorkerEnabled=false pour le premier essai manuel contrôlé. Après validation, activer explicitement Facebook:WorkerEnabled et redémarrer l’API pour les échéances. Cela peut traiter les publications Facebook déjà Scheduled et arrivées à échéance. L’API doit rester démarrée. Aucun test réel n’a été effectué par Codex.

## Sécurité et tokens

Les routes administratives sont absentes hors Development ou sans Facebook:Enabled. Elles requièrent HTTPS, loopback, le secret serveur comparé en temps constant et refusent les en-têtes Forwarded. La passerelle impose Host/Origin locaux exacts et l’en-tête non simple existant contre CSRF ; elle ne suit pas de redirection et valide le certificat HTTPS amont.

POST connect protégé délivre uniquement un ticket éphémère à usage unique vers la route HTTPS authorize. Celle-ci fixe un cookie HttpOnly/Secure/SameSite=Lax. Le callback vérifie l’état aléatoire, sa durée et sa liaison au navigateur avant échange du code. Aucun secret administratif, token utilisateur ou token de Page n’est renvoyé à React. Les tickets et états expirent au redémarrage. OAuth n’est pas une authentification applicative de production.

Les accès sont conservés côté serveur dans une enveloppe ASP.NET Data Protection dont les clés sont protégées par DPAPI sous Windows. Répertoire par défaut : %LOCALAPPDATA%/Redeemer.SocialFlow/Facebook. Garder cet emplacement privé. Le code échange le token initial pour une autorisation longue durée et borne l’usage des tokens de Page par l’expiration de cette autorisation. Il n’existe pas de renouvellement automatique ni de garantie de durée fixe : reconnecter après expiration/révocation. Les permissions et tâches sont revérifiées par HTTP avant chaque publication.

Déconnecter efface la connexion locale et tous ses tokens. Cela ne révoque pas l’application dans le compte Meta et ne peut annuler une requête déjà envoyée. Pour révoquer l’autorisation distante, utiliser les paramètres Meta.

Les clients HTTP n’ont ni logs, ni suivi de redirection, ni retries ; erreurs brutes et secrets ne sont pas exposés. Les URLs de pagination retournées ne sont jamais suivies : seuls les curseurs sont réutilisés vers graph.facebook.com fixe.

## Persistance et références

Aucune migration EF nécessaire : SocialPlatform.Facebook et les colonnes de PublicationOperation sont déjà présents. Aucune migration appliquée, aucune modification des données actives ni des anciennes migrations.

Graph API est épinglée à **v26.0**, version présente dans le SDK officiel Meta consulté :
- [Configuration du SDK officiel](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/apiconfig.py)
- [Endpoints Page officiels : feed/photos](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/page.py)
- [Endpoint utilisateur accounts](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/user.py)
- [Documentation OAuth Meta](https://developers.facebook.com/docs/facebook-login/guides/advanced/manual-flow/)
- [Publication Pages](https://developers.facebook.com/docs/pages-api/posts/)

Les pages de documentation web Meta ont renvoyé des erreurs d’accès/429 lors de cette phase. Les endpoints et la version ont été recoupés avec le SDK officiel, sans ajouter ce SDK comme dépendance. La configuration OAuth et les autorisations effectives devront être validées avec votre application Meta lors du prochain test réel.
