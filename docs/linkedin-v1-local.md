# LinkedIn V1 locale — procédure complète

## Interface et API

Le panneau de Posts affiche connexion, expiration, identifiant du profil et activation du
worker. Connecter LinkedIn ouvre directement l'API HTTPS dans un nouvel onglet : aucun fetch
de redirection ni proxy HTTP pour le cookie OAuth Secure. Revenir dans SocialFlow actualise
le statut par focus/visibilité. Un bouton permet aussi l'actualisation manuelle.

Les détails du post consultent l'opération durable après rechargement, même si la connexion
expire/disparaît. Le serveur fournit canPublish. Seul un LinkedIn Scheduled échu sans opération,
avec connexion valide et publication activée, peut être publié. Aucune conversion d'Approved.
OK et Cancel schedule sont conservés ; les actions sont désactivées pendant une requête.

| Méthode | Route Development | Réponse 200 |
| --- | --- | --- |
| GET | `/api/dev/linkedin/runtime` | Connexion, expiration, destination, activation publication/worker, URL OAuth |
| GET | `/api/dev/linkedin/posts/{id}/publication` | `{ operation, canPublish }`, opération nullable |
| GET | `/api/dev/linkedin/posts/{id}/preview` | `{ content, destination, confirmation }` |
| POST | `/api/dev/linkedin/posts/{id}/publish` | Corps `{ "confirmation": "valeur opaque reçue" }`, opération durable |

La confirmation, protégée par Data Protection et valable cinq minutes, lie post, contenu,
destination et OperationId. Le POST ne reçoit aucune destination libre ni jeton OAuth.
Le serveur revérifie le contexte et appelle IPublishScheduledPost avec le membre connecté.
La modale montre le texte exact et le profil avant l'envoi. Un résultat terminal observé
actualise le post et la liste. Le worker peut gagner la course pendant une confirmation :
le POST retourne alors l'opération existante sans nouvel envoi.

États durables : 1 Indeterminate, 2 Confirmed, 3 Failed. completedAt null signifie prise en
charge en cours ou interrompue, jamais autorisation de renvoi. Erreurs génériques : 400
confirmation invalide/expirée ; 404 post absent ; 409 non-éligible/contexte changé ; 503
publication désactivée ou statut indisponible ; 499 annulation. Routes 404 hors Development,
aucun jeton exposé. Le build frontend production masque ces fonctionnalités.

## Worker et limites

Les options LinkedIn:PublicationEnabled ET LinkedIn:WorkerEnabled doivent être true en
Development ; toutes deux sont désactivées par défaut. Premier passage après 30 secondes,
aucun POST immédiat au démarrage. Maximum 20 échéances par passage, séquentiellement,
par date puis ID. Comparaison DateTimeOffset en mémoire comme des instants (limite SQLite).
Toute opération existante est exclue : Confirmed, Failed, Indeterminate ou interrompue.
Sans connexion valide, aucune prise en charge. Manuel et worker partagent IPublishScheduledPost.

L'API doit rester ouverte. Au redémarrage, les échéances manquées sans opération peuvent
être traitées. La destination est le membre connecté lors du traitement : reconnecter un
autre compte change la destination des posts planifiés sans opération. V1 mono-utilisateur
Windows, identifiant du profil affiché sans nom. Aucun renouvellement, retry, réconciliation,
suppression distante ou garantie exactly-once. Pas de nouveau contrôle d'accès multi-utilisateur.

## Procédure unique de test

Aucune des activations/publications suivantes n'a été exécutée pendant l'implémentation.

1. Dans LinkedIn Developer Portal, vérifier les produits pour openid, profile et
   w_member_social (sans email), et enregistrer exactement le callback
   `https://localhost:65474/api/dev/linkedin/callback`.
2. Depuis la racine, configurer hors Git avec vos valeurs. Ci-dessous, valeurs fictives ;
   choisir le chemin absolu de la base SQLite existante de l'API :

   ```powershell
   dotnet user-secrets set "LinkedIn:ClientId" "CLIENT_ID_FICTIF" --project .\Redeemer.SocialFlow.Api
   dotnet user-secrets set "LinkedIn:ClientSecret" "CLIENT_SECRET_FICTIF" --project .\Redeemer.SocialFlow.Api
   dotnet user-secrets set "LinkedIn:PublicationEnabled" "true" --project .\Redeemer.SocialFlow.Api
   dotnet user-secrets set "LinkedIn:WorkerEnabled" "false" --project .\Redeemer.SocialFlow.Api
   $env:ConnectionStrings__SocialFlow = "Data Source=C:\CHEMIN_LOCAL\socialflow.db"
   dotnet ef database update --project .\Redeemer.SocialFlow.Infrastructure --startup-project .\Redeemer.SocialFlow.Api
   dotnet dev-certs https --trust
   dotnet run --project .\Redeemer.SocialFlow.Api --launch-profile Redeemer.SocialFlow.Api
   ```

   Aucun schéma nouveau dans cet incrément ; EF applique les migrations existantes si nécessaire.
3. Dans un deuxième terminal, avec les dépendances frontend déjà installées :

   ```powershell
   Set-Location .\Redeemer.SocialFlow.Web
   $env:API_PROXY_TARGET = "https://localhost:65474"
   npm run dev
   # Si npm global est défectueux : node node_modules/vite/bin/vite.js --host 127.0.0.1
   ```

   Ouvrir `http://127.0.0.1:5173/#posts` (serveur Vite Development, pas build production).
4. **Connecter LinkedIn**, terminer OAuth dans le nouvel onglet puis revenir. Vérifier
   profil, expiration et **Publication automatique désactivée**.
5. **Create post** : saisir titre/texte de test assumé public, plateforme LinkedIn.
   Enregistrer Draft, relire/modifier, **Submit for review**, puis **Approve**.
6. Choisir une date réellement future (par exemple deux minutes), **Schedule**, attendre.
7. Ouvrir les détails, **Actualiser la publication**, **Publier sur LinkedIn**. Vérifier texte
   et profil, puis **Confirmer la publication** : ce dernier clic envoie réellement le texte.
8. Vérifier Published et l'identifiant LinkedIn. Recharger et rouvrir le post : l'opération
   doit rester disponible. Sur linkedin.com, consulter le profil connecté puis son activité/
   publications et vérifier le texte. Indeterminate nécessite cette vérification manuelle ;
   ne pas contourner la protection en recréant une opération.
9. Pour tester ensuite l'automatique, vérifier toutes les échéances en attente, définir
   LinkedIn:WorkerEnabled à true via User Secrets et redémarrer l'API. Vérifier l'indication
   d'activation, refaire le cycle avec un nouveau post et une échéance future, laisser l'API
   ouverte et attendre l'échéance plus 30 secondes sans clic manuel. Pour désactiver les envois,
   remettre WorkerEnabled et PublicationEnabled à false puis redémarrer.

Tests : SQLite et fournisseurs simulés pour échéances, concurrence, résultats durables et
absence de retry ; React avec API simulée pour navigation OAuth, confirmation/annulation,
double clic, erreurs, résultats et consultation. Aucun appel réel LinkedIn pendant les tests.
