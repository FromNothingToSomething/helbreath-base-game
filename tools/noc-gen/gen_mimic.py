#!/usr/bin/env python3
"""Generates multiplayer/server/Config/Nocthemar/Mimic.json (22 monster abilities + 5 fusions) and appends the 22 essence items (ids 5000-5021) to Items.json.

Usage: python3 gen_mimic.py [server_dir]   (default: multiplayer/server)
Mimic.json is overwritten; Items.json is only appended to (essences already present are skipped), by text so the existing formatting is untouched.
An ability is "ready" when it has a handler (Skills/MimicHandlers.cs) or reuses a legacy Spells.json spell (spellId); everything else is "planned" (catalog entry, not castable).
Rarity, class exclusivity, drop chance, cost and cooldown below are defaults to be tuned in the committed JSON.
"""
import json, pathlib, re, sys

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "multiplayer/server")

# classes (Classes.json): 0 chevalier_osseux, 1 chantre_fer, 2 metamorphe, 3 necrophante, 4 forgeur_abysses
DROP = {"common": 0.20, "rare": 0.10, "epic": 0.04, "legendary": 0.01, "mythic": 0.005}
COOLDOWN = {"common": 30000, "rare": 40000, "epic": 60000, "legendary": 90000, "mythic": 180000}
MANA = {"common": 30, "rare": 50, "epic": 80, "legendary": 120, "mythic": 200}

# key -> ("handler", handler key) or ("spell", Spells.json id): the abilities that can be cast. Fusion results are all planned.
READY = {
 "peau_de_fer": ("handler", "buff_iron_skin"), "bouclier_noir": ("handler", "buff_black_shield"),
 "traversee": ("spell", 24), "frenesie": ("spell", 25), "brasier": ("spell", 8), "toile": ("spell", 3), "racines": ("spell", 7),
 "blizzard": ("spell", 21), "souffle_glacial": ("spell", 10), "pluie_de_feu": ("spell", 19), "eruption": ("spell", 12),
 "lame_celeste": ("spell", 18), "cri_mortel": ("spell", 16), "dard": ("spell", 4), "trois_tetes": ("spell", 5), "malediction": ("spell", 15),
}

# (key, name, kind, rarity, monster id, Monsters.json name, class ids, description) in doc order = ability id 2000 + index
A = [
 ("nuee", "Nuée", "offensive", "common", 1, "Slime", [3], "Invoque 3 rats"),
 ("morsure_infectee", "Morsure infectée", "offensive", "common", 51, "Skeleton", [], "Poison réduit soins"),
 ("traversee", "Traversée", "utility", "common", 19, "Detector", [2, 3], "Intangible 3s"),
 ("peau_de_fer", "Peau de fer", "defensive", "rare", 28, "Stone Golem", [0, 4], "+30% armure 10s"),
 ("brasier", "Brasier", "offensive", "rare", 43, "Fire Wyvern", [4], "Aura de feu 8s"),
 ("frenesie", "Frénésie", "utility", "rare", 60, "Werewolf", [], "+30% vitesse attaque 10s"),
 ("toile", "Toile", "control", "common", 54, "Tentocle", [], "Immobilise 3s"),
 ("racines", "Racines", "control", "rare", 9, "Cannibal Plant", [], "Enracine ennemis"),
 ("putrefaction", "Putréfaction", "defensive", "common", 61, "Zombie", [], "Explose en mourant"),
 ("malediction", "Malédiction", "offensive", "rare", 52, "Sorceress", [], "-20% stats ennemies"),
 ("trois_tetes", "Trois têtes", "offensive", "epic", 32, "Hellhound", [], "Attaque 3 fois"),
 ("souffle_glacial", "Souffle glacial", "offensive", "rare", 20, "Frost", [], "Gèle ennemis"),
 ("blizzard", "Blizzard", "offensive", "epic", 34, "Ice Golem", [], "Tempête de glace"),
 ("cri_mortel", "Cri mortel", "offensive", "epic", 39, "Nizie", [1], "Dégâts zone + peur"),
 ("lame_celeste", "Lame céleste", "offensive", "epic", 22, "God's Hand Knight", [], "Dégâts sacrés"),
 ("bouclier_noir", "Bouclier noir", "defensive", "rare", 17, "Dark Shadow Knight", [0], "Absorbe 50% dégâts"),
 ("pluie_de_feu", "Pluie de feu", "offensive", "epic", 18, "Demon", [], "Météore"),
 ("dard", "Dard", "offensive", "common", 50, "Scorpion", [], "Poison + paralysie"),
 ("eruption", "Éruption", "offensive", "rare", 33, "Hellclaw", [4], "Explosion de lave"),
 ("folie", "Folie", "control", "legendary", 7, "Beholder", [], "Confusion de zone"),
 ("jugement", "Jugement", "ultimate", "mythic", 46, "Lich", [], "Dégâts massifs + silence"),
 ("chant_de_la_fin", "Chant de la fin", "ultimate", "mythic", 62, "Abaddon (incomplete)", [], "Instant kill sous 10% PV"),
]
# (ability a, ability b, key, name, kind, class ids, description); result id 2100 + index
F = [
 (4, 3, "drake_de_fer", "Drake de Fer", "offensive", [], "Souffle + armure"),
 (13, 2, "reine_spectrale", "Reine Spectrale", "offensive", [], "Cri + invocations"),
 (5, 8, "bete_immortelle", "Bête Immortelle", "utility", [], "Frénésie + régénération"),
 (14, 16, "dualite", "Dualité", "offensive", [], "Alterne sacré/démoniaque"),
 (10, 17, "chimere_venimeuse", "Chimère Venimeuse", "offensive", [], "3 attaques + poison"),
]

def ability(i, key, name, kind, rarity, mid, mname, classes, desc):
    a = {"id": 2000 + i, "key": key, "name": name, "kind": kind, "rarity": rarity, "monsterId": mid, "monsterName": mname,
         "classIds": classes, "requiresRaid": rarity == "mythic", "dropChance": DROP[rarity], "cooldownMs": COOLDOWN[rarity],
         "manaCost": MANA[rarity], "desc": desc, "status": "planned"}
    ready = READY.get(key)
    if ready:
        a["status"] = "ready"
        a["handler" if ready[0] == "handler" else "spellId"] = ready[1]
    return a

abilities = [ability(i, *a) for i, a in enumerate(A)]
rank = ["common", "rare", "epic", "legendary", "mythic"]
fusions = []
for i, (a, b, key, name, kind, classes, desc) in enumerate(F):
    r = rank[min(4, max(rank.index(abilities[a]["rarity"]), rank.index(abilities[b]["rarity"])) + 1)]
    r = "legendary" if r == "mythic" else r
    abilities.append({"id": 2100 + i, "key": key, "name": name, "kind": kind, "rarity": r, "classIds": classes, "requiresRaid": False,
                      "dropChance": 0, "cooldownMs": COOLDOWN[r], "manaCost": MANA[r], "desc": desc, "status": "planned"})
    fusions.append({"a": 2000 + a, "b": 2000 + b, "result": 2100 + i})

doc = {"schemaVersion": 1, "slots": {"first": 1, "levelsPerSlot": 10, "max": 9},
       "xp": {"maxLevel": 10, "perCast": 1, "thresholds": [10, 30, 60, 100, 150, 210, 280, 360, 450], "powerPerLevel": 0.05},
       "essenceVialDropChance": 0.05,
       "abilities": abilities, "fusions": fusions}
(root / "Config/Nocthemar/Mimic.json").write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

items_path = root / "Config/Items.json"
text = items_path.read_text(encoding="utf-8")
have = {int(m) for m in re.findall(r'"id":\s*(\d+)', text)}
new = [{"id": 5000 + i, "name": f"Essence: {a['name']}", "itemType": "misc", "stackable": True} for i, a in enumerate(abilities[:len(A)]) if 5000 + i not in have]
if 5100 not in have:
    new.append({"id": 5100, "name": "Fiole d'Essence", "itemType": "misc", "stackable": True})
if new:
    body = text.rstrip()
    assert body.endswith("}\n]"), "unexpected Items.json ending"
    add = ",\n".join("  " + json.dumps(n, ensure_ascii=False, indent=2).replace("\n", "\n  ") for n in new)
    items_path.write_text(body[:-2] + ",\n" + add + "\n]", encoding="utf-8")
print(f"abilities {len(abilities)}, fusions {len(fusions)}, essences added {len(new)}")
