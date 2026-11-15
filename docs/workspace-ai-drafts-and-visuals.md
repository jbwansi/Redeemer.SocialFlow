# Brouillons IA et visuels dans un espace de travail

Depuis **Posts**, un Owner, Admin ou Editor peut choisir **Générer un brouillon**, sélectionner Facebook ou LinkedIn, et ajouter un contexte facultatif (4 000 caractères). Le mode Éditorial utilise le brief ; le mode Avec mes sources recherche les documents éligibles du workspace. Sans résultat exploitable dans ce dernier mode, aucun brouillon n’est créé.

Après enregistrement du brouillon, le panneau des visuels permet de générer une image à partir du brief visuel ou d’importer une image PNG/JPEG. Choisir une version la rend disponible au parcours de publication existant. La génération de texte, la génération d’image et la sélection du visuel ne planifient et ne publient rien.

Le panneau des visuels du workspace est activé explicitement, côté serveur :

```powershell
dotnet user-secrets set "Visuals:WorkspaceEnabled" "true" --project .\Redeemer.SocialFlow.Api
```

Redémarrer l’API. Les actions texte et image nécessitent la configuration OpenAI côté serveur ; l’import n’en a pas besoin. Le panneau accepte une identité proposée dans le formulaire (modifiable), sans dépendre de la bibliothèque d’administration locale. Les mutations exigent les droits d’édition et le CSRF de la session ; les lectures vérifient l’appartenance au workspace.

Pour une recette sans publication, générer un brouillon Facebook et un brouillon LinkedIn avec le même sujet et un contexte explicite. Vérifier le texte et le brief visuel, générer ou importer une image puis la sélectionner. Rouvrir chaque brouillon pour vérifier que le bon visuel est affiché. Relire et approuver séparément avant de choisir de publier ou de planifier.
