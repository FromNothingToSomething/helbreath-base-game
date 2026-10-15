# NOCTHEMAR - module 4 (mimetisme, Forge d'Ames)

## Contenu (T56-T72)
| Lot | Taches | Resume |
|---|---|---|
| 1 | T56-T60 | `StatId.EssenceCapture` (21), `MimicConfig`/`Mimic.json` (16 capacites pretes, 6 monstres + 5 fusions prevus), capture/essences, `NocState` v3 (Mimics, MimicEquipped), schema perso 4 |
| 2 | T61-T66 | routage de cast `CastRouting.Of` (sorts < 1000, skills 1000-1999, mimetisme >= 2000), handlers `buff_iron_skin`/`buff_black_shield`, `EssenceDropper`, regles consommer/fusionner/equiper |
| 3 | T62bis, T67, T68 | Fiole d'Essence (drop + `CreateItem` debug), protocole (snapshot/catalogue/requetes 105-107), `SoulForge` + `NocDispatcher.HandleForge`, PNJ 10 |
| 4 | T69-T72 | store/types client + tests node, dialogue Forge d'Ames + Barre de Mimetisme (violette), E2E, cloture |

## Regles
- Ids: sorts < 1000, skills 1000-1999, capacites de monstres 2000-2099, fusions 2100-2199; essence = item 5000+(id-2000); Fiole d'Essence = item 5100.
- Slots: `min(max, first + (niveau-1)/levelsPerSlot)` (Mimic.json: 1, 10, 9). Seuils d'XP 10,30,60,100,150,210,280,360,450; puissance +5 %/niveau; recharges >= 30 s.
- Consommer/fusionner: Forge d'Ames a 3 cases (Chebyshev) et joueur vivant. Equiper/desequiper: partout.
- Refus: `NocActionRefused` + `SpellCastFailed`; refus debug journalises une fois par (joueur, requete).

## Protocole
| Message | Numero |
|---|---|
| ClientMessage `NocForgeConsumeRequest` / `NocForgeFuseRequest` / `NocMimicEquipRequest` | 105 / 106 / 107 |
| ClientMessage `NocCreateItemRequest` (debug, qte 1..100) | 104 |
| `NocSnapshot` mimics / mimic_equipped / mimic_slots | 10 / 11 / 12 |
| `NocCatalog` mimics, fusions, xp thresholds, power/level, slots first/levels/max | 3, 4, 5, 6, 7, 8, 9 |

## Client
- `NocCatalogView`/`MimicView` (purs, testes en node), `Mimic.store`, `MimicBarDialog` (barre violette: clic = lancer, clic droit = retirer), `SoulForgeDialog` (onglets Essences / Capacites / Regles), bouton "Mimicry" du HUD.

## E2E (`noc-e2e`)
- Essences creees par la commande debug, Forge invoquee (`summonNpcRequest` PNJ 10), consommer, fusionner 2005+2008 -> 2102, equiper (second equip refuse), cast de 2003 (classes 0 et 4), puis comparaison apres reconnexion et redemarrage serveur.

## Limites connues
- La capture (kill + ramassage) n'est pas couverte par l'E2E (tests serveur uniquement).
- Capacites a base de sort: pas de mise a l'echelle de puissance; 6 monstres et 5 fusions restent "planned".
- Fiole d'Essence: drop/debug seulement jusqu'au module 6.5 (marchand).
- Essences sans sprite; PNJ Forge: position (49,33) et sprite (`gandlf`) provisoires, a valider en jeu.
- Pas de raccourcis clavier pour la barre.

## Validation
CI uniquement (build + tests serveur + client-logic + `proto-baseline`) et job `noc-e2e`.
