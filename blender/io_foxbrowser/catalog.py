# SPDX-License-Identifier: GPL-3.0-or-later
"""
Find the model you need without leaving Blender.

Points at a folder of FoxBrowser rips, scans the model FBXs in it, and gives each
one a plain-language description -- so "arm female", "quiet", "respirator", "hat"
in the search box turns a folder of terse filenames into something browsable. The
panel that drives this (:mod:`ui`) then imports the chosen model, or adds it onto
the active character rig.

The description logic is the same one the tool's ``catalog.html`` uses, ported to
Python so the add-on needs no data file: a model's name and the rip folder it sits
in are enough to name it.
"""

import os
import re

from . import discovery

# Character codes we can name with confidence.
CHARS = {
    "sna": "Snake (Venom / Punished Snake)", "sna2": "Snake — Ground Zeroes",
    "skl": "Player base skeleton", "qui": "Quiet", "kaz": "Kazuhira Miller",
    "hue": "Huey Emmerich", "hyu": "Huey Emmerich", "paz": "Paz Ortega",
    "dds": "Diamond Dogs soldier", "ddg": "Diamond Dogs soldier",
    "olm": "Ocelot", "ocl": "Ocelot", "dlf": "DLC female character",
    "dlg": "DLC gear character", "dlh": "DLC character",
    "avm": "MGO avatar", "avf": "MGO avatar (female)", "avr": "MGO avatar stage",
    "wss": "Soviet soldier (GZ)", "uss": "US soldier (GZ)", "rai": "Raiden",
    "chi": "Chico", "rvn": "Raven", "psy": "Psycho Mantis", "dar": "Skull Face",
    "prs": "Prisoner", "nrs": "Medical staff", "hrs": "Horse",
    "bss": "Boss enemy", "kij": "Kaiju / creature", "zmb": "Wanderer (zombie)",
    "mbs": "Mother Base staff", "bsm": "Player base — male", "bsf": "Player base — female",
    "emm": "Base male", "emf": "Base female", "dmc": "Story character",
    "gnt": "Giant enemy", "eng": "Engineer NPC",
}

# Folder segment -> (category, description). Segment is the rip subfolder or a
# recognised part prefix.
FOLDERS = {
    "hats": ("Headgear", "Hats, helmets and headwear"),
    "glasses": ("Eyewear", "Glasses and goggles"),
    "inf_chest": ("Chest — Infiltrator", "Infiltrator chest gear"),
    "rec_chest": ("Chest — Enforcer", "Enforcer chest gear"),
    "tec_chest": ("Chest — Scout", "Scout chest gear"),
    "inf_head": ("Head — Infiltrator", "Infiltrator head"),
    "rec_head": ("Head — Enforcer", "Enforcer head"),
    "tec_head": ("Head — Scout", "Scout head"),
    "inf_suit": ("Suit — Infiltrator", "Infiltrator bodysuit"),
    "rec_suit": ("Suit — Enforcer", "Enforcer bodysuit"),
    "tec_suit": ("Suit — Scout", "Scout bodysuit"),
    "cmn_suit": ("Suit — Common", "Shared bodysuit"),
    "inf_cloth": ("Outfit — Infiltrator", "Infiltrator outfit"),
    "rec_cloth": ("Outfit — Enforcer", "Enforcer outfit"),
    "tec_cloth": ("Outfit — Scout", "Scout outfit"),
    "avm": ("Avatar", "Created-character body / head / hair"),
    "base": ("Base body", "Base skeleton / body"),
    "arm": ("Arms", "Arm / glove part"), "head": ("Head", "Head / face part"),
    "leg": ("Legs", "Leg / boot part"), "body": ("Body", "Torso / base body"),
    "up_armor": ("Upper armor", "Chest armor"),
    "chest_rig": ("Chest rig", "Webbing / chest rig"),
    "boss": ("Boss", "Boss enemy"), "kaiju": ("Kaiju", "Large creature"),
    "zmb": ("Wanderer", "Zombie enemy"),
}

# 2-letter Survive part prefixes -> category (gender is the 3rd letter).
# hd* is worn headgear (helmets / masks / hats), NOT the head itself -- the
# actual head/face models are the avatar `av[mf]N_typeN_def` presets, handled
# separately in category_and_desc.
PART_PREFIX = {
    "ar": ("Arms", "Arm / glove part"), "hd": ("Headgear", "Helmet / mask / headgear"),
    "lg": ("Legs", "Leg / boot part"), "ua": ("Upper armor", "Chest armor"),
    "bd": ("Body", "Torso / base body"), "cr": ("Chest rig", "Chest rig"),
    "rg": ("Chest rig", "Rig / webbing"),
}


def game_of(path, stem):
    p = path.lower()
    if "survive" in p or "ssd" in p:
        return "Metal Gear Survive"
    if "mgo" in p or "avatar" in p:
        return "MGSV: The Phantom Pain / MGO"
    if "gz" in p or "ground" in p:
        return "MGSV: Ground Zeroes"
    if re.match(r"^(bsm|bsf|emm|emf|ar[fm]|hd[fm]|lg[fm]|ua[fm]|bd[fm]|rg[fm]|bss|kij|zmb)", stem):
        return "Metal Gear Survive"
    return "MGSV: The Phantom Pain / MGO"


def _gender(stem):
    m = re.match(r"^(ar|hd|lg|ua|bd|cr|rg)([fm])\d", stem)
    if m:
        return "Female" if m.group(2) == "f" else "Male"
    if re.search(r"_def_f\b|_f$", stem):
        return "Female"
    if stem.startswith("avf"):
        return "Female"
    if stem.startswith("avm"):
        return "Male"
    return ""


def _folder_from_path(path):
    # rips/<game>/models/<name>/<name>.fbx  -> the part group is not in the path,
    # so fall back to the stem prefix below. But MGO gear keeps a folder hint.
    parts = os.path.normpath(path).split(os.sep)
    for seg in reversed(parts):
        if seg in FOLDERS:
            return seg
    return ""


def category_and_desc(stem, path):
    folder = _folder_from_path(path)
    # Survive part prefix (arf0, hdm7...)
    m = re.match(r"^(ar|hd|lg|ua|bd|cr|rg)[fm]\d", stem)
    if "hair" in stem:
        cat, base = "Hair", "Hairstyle"
    # The real head/face models: avatar face presets av[mf]N_typeN_def -- eyes,
    # mouth, skin and a bandanna. type0..type7 are different face presets.
    elif re.match(r"^av[mf]\d+_type\d+", stem):
        cat, base = "Head", "Head / face (avatar preset)"
    elif re.match(r"^av[mf]\d+_body\d+", stem):
        cat, base = "Base body", "Avatar base body (upper torso)"
    elif "hone" in stem:
        cat, base = "Headgear", "Horn accessory"
    elif not folder and m:
        pre = m.group(1)
        if pre in PART_PREFIX:
            cat, base = PART_PREFIX[pre]
        else:
            cat, base = "Parts", "Part"
    elif folder in FOLDERS:
        cat, base = FOLDERS[folder]
    elif stem.startswith("hat"):
        cat, base = "Headgear", "Hats and headwear"
    elif stem.startswith("gls") or "glass" in stem:
        cat, base = "Eyewear", "Glasses / goggles"
    elif re.match(r"^(bsm|bsf|skl|emm|emf)", stem):
        cat, base = "Base body", "Base skeleton / body"
    else:
        cat, base = "Characters", None

    known = None
    for key in (stem.split("_")[0], stem[:3], stem[:4], stem[:2]):
        if key in CHARS:
            known = CHARS[key]
            break

    bits = []
    if base:
        bits.append(base)
        if known:
            bits.append("(%s)" % known)
    elif known:
        bits.append(known)
    g = _gender(stem)
    if g:
        bits.append(g)
    if "_cov" in stem:
        bits.append("attachment")
    if "hair" in stem:
        bits.append("hairstyle")
    if re.search(r"skin\d", stem):
        bits.append("skin set")
    desc = ", ".join(bits) or "character model"
    return cat, desc


def scan(root, limit=6000):
    """Every model FBX under *root*, described. Prefers the <name>/<name>.fbx
    layout the tool writes but accepts loose FBXs too. Skips the per-clip
    animation packs, which are not models."""
    found = []
    if not root or not os.path.isdir(root):
        return found
    seen = set()
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            if not fn.lower().endswith(".fbx"):
                continue
            stem = os.path.splitext(fn)[0]
            # Keep real model exports, not animation clips. A model always has
            # sidecars (rig.json / maps.tsv / _source / _textures) beside its
            # FBX; the tens of thousands of clip FBXs from an all-animations rip
            # have none, and without this they flood the list and push real
            # models past the scan cap.
            if not discovery.has_model_sidecar(dirpath, stem):
                continue
            path = os.path.join(dirpath, fn)
            game = game_of(path, stem)
            # Dedupe per game, not by name alone: the same model exists in both
            # MGSV/MGO and Survive (e.g. avf0_type0_def), each on its own game's
            # skeleton, and both copies should show so you can pick the one that
            # matches your character's body.
            key = (game, stem)
            if key in seen:
                continue
            seen.add(key)
            cat, desc = category_and_desc(stem, path)
            found.append({
                "name": stem, "path": path,
                "game": game, "category": cat, "desc": desc,
                "gender": _gender(stem) or "Unisex",
            })
            if len(found) >= limit:
                return found
    found.sort(key=lambda r: (r["game"], r["category"], r["name"]))
    return found
