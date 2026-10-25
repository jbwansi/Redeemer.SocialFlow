# Inspection ponctuelle des autorisations Facebook

Pour comparer les cibles entre permissions et avec la Page attendue, voir
[Comparaison locale des ressources Facebook](facebook-resource-comparison.md).

Par défaut, `expires_in`/`expires` suffit au contrôle temporel de l'échange :
`debug_token` n'est alors pas appelé et les ressources granulaires restent inconnues.

L'option `Facebook:OAuthResourceDiagnosticsEnabled` (false par défaut) ajoute,
uniquement en Development, **un appel supplémentaire par échange OAuth réussi**,
après les validations et la récupération habituelle des Pages. Elle ne déclenche
ni publication ni interrogation périodique. L'appel utilise le jeton utilisateur
final et l'authentification applicative existante, avec une limite de cinq secondes.
Même si le contrôle d'expiration a déjà utilisé debug_token, cette inspection
facultative reste un appel distinct. Aucun résultat n'est persisté.

Depuis la racine du dépôt, arrêter le lanceur puis activer :

```powershell
dotnet user-secrets set "Facebook:OAuthResourceDiagnosticsEnabled" "true" --project .\Redeemer.SocialFlow.Api
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

Effectuer un nouveau consentement Facebook depuis l'interface. Dans la console
`[API]`, chercher `Facebook optional permission resources`. Trois lignes sont
émises : une pour `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`.
Exemple si les métadonnées sont absentes (sans préjuger de la réponse réelle) :

```text
Facebook optional permission resources: Permission=pages_show_list; Granted=unknown; GranularScopePresent=unknown; TargetIdsCount=unknown; DiagnosticStatus=completed
Facebook optional permission resources: Permission=pages_read_engagement; Granted=unknown; GranularScopePresent=unknown; TargetIdsCount=unknown; DiagnosticStatus=completed
Facebook optional permission resources: Permission=pages_manage_posts; Granted=unknown; GranularScopePresent=unknown; TargetIdsCount=unknown; DiagnosticStatus=completed
```

- `Granted` : correspondance exacte dans `debug_token.data.scopes` ; absent ou mal formé = `unknown`.
- `GranularScopePresent` : entrée présente dans un tableau valide ; métadonnée absente ou mal formée = `unknown`.
- `TargetIdsCount` : nombre d'éléments des tableaux explicitement présents pour cette permission. Seul un tableau explicitement vide donne zéro ; absence = `unknown`. Aucun identifiant n'est journalisé.
- `DiagnosticStatus` : `completed`, `invalid_response`, `request_failed`, ou `timeout`. Completed indique la lecture du diagnostic, pas une validation de publication.

Les échecs facultatifs produisent trois lignes avec les valeurs inconnues et
n'empêchent pas la connexion. Une annulation de la requête cliente reste propagée.
Les diagnostics OAuth préexistants sont conservés : `not_requested` avec
`relative_expiry_present` décrit le contrôle d'expiration initial, avant cette
inspection facultative. Aucun résultat diagnostique ne modifie les autorisations,
la sélection des Pages ou les règles de publication.

Après le test, arrêter le lanceur, désactiver puis redémarrer :

```powershell
dotnet user-secrets set "Facebook:OAuthResourceDiagnosticsEnabled" "false" --project .\Redeemer.SocialFlow.Api
powershell.exe -NoProfile -File .\scripts\Start-Local.ps1
```

Les variables d'environnement .NET ont priorité sur User Secrets : ne pas définir
`Facebook__OAuthResourceDiagnosticsEnabled` si cette procédure est utilisée.
Une liste de Pages vide ne peut pas être expliquée définitivement sans la nouvelle
réponse Meta ; ces observations ne constituent pas un contournement des permissions.
