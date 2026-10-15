# NOCTHEMAR - module 5 (factions, reputation, quetes)

## Contenu (T73-T88)
| Lot | Taches | Resume |
|---|---|---|
| 1 | T73-T76 | `FactionConfig`/`Factions.json` (4 factions), `ReputationService`, `StatId.ReputationGainMult` (22), `NocState` v4 + schema 5 + migration, `Monsters.json factionId/repOnKill` + crochet kill |
| 2 | T77-T80 | `FactionService` (rejoindre, perks par palier), proto factions (108, 112, `PlayerFactionChanged`, `PlayerEnteredRange.faction_id`), PNJ Maitre de faction 11-14, `pvpMode` |
| 3 | T81-T84 | `Quests.json` (12 quetes) + `gen_quests.py`, `QuestService` (rendu atomique, `InventoryManager.CanHold`), crochet kill, proto quetes (109-111, snapshot 15-16, catalogue 12-13) |
| 4 | T85-T88 | vues/store/theme client + tests node, dialogues Maitre de faction / journal / barre de reputation, nom + faction au-dessus du sprite, E2E, cloture |

## Regles
- Reputation -10000..10000; paliers Hostile < -1000 <= Neutre < 1000 <= Amical < 3000 <= Honore < 6000 <= Exalte (valeur egale au seuil = palier superieur).
- Factions: 0 chant_de_fer, 1 choeur_abyssal, 2 meutes_libres, 3 forgeurs; rivales 0-1 et 2-3. Affiliation unique, niveau 10, via le Maitre de sa faction (3 cases, vivant).
- Sans faction: aucune reputation gagnee. Kill: la faction du monstre perd, ses rivales gagnent; seul le killer est credite.
- Quetes: max 5 actives, non repetables, reservees aux membres, palier minimum, cibles `kill` = monstres de la faction rivale, `deliver` = contenu du sac, rendu atomique (sac plein = refus).
- Refus accepter: mort, inconnue, pas de maitre, sans faction, autre faction, deja faite, deja active, palier, limite. Rendre: mort, inconnue, pas de maitre, non active, objectif non atteint, sac plein. Abandon: partout.
- `pvpMode`: `legacy` (defaut) ou `factions` (meme faction = attaque refusee).

## Protocole
| Message | Numero |
|---|---|
| ClientMessage `NocFactionJoinRequest` / `NocQuestAcceptRequest` / `NocQuestAbandonRequest` / `NocQuestTurnInRequest` | 108 / 109 / 110 / 111 |
| ClientMessage `NocReputationAddRequest` (debug) | 112 |
| ServerMessage `PlayerFactionChanged` | 102 |
| `PlayerEnteredRange.faction_id` | 20 |
| `NocSnapshot` faction_id / reputations / quests / quests_done | 13 / 14 / 15 / 16 |
| `NocCatalog` factions / reputation_rules / quests / quest_max_active | 10 / 11 / 12 / 13 |

## Client
- `FactionView`/`QuestView` (purs, testes en node: paliers, barre, previsions de refus), `factionTheme.ts` (argent #c0c4cc, violet #7b4fbf, vert sombre #3f6b3a, or terni #b8923a), `Faction.store`.
- `FactionDialog` (rejoindre ou quetes du Maitre), `QuestLogDialog` (progression, abandon), barre de reputation et boutons dans le HUD NOC.
- Tag au-dessus du sprite (`PlayerTag`): nom 11 px contour noir, icone 16x16 a gauche, couleur de faction; 2 options (Player names, Player factions) dans Controls, persistees en localStorage (`PlayerTagState`).

## E2E (`noc-e2e`)
- Apres la mimetique: niveau 10, reputation debug (Amical), join refuse sans maitre, maitre invoque (PNJ 11 + faction), join, second join refuse, quete `deliver` acceptee, items debug, rendu, re-accept refuse, quete `kill` laissee active; faction, reputations, quetes actives et terminees comparees apres reconnexion et redemarrage serveur.

## Limites connues
- Le kill de monstre (progression `kill`, gain de reputation) n'est pas couvert par l'E2E (tests serveur seulement).
- Pas de quetes repetables, de chaines, de dialogues PNJ ni de partage de groupe.
- Defection: debug uniquement; payante en module 6.5. Prix par palier: consommes en 6.5.
- Guildes, titres, rangs et guerres de territoires: module 8.
- Les cibles `kill` s'affichent par id de monstre (le catalogue n'envoie pas les noms de monstres).
- Maitres de faction: sprites (gail, mcgaffin, perry, kennedy) et positions (50..53,33) provisoires; ouverture des dialogues par boutons du HUD (pas de clic sur le PNJ), le serveur verifie la portee.
- Tag: position fixe a 2 cases au-dessus des pieds, non ajustee a la taille du sprite; rendu non verifie visuellement (CI sans navigateur).
- `pvpMode=factions` non active par defaut.

## Validation
CI uniquement (build + tests serveur + client-logic + `proto-baseline` + proto round-trip) et job `noc-e2e`.
