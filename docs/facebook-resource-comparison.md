# Comparaison locale des ressources Facebook

## Faits disponibles et limites

Observations rapportées : les trois permissions `pages_show_list`,
`pages_read_engagement`, `pages_manage_posts` ont `Granted=true`,
`GranularScopePresent=true`, `TargetIdsCount=1`, `DiagnosticStatus=completed`.
L'appel `/me/accounts` a `ReturnedPagesCount=0`.

Ces nombres ne prouvent ni une ressource commune ni l'identité de la Page
Redeemer Holding. Le compteur des Pages retournées est incrémenté avant le
filtrage des jetons de Page et des tâches : zéro ne résulte donc pas du rejet
local de Pages dépourvues de jeton ou de tâche de création.

Aucune nouvelle observation réelle de Meta n'a été effectuée pendant cette
modification. Les tests utilisent des réponses simulées. L'identifiant attendu
n'a pas été fourni ni recherché dans les secrets locaux.

## Configuration locale et prochaine action

Le diagnostic existant doit être activé par
`Facebook:OAuthResourceDiagnosticsEnabled=true`, et l'environnement doit être
`Development`. Voir [la procédure existante](facebook-optional-resource-diagnostics.md).

Ajouter facultativement la clé `Facebook:OAuthResourceDiagnosticsExpectedPageId`
dans les User Secrets du projet `Redeemer.SocialFlow.Api` (Visual Studio :
**Gérer les secrets utilisateur**). Y saisir l'identifiant exact de la **Page**
Redeemer Holding, obtenu dans Meta, comme chaîne de caractères. Ne pas utiliser
l'identifiant du portefeuille Business, du compte publicitaire ou de l'application.
Ne pas placer cette valeur dans appsettings versionné, une capture, une commande
avec historique, un ticket ou des journaux. User Secrets reste un stockage local
non chiffré : conserver les protections d'accès du compte développeur.

Redémarrer l'API, effectuer une connexion Facebook et relever uniquement les
quatre lignes `Facebook optional resource comparison` : une ligne globale
`CommonTargetPresent`, et une ligne `ExpectedPageMatched` pour chaque permission.
Ne pas copier les réponses Graph brutes. Après diagnostic, désactiver l'option
et retirer la clé de comparaison des secrets locaux. Une variable d'environnement
`Facebook__OAuthResourceDiagnosticsExpectedPageId` remplace la valeur User Secrets
si elle est déjà définie ; ne pas conserver deux sources concurrentes.

## Sémantique et exactitude

| Champ | true | false | unknown |
| --- | --- | --- | --- |
| `CommonTargetPresent` | Intersection des trois ensembles non vide | Trois ensembles connus et intersection vide | Au moins un ensemble absent, mal formé ou ambigu, ou échec du diagnostic |
| `ExpectedPageMatched` | Identifiant configuré présent exactement dans l'ensemble de cette permission | Identifiant configuré valide et ensemble connu sans correspondance | Configuration absente/invalide, ensemble inconnu, ou échec du diagnostic |

Un tableau explicitement vide est connu. Une entrée absente n'est pas un tableau
vide. Une propriété dupliquée, plusieurs entrées pour la même permission, un
`target_ids` non tableau ou un élément invalide empêchent de conclure pour
l'ensemble concerné. Une entrée dont la permission ne peut pas être déterminée
rend l'analyse conservatrice. Les permissions sont comparées exactement.

Les identifiants restent en mémoire pendant l'inspection : chaînes JSON décodées
ou chiffres lexicaux d'un entier JSON, comparés avec `StringComparer.Ordinal`.
Seules les suites non vides de chiffres ASCII sont acceptées, sans conversion
numérique, arrondi, suppression de zéro initial, Trim ou rapprochement par nom.
Une notation exponentielle, un nombre décimal, un signe ou des espaces donnent
`unknown`, pas une approximation. Un entier JSON et une chaîne contenant
exactement ses chiffres correspondent ; des zéros initiaux différents ne
correspondent pas. Les ensembles ne sont ni persistés ni journalisés, et aucun
objet contenant leurs valeurs n'est passé au logger. Les nouvelles lignes ne
contiennent que les noms de permissions prédéfinis et `true/false/unknown`.

`Granted` reste une observation distincte : la présence d'une cible ne constitue
pas une nouvelle décision d'autorisation. Aucun changement des validations OAuth,
de la publication, de LinkedIn, de la sélection de Page ou des endpoints utilisés.
Le diagnostic facultatif conserve sa limite de cinq secondes et ses erreurs
non bloquantes ; l'annulation explicite du client reste propagée.

## Si les ressources correspondent à la Page attendue

Trois `ExpectedPageMatched=true` confirment l'identité de la cible dans les trois
permissions. `CommonTargetPresent=true` seul confirme seulement une cible commune.
Même une correspondance complète ne prouve pas que `/me/accounts` énumère cette
Page pour l'identité portée par le jeton.

Meta présente `/me/accounts` comme la liste des Pages gérées par un utilisateur,
interrogée avec son User Access Token dans sa
[collection officielle](https://www.postman.com/meta/instagram/request/0vuw3vk/get-access-tokens-of-pages-you-manage).
Meta distingue également [l'accès Facebook et l'accès par tâches](https://www.facebook.com/help/289207354498410/r.php/)
: l'accès par tâches permet de travailler via les outils Business sans pouvoir
basculer vers la Page dans Facebook. La présence d'une Page dans un portefeuille
Business et le consentement de l'application ne suffisent donc pas à décrire
l'ensemble des accès effectifs de la personne connectée. Cette distinction est
une piste d'investigation, pas une preuve que l'accès par tâches explique à lui
seul la réponse vide.

Vérifications manuelles suivantes, sans élargissement automatique des droits :

1. Vérifier que le profil personnel utilisé pour le consentement est celui auquel
   la Page attendue est effectivement attribuée ; distinguer accès Facebook,
   tâches de contenu et rôle sur le portefeuille Business.
2. Dans les paramètres Business, vérifier l'affectation de cette Page à la
   personne ou au partenaire concerné, et les invitations encore en attente.
   Un rôle sur le Business doit être distingué de l'affectation de ses ressources.
3. Dans la configuration Facebook Login for Business utilisée, vérifier le type
   de jeton prévu et la sélection de ressources. L'hypothèse d'une identité
   utilisateur système/Business différente de l'utilisateur attendu reste à
   vérifier, sans modifier l'échange actuel. Quand l'expiration relative est
   présente, le diagnostic facultatif n'atteste pas le type du jeton ; son statut
   `completed` n'est pas une validation de ce type.
4. Vérifier la cohérence entre l'application, sa configuration de connexion,
   l'intégration Business consentie et ses accès effectifs/restrictions. Si tout
   est cohérent, reproduire puis soumettre à Meta les seuls résultats expurgés,
   l'heure et la version Graph, sans jeton ni identifiant dans les journaux.

Il n'est pas établi qu'ajouter `business_management` résoudrait ce cas ; aucun
scope n'est ajouté. Aucun repli vers des endpoints de ressources Business ou de
Page directe n'est introduit. Les pages développeur Meta Login for Business et
User/accounts n'étaient pas récupérables lors de la vérification documentaire
(limitation HTTP 429) ; les deux sources officielles accessibles ci-dessus
fondent uniquement les distinctions indiquées, pas un diagnostic définitif.

## Validation

Les tests couvrent l'intersection réelle des trois ensembles (y compris des
intersections par paires sans cible commune), les correspondances individuelles,
les grands entiers exacts, les zéros initiaux, les formats invalides, les données
absentes/dupliquées, les erreurs, la configuration facultative, le verrou
Development et l'absence d'identifiants dans les messages et états structurés.
Ils vérifient aussi qu'une réponse vide conserve une connexion sans Page et
qu'aucun appel de repli n'est effectué.
