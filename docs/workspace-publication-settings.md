# Paramètres de publication par espace

Dans **Paramètres de publication**, un Owner/Admin peut choisir une destination Facebook et LinkedIn préférée, le fuseau IANA (par exemple Europe/Zurich) et jusqu’à huit horaires proposés. Le choix de la destination reste visible et modifiable avant la confirmation. Seules les destinations encore éligibles sont présélectionnées. Les créneaux remplissent l’heure du jour choisi dans le formulaire ; ils ne programment rien seuls.

Le bouton de mise en pause est indépendant pour Facebook et LinkedIn. Une autorisation explicite par post et un worker global actif restent nécessaires. La pause laisse les autorisations en place : la reprise peut traiter des posts déjà autorisés dont l’heure est passée. Elle ne peut pas annuler un appel de publication déjà parti. Les espaces qui n’ont jamais enregistré de réglages conservent la possibilité d’autoriser une publication automatique Facebook. LinkedIn est désactivé par défaut dans chaque espace. Les publications LinkedIn restent disponibles manuellement ; aucun post existant n’est automatiquement autorisé.

Le worker reste activé côté serveur via `SocialConnections:Publication:WorkerEnabled`. Son intervalle est `SocialConnections:Publication:PollIntervalSeconds` (30 secondes par défaut, borné à 5–300 secondes). Les secrets et le drapeau serveur Facebook ne sont pas modifiés par cet écran.

Pour comparer la version Vite au build compilé, utiliser le même espace et le même écran : `https://localhost:5173/#publication-settings` et `https://localhost:65474/#publication-settings`. Vite sert les sources en direct et relaie normalement `/api` au port 65474 ; le port 65474 sert `dist` en développement. Refaire `npm.cmd run build` dans le projet Web pour actualiser ce dernier.

Appliquer la migration **AddWorkspacePublicationSettings** avant de démarrer la nouvelle version sur une base existante. Elle ajoute une colonne nullable à Workspaces. Aucun envoi et aucune autorisation de post ne sont créés par la migration.
