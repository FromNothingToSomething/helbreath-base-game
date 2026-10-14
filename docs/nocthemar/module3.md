# NOCTHEMAR - module 3 (classes, skills, combat stats)

## Contenu (T37-T55)
| Lot | Taches | Resume |
|---|---|---|
| 1 | T37-T40 | `ModifierSources.Skill`, `SkillIds`, `ClassConfig`, `SkillConfig` (120 skills), `Classes.json`/`Skills.json` (generes par `tools/noc-gen/gen_skills.py`), `ISkillHandler`/`SkillHookTable`, points de competence (`Progression.json` skillPoints) |
| 2 | T41-T45 | `NocState` v2 (classe, skills, kills), schema perso 3 + migration 2->3, `SkillService` (regles d'apprentissage), `SkillEffects`, joueur (classe/skills/respec), protocole (101-103, `NocCatalog`, snapshot), `NocDispatcher` |
| 3 | T46-T50 | critique (`CritChance`), mitigation armure/resistances (`DamageSchool`), `Heal`, regen PV (`HpRegen`), lancement des skills (id >= 1000), handlers livres, hooks OnKill/OnApply |
| 4 | T51-T55 | store/types client, choix de classe + arbre de competences, E2E etendu, cloture |

## Regles
- Classe choisie une fois. Points: 1 par niveau a partir du niveau 2. Deux branches maximum par classe. Respec: debug uniquement.
- Ordre des refus d'apprentissage: classe, skill inconnu, autre classe, indisponible, deja appris, niveau, prerequis, deux branches, points.
- Degats subis: `damage x (1 - resist) / armure` (arrondi, minimum 1). Physique: armure + `ResistPhysical`; magique: `ResistMagic` seule. Plafond de resistance 0.8, armure minimum 0.1.
- Critique: chance de base 5 %, degats x1.5 x `CritDamageMult`.
- Soins x `HealReceivedMult`; regen PV apres 5 s sans degats (`hp.regenPerSecond`, 0 par defaut = aucune).
- Cast d'un skill actif: id de skill passe par `SpellCastStartRequest`/`SpellCastRequest`; cout mana x `ManaCostMult`, recharge `cooldownMs`; refus = `NocActionRefused` + `SpellCastFailed`.

## Skills avec comportement (3 sur 120)
| Id | Handler | Effet |
|---|---|---|
| 1001 Bouclier d'Os | `buff_bone_shield` | +30 % resistance physique et magique, 8 s |
| 1301 Benediction Noire | `heal_black_blessing` | soigne 250 PV |
| 1026 Heritage Abyssal | `passive_abyssal_heritage` | +1 % armure par monstre tue, max 20 % |

Les 11 passifs a modificateurs simples restent data-only (voir `READY` dans le generateur). Les autres skills sont au catalogue en `planned`.

## Limites connues
- Les buffs temporises ne sont pas persistes (perdus a la deconnexion).
- `OnKill` ne couvre que les monstres; `OnHitDealt`, `OnDamageTaken`, `OnTick` ne sont pas encore branches.
- Un cast de skill ignore la cible (buffs/soins sur soi uniquement).
- Les sorts de zone au sol (`Casting.cs` snapshot) utilisent encore `ComputeAttackDamage` sans critique.
- Stats sans consommateur: voir les commentaires `// consumer: module N` dans `StatPipeline.cs`.

## Validation
CI uniquement (build + tests serveur + tests client + `proto-baseline`). Le job nocturne `noc-e2e` verifie classe/points/skills a travers la reconnexion et le redemarrage.
