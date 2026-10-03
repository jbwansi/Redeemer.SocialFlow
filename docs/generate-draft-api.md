# POST /api/posts/generate-draft

L'accès reste celui de l'endpoint existant (aucune nouvelle restriction ou
autorisation). Le mode est désormais obligatoire. Les enums JSON sont numériques :
Editorial = 1, Grounded = 2 ; Facebook = 1, LinkedIn = 2, Instagram = 3.
Les champs inconnus sont ignorés par la désérialisation existante : des
`referencePassages` fournis par le client ne sont jamais transmis à Application.
En mode Grounded, seule la recherche serveur fournit le contexte.

## Requêtes

Editorial :

```json
{
  "subject": "Accompagnement des bénévoles",
  "objective": "Présenter des conseils pratiques",
  "audience": "Responsables associatifs",
  "platform": 2,
  "mode": 1
}
```

Grounded :

```json
{
  "subject": "Accompagnement des bénévoles",
  "objective": "Présenter des conseils pratiques",
  "audience": "Responsables associatifs",
  "platform": 2,
  "mode": 2
}
```

## Succès : 201 Created

`Location: /api/posts/11111111-1111-1111-1111-111111111111`

Exemple conforme au contrat sérialisé, avec des valeurs fictives de test :

```json
{
  "outcome": 1,
  "draft": {
    "post": {
      "id": "11111111-1111-1111-1111-111111111111",
      "title": "Accompagner les bénévoles",
      "content": "Clarifier leur rôle et écouter leurs attentes.",
      "platform": 2,
      "status": 1,
      "callToAction": null,
      "visualBrief": null,
      "visualUrl": null,
      "scheduledAt": null,
      "publishedAt": null,
      "createdAt": "2026-10-03T12:00:00+00:00",
      "updatedAt": "2026-10-03T12:00:00+00:00"
    },
    "warnings": ["Relecture humaine requise."]
  },
  "metadata": {
    "provider": "TestProvider",
    "model": "test-model"
  },
  "referencePassages": [
    {
      "knowledgeChunkId": "22222222-2222-2222-2222-222222222222",
      "knowledgeDocumentId": "33333333-3333-3333-3333-333333333333",
      "documentTitle": "TEST — Accompagnement des bénévoles",
      "documentVersion": 1,
      "sourceType": 7,
      "authorityLevel": 1,
      "language": "fr",
      "content": "Pour accompagner les bénévoles, clarifier leur rôle, écouter leurs attentes et identifier leurs besoins de soutien.",
      "chunkIndex": 0,
      "pageNumber": null,
      "section": "Données fictives de test"
    }
  ],
  "postId": "11111111-1111-1111-1111-111111111111"
}
```

Editorial retourne la même structure avec `referencePassages: []`.
Les avertissements sont dans `draft.warnings`. Le post est toujours Draft
(`status: 1`), consultable par GET sur Location. Le post et l'audit AiGeneration
sont persistés comme auparavant. Les références sont uniquement retournées :
elles ne sont ni persistées ni une garantie que toutes les affirmations sont vérifiées.

## Absence de passages : 200 OK

```json
{
  "outcome": 2,
  "draft": null,
  "metadata": null,
  "referencePassages": [],
  "postId": null
}
```

## Contexte trop volumineux : 200 OK

```json
{
  "outcome": 3,
  "draft": null,
  "metadata": null,
  "referencePassages": [],
  "postId": null
}
```

Ces deux résultats sont des issues normales du parcours : aucun post, aucun
audit, aucune Location et aucun repli vers Editorial. Le générateur n'est pas appelé.

Requête invalide (dont mode absent, null ou inconnu) : 400 ProblemDetails.
Erreur de génération : 500 ProblemDetails générique sans détails internes.
Annulation de la requête : comportement 499 existant conservé.

Le frontend n'est pas modifié dans cet incrément. Ses anciens appels sans mode
reçoivent donc 400 ; sa lecture des succès devra également utiliser `draft.post`
et `draft.warnings` au lieu de `post` et `warnings` à la racine.
