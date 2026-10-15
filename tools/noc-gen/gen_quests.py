#!/usr/bin/env python3
"""Generates multiplayer/server/Config/Nocthemar/Quests.json: 12 quests, 3 per faction (Neutral, Friendly, Honored).

Usage: python3 gen_quests.py [server_dir]   (default: multiplayer/server)
Quests.json is overwritten. Kill targets are monsters of the faction's rival (killing them is what earns the quest giver's reputation anyway);
deliver quests use items that exist in Items.json (35 Gold, 36 Red Potion, 5100 Fiole d'Essence). Rewards, counts and tiers are defaults to be tuned in the committed JSON.
Factions (Factions.json): 0 chant_de_fer, 1 choeur_abyssal, 2 meutes_libres, 3 forgeurs.
Monsters (Monsters.json): 51 Skeleton, 61 Zombie, 52 Sorceress (choeur); 31 Guard, 56 Temple Knight, 22 God's Hand Knight (chant de fer);
8 Battle Golem, 12 Clay Golem, 28 Stone Golem (forgeurs); 60 Werewolf, 27 Giant Tree, 9 Cannibal Plant (meutes).
"""
import json, pathlib, re, sys

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "multiplayer/server")

GOLD, POTION, VIAL = 35, 36, 5100

def kill(monster, count): return {"kind": "kill", "monsterId": monster, "count": count}
def deliver(item, count): return {"kind": "deliver", "itemId": item, "count": count}
def rewards(xp, rep, *items): return {"xp": xp, "rep": rep, "items": [{"itemId": i, "qty": q} for i, q in items]}

# (faction id, key, name, min tier, objective, rewards, desc) in file order = quest id
Q = [
 (0, "chant_squelettes", "Faire taire les os", "Neutral", kill(51, 10), rewards(200, 100, (POTION, 5)), "Dix squelettes du Chœur Abyssal hantent les routes."),
 (0, "chant_fioles", "Fioles pour la forge", "Friendly", deliver(VIAL, 3), rewards(400, 150, (GOLD, 50)), "Rapportez trois Fioles d'Essence au Maître."),
 (0, "chant_sorcieres", "La fin des sorcières", "Honored", kill(52, 5), rewards(800, 250, (GOLD, 100), (POTION, 10)), "Cinq sorcières du Chœur doivent tomber."),
 (1, "choeur_gardes", "Briser la garde", "Neutral", kill(31, 10), rewards(200, 100, (POTION, 5)), "Dix gardes de l'Ordre du Chant de Fer barrent la voie."),
 (1, "choeur_potions", "Soins pour les fidèles", "Friendly", deliver(POTION, 10), rewards(400, 150, (GOLD, 50)), "Dix Potions Rouges pour les blessés du Chœur."),
 (1, "choeur_templiers", "Chevaliers du temple", "Honored", kill(56, 5), rewards(800, 250, (GOLD, 100), (VIAL, 2)), "Cinq chevaliers du temple à abattre."),
 (2, "meutes_golems", "Briser l'acier", "Neutral", kill(8, 10), rewards(200, 100, (POTION, 5)), "Dix golems de bataille des Forgeurs."),
 (2, "meutes_potions", "Remèdes pour la meute", "Friendly", deliver(POTION, 10), rewards(400, 150, (GOLD, 50)), "Dix Potions Rouges pour la meute."),
 (2, "meutes_pierre", "Cœurs de pierre", "Honored", kill(28, 5), rewards(800, 250, (GOLD, 100), (VIAL, 2)), "Cinq golems de pierre des Forgeurs."),
 (3, "forgeurs_loups", "Chasse aux loups", "Neutral", kill(60, 10), rewards(200, 100, (POTION, 5)), "Dix loups-garous des Meutes Libres."),
 (3, "forgeurs_fioles", "Essence pour l'enclume", "Friendly", deliver(VIAL, 5), rewards(400, 150, (GOLD, 50)), "Cinq Fioles d'Essence pour la forge."),
 (3, "forgeurs_arbres", "Abattre les géants", "Honored", kill(27, 5), rewards(800, 250, (GOLD, 100), (POTION, 10)), "Cinq arbres géants des Meutes."),
]

quests = []
for i, (faction, key, name, tier, obj, rw, desc) in enumerate(Q):
    q = {"id": i, "key": key, "name": name, "factionId": faction, "minTier": tier}
    q.update(obj)
    q["rewards"] = rw
    q["desc"] = desc
    quests.append(q)

# Keys are identifiers (server regex ^[a-z][a-z0-9_]{1,31}$): fail here, not at server start, if one gets an accent or a space.
KEY = re.compile(r"^[a-z][a-z0-9_]{1,31}$")
bad = [q["key"] for q in quests if not KEY.match(q["key"])]
assert not bad, f"invalid quest keys (ASCII lowercase, digits, underscore): {bad}"
assert len({q["key"] for q in quests}) == len(quests), "duplicate quest keys"

doc = {"schemaVersion": 1, "rules": {"maxActive": 5, "bagSlots": 40}, "quests": quests}
(root / "Config/Nocthemar/Quests.json").write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"quests {len(quests)}")
