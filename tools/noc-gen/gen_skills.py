#!/usr/bin/env python3
"""Generates multiplayer/server/Config/Nocthemar/{Classes,Skills}.json from the design document (CONTEXTE GENERAL).

Usage: python3 gen_skills.py [output_dir]   (default: multiplayer/server/Config/Nocthemar)
The JSON files are the source of truth once committed; edit this script and regenerate only to change the whole grid.
A passive is "ready" when it maps to an existing StatId (see READY); everything else is "planned" (catalog entry, not learnable yet).
"""
import json, sys, unicodedata, re, pathlib

LEVELS = [1, 3, 6, 10, 14, 18, 24, 30]
KINDS = ["passive", "active", "passive", "active", "passive", "active", "passive", "ultimate"]

# class: (key, name, role, [(branch key, branch name, branch role, [(skill name, description) x 8])])
C = [
 ("chevalier_osseux", "Chevalier d'Osseux", "Tank / DPS mêlée", [
  ("rempart", "Rempart", "Tank pur", [
   ("Posture de Fer", "+15% défense, -10% vitesse"), ("Bouclier d'Os", "Mur d'ossements absorbe 30% des dégâts pendant 8s"),
   ("Peau de Granit", "Réduit les dégâts critiques de 25%"), ("Provocation Spectrale", "Force les ennemis à attaquer le Chevalier pendant 5s"),
   ("Armure de Moelle", "20% des dégâts subis convertis en bouclier"), ("Forteresse de Chair", "Immobilise mais immunise aux projectiles 6s"),
   ("Écho de Douleur", "Les ennemis qui frappent subissent 10% de leurs dégâts"), ("Mur de Crânes", "Barrière d'ossements bloque une zone 12s")]),
  ("briseur", "Briseur", "DPS mêlée", [
   ("Frappe Vertébrale", "+10% dégâts mêlée"), ("Coup de Masse", "Attaque lourde brise l'armure ennemie"),
   ("Rage Osseuse", "+5% dégâts par ennemi proche"), ("Charge Squelettique", "Fonce vers un ennemi, étourdit 2s"),
   ("Déchirement", "Les critiques infligent saignement"), ("Tourbillon d'Os", "Attaque circulaire projette les ennemis"),
   ("Fureur Cadavérique", "+20% vitesse d'attaque sous 30% PV"), ("Exécution Macabre", "300% dégâts si ennemi sous 40% PV")]),
  ("memoire", "Mémoire", "Hybride / Mimétisme", [
   ("Souvenir Ancestral", "+5% résistance magique"), ("Rappel de Carapace", "Gagne la défense d'un golem 10s"),
   ("Réminiscence", "Mimétisme dure 15% plus longtemps"), ("Écho du Troll", "Régénération accélérée 8s"),
   ("Mémoire du Dragon", "+15% résistance au feu"), ("Voix des Morts", "Invoque 2 spectres combattants 15s"),
   ("Héritage Abyssal", "+1% défense permanente par monstre tué (max 20%)"), ("Armée des Oubliés", "Invoque 5 squelettes guerriers 30s")]),
 ]),
 ("chantre_fer", "Chantre de Fer", "Support / DPS distance", [
  ("distorsion", "Distorsion", "DPS distance", [
   ("Note Acide", "+10% dégâts magiques"), ("Riff Foudroyant", "Éclair frappe 3 ennemis"),
   ("Accord Brisé", "Les sorts réduisent la résistance magique"), ("Solo Infernal", "Canalise 3s, dégâts croissants"),
   ("Harmonique Noire", "+15% dégâts contre les monstres"), ("Cri de la Banshee", "Scream de zone inflige peur"),
   ("Réverbération", "20% des dégâts répétés 2s plus tard"), ("Requiem de Feu", "Solo déclenche tempête de flammes")]),
  ("harmonie", "Harmonie", "Support / Soin", [
   ("Mélodie Apaisante", "+10% soins"), ("Berceuse des Abysses", "Endort ennemis, régénère alliés"),
   ("Chœur Spectral", "Soins touchent un allié supplémentaire"), ("Hymne de Guerre", "+20% dégâts alliés 10s"),
   ("Contrepoint", "Soins critiques rendent un bouclier"), ("Rappel", "Réanime un allié avec 30% PV"),
   ("Symphonie de Fer", "+10% toutes stats pour le groupe"), ("Opéra des Damnés", "Soin massif + immunité 5s groupe")]),
  ("sample", "Sample", "Mimétisme sonore", [
   ("Oreille Absolue", "+10% portée des sorts"), ("Écho Monstrueux", "Reproduit un cri de monstre capturé"),
   ("Bibliothèque Sonore", "+1 slot de sample"), ("Rugissement du Dragon", "Reproduit le souffle d'un dragon"),
   ("Mémoire Auditive", "Samples durent 20% plus longtemps"), ("Chant de la Sirène", "Charm les ennemis 4s"),
   ("Résonance Abyssale", "Samples infligent +15% dégâts"), ("Concert des Abysses", "Joue tous les samples enregistrés")]),
 ]),
 ("metamorphe", "Métamorphe", "Assassin / DPS furtif", [
  ("predateur", "Prédateur", "Assassin", [
   ("Griffes Naissantes", "+10% dégâts critiques"), ("Frappe Furtive", "Attaque depuis l'ombre, +50% dégâts"),
   ("Camouflage Charnel", "Invisibilité accrue hors combat"), ("Venin Abyssal", "Poison se propage"),
   ("Instinct de Chasse", "Détecte ennemis à faible PV"), ("Dévoration", "Exécute un ennemi et vole une capacité"),
   ("Frénésie", "+30% vitesse après exécution"), ("Ombre Mortelle", "Invisible + 500% dégâts prochain coup")]),
  ("mutation", "Mutation", "DPS / Tank hybride", [
   ("Peau Écailleuse", "+10% armure"), ("Mue de Chair", "Transformation partielle 15s"),
   ("Régénération Troll", "+2% PV/s hors combat"), ("Ailes de Nuit", "Vol temporaire, esquive +30%"),
   ("Carapace de Golem", "+20% résistance physique"), ("Forme Cauchemardesque", "Terreur de zone, paralysie 3s"),
   ("Cœur de Monstre", "+15% PV max"), ("Apothéose Abyssale", "Transformation complète 20s")]),
  ("adaptation", "Adaptation", "Mimétisme évolutif", [
   ("Assimilation", "+5% absorption de capacités"), ("Glande Mimétique", "Copie une capacité ennemie 10s"),
   ("Mémoire Cellulaire", "+1 capacité mimétique permanente"), ("Adaptation Rapide", "Gagne résistance au type de dégât subi"),
   ("Évolution", "Capacités mimétiques +10% puissance"), ("Vol de Forme", "Prend l'apparence d'un monstre"),
   ("Symbiose", "Capacités mimétiques -20% coût"), ("Chimère", "Fusionne 3 capacités monstres")]),
 ]),
 ("necrophante", "Nécrophante", "Soigneur / Invocateur", [
  ("benediction", "Bénédiction", "Soin pur", [
   ("Prière Sombre", "+10% soins"), ("Bénédiction Noire", "Soigne en absorbant la vie ennemie"),
   ("Lien Vital", "Partage 10% dégâts avec un allié"), ("Requiem", "Chant soigne groupe et blesse ennemis"),
   ("Âme Gardienne", "Spectre protège l'allié le plus faible"), ("Résurrection", "Réanime un allié avec 50% PV"),
   ("Sanctuaire d'Os", "Zone de soin autour du Nécrophante"), ("Jugement Dernier", "Soin massif + purge des malus")]),
  ("invocation", "Invocation", "Armée des morts", [
   ("Appel du Serviteur", "+1 serviteur max"), ("Lever de Bouclier", "Invoque un spectre tank"),
   ("Légion", "+10% PV des invocations"), ("Armée Spectrale", "Invoque 3 âmes de monstres"),
   ("Nécro-Maîtrise", "Invocations +15% dégâts"), ("Général d'Outre-Tombe", "Invoque un boss monstre allié 30s"),
   ("Sacrifice", "Sacrifie invocation pour soigner"), ("Roi des Morts", "Invoque 10 spectres")]),
  ("malediction", "Malédiction", "Débuff / DPS", [
   ("Chuchotement", "+10% dégâts de malédiction"), ("Marque de Mort", "Réduit les soins reçus par l'ennemi"),
   ("Peste", "Malédictions se propagent"), ("Lien d'Outre-Tombe", "Partage dégâts allié/ennemi"),
   ("Désespoir", "-15% résistance ennemie"), ("Condamnation", "Inflige dégâts sur la durée"),
   ("Vol d'Âme", "Récupère ressources en tuant"), ("Damnation Éternelle", "Tue instantanément sous 20% PV")]),
 ]),
 ("forgeur_abysses", "Forgeur d'Abysses", "Mage / DPS distance", [
  ("fusion", "Fusion", "DPS magique", [
   ("Cœur Brûlant", "+10% dégâts de feu"), ("Explosion de Noyau", "Orbe d'essence explose"),
   ("Chaleur Industrielle", "+10% pénétration magique"), ("Feu de Forge", "Torrent de flammes noires"),
   ("Magma", "Sorts laissent traînée de feu"), ("Chaînes Abyssales", "Enchaîne et attire les ennemis"),
   ("Noyau Instable", "+20% dégâts critiques magiques"), ("Supernova Abyssale", "Explosion massive de zone")]),
  ("forge", "Forge", "Buff / Armure", [
   ("Peau de Métal", "+10% armure"), ("Armure de Magma", "Brûle les attaquants 10s"),
   ("Trempe", "+15% résistance critiques"), ("Bouclier Runique", "Absorbe dégâts magiques"),
   ("Alliage Vivant", "+10% toutes résistances"), ("Forge de Guerre", "Buff +25% dégâts pour un allié"),
   ("Indestructible", "Immunité aux étourdissements"), ("Forteresse Noire", "Bouclier massif pour le groupe")]),
  ("extraction", "Extraction", "Mimétisme matériel", [
   ("Œil de Forge", "Voir les faiblesses des monstres"), ("Extraction de Noyau", "Vole une capacité monstre"),
   ("Réserve d'Essence", "+2 slots de noyaux"), ("Souffle du Dragon", "Lance le souffle d'un dragon capturé"),
   ("Fonte Rapide", "Noyaux rechargent 20% plus vite"), ("Tempête de Golem", "Invoque des éclairs de golem"),
   ("Maître Forgeron", "Noyaux +15% puissance"), ("Arme Ultime", "Forge une arme légendaire à partir de 3 noyaux")]),
 ]),
]

A, F, M = "PercentAdd", "Flat", "PercentMult"
def mod(stat, kind, value): return {"stat": stat, "kind": kind, "value": value}
# (class, branch, slot) -> modifiers of the passives that map to an existing StatId.
READY = {
 (0, 0, 0): [mod("ArmorMult", A, 0.15), mod("MoveSpeedMult", A, -0.10)],
 (0, 1, 0): [mod("DamageMult", A, 0.10)],
 (0, 2, 0): [mod("ResistMagic", F, 0.05)],
 (0, 2, 2): [mod("MimicDurationMult", A, 0.15)],
 (1, 0, 0): [mod("MagicDamageMult", A, 0.10)],
 (2, 0, 0): [mod("CritDamageMult", A, 0.10)],
 (2, 1, 0): [mod("ArmorMult", A, 0.10)],
 (2, 1, 4): [mod("ResistPhysical", F, 0.20)],
 (2, 1, 6): [mod("MaxHp", A, 0.15)],
 (4, 1, 0): [mod("ArmorMult", A, 0.10)],
 (4, 1, 4): [mod(r, F, 0.10) for r in ("ResistPhysical", "ResistMagic", "ResistCurse", "ResistDeath")],
}

def slug(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode().lower()
    return re.sub(r"[^a-z0-9]+", "_", s).strip("_")

def main(out):
    classes, skills, keys = [], [], set()
    for ci, (ckey, cname, crole, branches) in enumerate(C):
        classes.append({"id": ci, "key": ckey, "name": cname, "role": crole,
                        "branches": [{"id": bi, "key": b[0], "name": b[1], "role": b[2]} for bi, b in enumerate(branches)]})
        for bi, (_, _, _, rows) in enumerate(branches):
            assert len(rows) == 8
            for si, (name, desc) in enumerate(rows):
                key = slug(name)
                assert key not in keys, key
                keys.add(key)
                mods = READY.get((ci, bi, si))
                s = {"id": 1000 + ci * 100 + bi * 10 + si, "key": key, "name": name, "class": ci, "branch": bi, "slot": si,
                     "level": LEVELS[si], "kind": KINDS[si], "cost": 2 if si == 7 else 1, "desc": desc,
                     "status": "ready" if mods else "planned"}
                if mods:
                    s["modifiers"] = mods
                skills.append(s)
    assert len(skills) == 120 and set(READY) <= {(s["class"], s["branch"], s["slot"]) for s in skills}
    out = pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "Classes.json").write_text(json.dumps({"schemaVersion": 1, "classes": classes}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out / "Skills.json").write_text(json.dumps({"schemaVersion": 1, "skills": skills}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[2] / "multiplayer/server/Config/Nocthemar")
